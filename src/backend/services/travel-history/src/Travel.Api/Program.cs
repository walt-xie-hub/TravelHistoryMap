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
using Shared.Security;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry：统一 Tracing / Metrics / Logging 管线，OTLP 导出目标由环境变量控制
builder.Services.AddObservability("travel-service");

// 组合根：在唯一能引用所有层的地方完成装配
builder.Services.AddScoped<ITravelService, TravelService>();
builder.Services.AddScoped<ITravelImageService, TravelImageService>();
builder.Services.AddInfrastructure(builder.Configuration);

// 令牌校验（ADR-0021）：接受 identity-service 签发的 RS256 令牌（公钥经 OIDC 发现获取），
// 并在共存窗口内继续接受旧的 HS256 令牌。本服务只验签、不签发。
builder.Services.AddTravelMapJwt(builder.Configuration, builder.Environment);
builder.Services.AddTravelMapServiceTokenClient(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// CORS：生产只允许实际前端域名（或走网关同源）；开发态回落到 localhost 任意端口。
// 用配置项而不是硬编码，避免把开发期白名单带进生产（docs/security/authorization.md）。
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("AppCors", policy =>
    {
        if (corsOrigins.Length == 0)
        {
            policy.SetIsOriginAllowed(origin =>
                origin is not null &&
                (origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase) ||
                 origin.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase)));
        }
        else
        {
            policy.WithOrigins(corsOrigins);
        }

        policy.AllowAnyHeader().AllowAnyMethod();
    });
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

// Swagger / OpenAPI 交互界面：**只在 Development 暴露**（生产不公开 API 文档，
// 见 docs/security/observability-and-audit.md）。
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/openapi/v1.json", "Travel API v1");
    });
}

// 跨域（必须在 MapTravelEndpoints 之前）
app.UseCors("AppCors");

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
