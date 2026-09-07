using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using User.Api.Endpoints;
using Swashbuckle.AspNetCore.SwaggerUI;
using Microsoft.EntityFrameworkCore;
using User.Api.Security;
using User.Application.Abstractions;
using User.Application.Services;
using User.Infrastructure;
using User.Infrastructure.Persistence;
using Shared.Observability;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry：统一 Tracing / Metrics / Logging 管线，OTLP 导出目标由环境变量控制
builder.Services.AddObservability("user-service");

// 组合根：在唯一能引用所有层的地方完成装配
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddInfrastructure(builder.Configuration);

// JWT 认证（ADR-0005）：自签发 HS256 access token；Key/Issuer/Audience 与 travel-history 共享同一组配置
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddSingleton<JwtTokenFactory>();
// 图片验证码：内存缓存答案 + SkiaSharp 渲染（一次性、5 分钟过期）
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<CaptchaService>();
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

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// 开发态 CORS：允许 Angular dev server（ng serve，任意端口）及旧 nginx 开发端口。
// 使用 localhost / 127.0.0.1 任意端口放行，避免用户通过 127.0.0.1:4200 访问时
// 因 Origin 不匹配导致验证码/登录等请求被浏览器拦截。
// 演示/开发用途；生产应改为具体前端域名或走网关同源。
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
        policy.SetIsOriginAllowed(origin =>
            origin is not null &&
            (origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase) ||
             origin.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase)))
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// 传输安全：生产流量在边缘层（Azure Container Apps 入口 / K8s nginx Ingress）终结 TLS，
// 本服务收到的只是明文 HTTP。此中间件信任边缘层写入的 X-Forwarded-Proto /
// X-Forwarded-For，使 request.Scheme 恢复为 https，保证 OpenAPI/Swagger 链接、
// 绝对 URL 与日志中的协议正确。开发态直连（无该头）则不受影响。
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

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

// 共享 appdb 的 schema 引导（EnsureCreated + CreateTables 兜底，见 docs/adr/0002）。
// 库内可能已有 travel-history 建的表，裸 EnsureCreated 会整体跳过，必须走本引导器。
app.Services.EnsureUserSchema();

// dev 演示账号（ADR-0005）：仅 Development，密码由 DemoUser:Password 提供
if (app.Environment.IsDevelopment())
    await app.Services.SeedDevelopmentUserAsync();

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
    options.SwaggerEndpoint("/openapi/v1.json", "User API v1");
});

// 开发态允许跨域（必须在 MapEndpoints 之前）
app.UseCors("DevCors");

// 认证/授权（JWT 校验须在业务端点映射前启用）
app.UseAuthentication();
app.UseAuthorization();

// 暴露 /metrics 端点供 Prometheus 抓取（必须在 UseCors 之后、Map 之前）
app.UseObservability();

// 认证与用户微服务端点
app.MapAuthEndpoints();
app.MapUserEndpoints();

// 健康检查（供容器探针 / 网关使用）
app.MapGet("/health", () => Results.Ok("Healthy"));

app.Run();

// 暴露 Program 类，便于集成测试使用 WebApplicationFactory<Program>
public partial class Program;
