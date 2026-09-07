using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Travel.Api.Endpoints;
using Swashbuckle.AspNetCore.SwaggerUI;
using Travel.Application.Abstractions;
using Travel.Application.Services;
using Travel.Domain.Common;
using Travel.Infrastructure;
using Travel.Infrastructure.Persistence;
using Shared.Observability;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry：统一 Tracing / Metrics / Logging 管线，OTLP 导出目标由环境变量控制
builder.Services.AddObservability("travel-service");

// 组合根：在唯一能引用所有层的地方完成装配
builder.Services.AddScoped<ITravelService, TravelService>();
builder.Services.AddScoped<ITravelImageService, TravelImageService>();
builder.Services.AddInfrastructure(builder.Configuration);

// JWT 验证（ADR-0005）：与 user-service 共享同一组 Jwt 配置（签名密钥/签发者/受众），
// 本服务只验证不签发。足迹归属以 token 中的用户身份为准。
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"] ?? "travel-map",
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"] ?? "travel-map-client",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSection["Key"]
                    ?? throw new InvalidOperationException("Jwt:Key is not configured."))),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// 开发态 CORS：允许 Angular dev server（ng serve，默认 :4200）及旧 nginx 开发端口 :8082
// 跨域调用本服务。演示/开发用途；生产应改为具体前端域名或走网关同源。
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
        policy.WithOrigins("http://localhost:4200", "http://localhost:8082")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// 请求日志中间件：为所有 HTTP 请求生成结构化日志 → OTel → Collector → Loki
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var stopwatch = Stopwatch.StartNew();

    try
    {
        await next(context);
        stopwatch.Stop();

        logger.LogInformation(
            "{Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        logger.LogError(
            ex,
            "{Method} {Path} failed with {StatusCode} in {ElapsedMs}ms",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds);
        throw;
    }
});

// 写入时引用了不存在的用户（user-service 侧 23503 外键违例）→ HTTP 400
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (UnknownUserException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

// 共享 appdb 的 schema 引导（EnsureCreated + CreateTables 兜底 + 跨服务 FK，见 docs/adr/0002）。
// 库内可能已有 user-service 的表，裸 EnsureCreated 会整体跳过，必须走本引导器。
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.EnsureTravelSchema();
}

// Configure the HTTP request pipeline.
// 容器只监听 HTTP(8080)，未配置 HTTPS 终结点与证书，因此关闭 HttpsRedirection，
// 否则所有 HTTP 请求（含 /swagger）会被重定向到不可达的 https 端口而打不开。
// app.UseHttpsRedirection();

// Swagger / OpenAPI 交互界面：开发演示场景始终开启（不受环境限制）。
// 若生产环境不想暴露，可改回用 if (app.Environment.IsDevelopment()) 包裹下面两行。
app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "swagger";
    options.SwaggerEndpoint("/openapi/v1.json", "Travel API v1");
});

// 开发态允许跨域（必须在 MapTravelEndpoints 之前）
app.UseCors("DevCors");

// 认证/授权（JWT 校验须在业务端点映射前启用）
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// 暴露 /metrics 端点供 Prometheus 抓取（必须在 UseCors 之后、Map 之前）
app.UseObservability();

// 旅行记录微服务端点
app.MapTravelEndpoints();

// 健康检查（供容器探针 / 网关使用）
app.MapGet("/health", () => Results.Ok("Healthy"));

app.Run();

// 暴露 Program 类，便于集成测试使用 WebApplicationFactory<Program>
public partial class Program;
