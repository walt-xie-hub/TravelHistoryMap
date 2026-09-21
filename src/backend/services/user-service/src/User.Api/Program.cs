using System.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using User.Api.Endpoints;
using Swashbuckle.AspNetCore.SwaggerUI;
using User.Application.Abstractions;
using User.Application.Services;
using User.Infrastructure;
using User.Infrastructure.Persistence;
using Shared.Observability;
using Shared.Security;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry：统一 Tracing / Metrics / Logging 管线，OTLP 导出目标由环境变量控制
builder.Services.AddObservability("user-service");

// 组合根：在唯一能引用所有层的地方完成装配
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddInfrastructure(builder.Configuration);

// 令牌校验（ADR-0021）：接受 identity-service 签发的 RS256 令牌（公钥经 OIDC 发现获取），
// 并在共存窗口内继续接受旧的 HS256 令牌（窗口上限 7 天）。
// 本服务**不再签发**令牌：注册与档案归 user-service，认证归 identity-service（ADR-0020）。
builder.Services.AddTravelMapJwt(builder.Configuration, builder.Environment);
builder.Services.AddTravelMapServiceTokenClient(builder.Configuration);
builder.Services.AddAuthorization();

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

// Swagger / OpenAPI 交互界面：**只在 Development 暴露**（生产不公开 API 文档，
// 见 docs/security/observability-and-audit.md）。
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/openapi/v1.json", "User API v1");
    });
}

// 跨域（必须在 MapEndpoints 之前）
app.UseCors("AppCors");

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
