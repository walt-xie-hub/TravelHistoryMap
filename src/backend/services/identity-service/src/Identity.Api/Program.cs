using System.Diagnostics;
using Identity.Api.Endpoints;
using Identity.Api.Security;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Services;
using Identity.Infrastructure;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Observability;
using Swashbuckle.AspNetCore.SwaggerUI;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry：统一 Tracing / Metrics / Logging 管线，OTLP 导出目标由环境变量控制
builder.Services.AddObservability("identity-service");

// 验证码：内存缓存答案（一次性、5 分钟过期）。归属在这里而不是 user-service，
// 因为它是登录状态机的一部分，必须与"校验方"同进程（ADR-0020）。
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<CaptchaService>();

// 组合根：基础设施（库、只读凭据、签名密钥、仓储）+ 应用服务
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
// token exchange 的主体令牌校验（ADR-0024）：验签用户令牌并取其 sub 作为委托主体
builder.Services.AddSingleton<IUserTokenValidator, DelegatedTokenValidator>();

var identityOptions = new IdentityOptions();
builder.Configuration.GetSection(IdentityOptions.SectionName).Bind(identityOptions);

// 两个 JwtBearer 方案：默认（用户令牌，aud=客户端标识）与 Service（服务令牌，aud=本服务）
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer()
    .AddJwtBearer(ServiceIdentity.Scheme);
builder.Services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, ServiceIdentity.JwtBearerConfiguration>();
builder.Services.AddServiceIdentityPolicy();

// CORS：生产只允许实际前端域名（或走网关同源）；开发态放行 localhost 任意端口。
// 用配置项而不是硬编码，避免把开发期白名单带进生产（docs/security/authorization.md）。
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("App", policy =>
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
}));

var app = builder.Build();

// 传输安全：TLS 在边缘层（ACA 入口 / 网关）终结，本服务收到的是明文 HTTP。
// 信任边缘写入的 X-Forwarded-* 以恢复真实协议与客户端 IP（审计要用）。
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

// 请求日志中间件：结构化日志 → OTel → Collector → Loki
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

// 共享 appdb 的 schema 引导：只建本服务自己的 4 张表，绝不建/改 Users（ADR-0020）
app.Services.EnsureIdentitySchema();

for (var registrationAttempt = 0; registrationAttempt < 5; registrationAttempt++)
{
    try
    {
        await using var registrationScope = app.Services.CreateAsyncScope();
        await registrationScope.ServiceProvider
            .GetRequiredService<ServiceClientRegistrationService>()
            .EnsureRegisteredAsync();
        break;
    }
    catch (DbUpdateException) when (registrationAttempt < 4)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(100 * (registrationAttempt + 1)));
    }
}

// 等 Users 表与 CredentialVersion 列就绪（由 user-service 建/加）。
// 超时仍不就绪则拒绝启动——不在一个凭据契约不完整的库上提供认证。
await app.Services.GetRequiredService<ICredentialReader>().WaitUntilAvailableAsync();

// 运维端点只在 Development 暴露（docs/security/observability-and-audit.md）
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/openapi/v1.json", "Identity API v1");
    });
}

app.UseCors("App");
app.UseAuthentication();
app.UseAuthorization();

// /metrics 供 Prometheus 抓取（仅集群内部可达）
app.UseObservability();

app.MapOidcEndpoints(identityOptions);
app.MapIdentityEndpoints();
app.MapTokenEndpoint();
app.MapInternalEndpoints();

app.MapGet("/health", () => Results.Ok("Healthy"));

app.Run();

// 暴露 Program 类，便于集成测试使用 WebApplicationFactory<Program>
public partial class Program;
