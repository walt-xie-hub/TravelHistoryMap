using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Shared.Security;

/// <summary>
/// 委托令牌的接收侧（ADR-0024）：接受 identity-service 为**本服务**签发的用户委托令牌，
/// 即 sub = 用户、act = 代理服务、aud = 本服务。
///
/// 它与 <see cref="TravelMapJwt"/> 的用户令牌方案是**两个方案**，而不是一个方案加判断：
/// 两者的 sub 语义不同（用户 vs 用户+代理者）、aud 不同，用方案表达出来，端点就不用写 if。
///
/// 为什么必须要求 <c>act</c>：委托令牌与 client_credentials 服务令牌的 aud 都是本服务标识，
/// 光看 aud 无法区分。act 是服务令牌**不会带**的声明，因此它是可靠的判别位 ——
/// 少了它，一枚普通服务令牌就能冒充“代表某用户”。
/// </summary>
public static class DelegatedIdentity
{
    /// <summary>委托令牌专用的认证方案名。</summary>
    public const string Scheme = "Delegated";

    /// <summary>要求“用户委托”身份的授权策略名。</summary>
    public const string PolicyName = "DelegatedIdentity";

    /// <summary>RFC 8693 的代理链声明（嵌套 JSON，形如 <c>{"sub":"service:user-service"}</c>）。</summary>
    public const string ActClaimType = "act";

    /// <summary>
    /// 本服务作为被调方的标识，即委托令牌的 aud。取 <c>ServiceIdentity:ClientId</c>：
    /// 它本来就表示“本服务在 identity 注册的身份”，与 <c>Identity:AllowedServiceAudiences</c>
    /// 里登记的名字是同一个东西。缺失即拒绝启动——否则会静默接受任意 audience 的令牌。
    /// </summary>
    public static string ResolveAudience(IConfiguration configuration)
    {
        var clientId = configuration[$"{ServiceIdentityOptions.SectionName}:ClientId"];

        if (string.IsNullOrWhiteSpace(clientId) || clientId == ServiceIdentityOptions.EnvironmentPlaceholder)
        {
            throw new InvalidOperationException(
                $"缺少 {ServiceIdentityOptions.SectionName}:ClientId：它是本服务的标识，也是委托令牌 aud 的期望值。");
        }

        return clientId;
    }

    /// <summary>
    /// 注册委托令牌的认证方案与授权策略。必须在 <see cref="TravelMapJwt.AddTravelMapJwt"/> 之后调用
    /// （后者负责设置默认方案）。
    /// </summary>
    public static IServiceCollection AddTravelMapDelegatedIdentity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var issuer = configuration["Identity:Issuer"];

        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new InvalidOperationException(
                "缺少 Identity:Issuer：委托令牌的签发方（也是 OIDC 发现地址）无法确定。");
        }

        var audience = ResolveAudience(configuration);

        services.AddAuthentication()
            .AddJwtBearer(Scheme, options =>
            {
                options.MapInboundClaims = true;   // sub → NameIdentifier，与既有服务读取用户 id 的方式一致
                options.RequireHttpsMetadata = !environment.IsDevelopment();

                // 公钥经 OIDC 发现（jwks_uri）获取，本服务不持任何密钥
                options.Authority = issuer;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    // aud 必须就是本服务：一个委托令牌只对一个目标有效
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    // 算法白名单：显式只接受 RS256
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                };
            });

        services.AddAuthorization(options => options.AddPolicy(PolicyName, policy => policy
            .AddAuthenticationSchemes(Scheme)
            .RequireAuthenticatedUser()
            // 服务令牌没有 act；要求它存在，等于要求“这是一枚用户委托令牌”
            .RequireClaim(ActClaimType)));

        return services;
    }

    /// <summary>
    /// 取代理发起方（如 <c>service:user-service</c>）。格式不符或缺失返回 null ——
    /// 调用方应把它当“审计/日志字段”，不要用它做授权判断；授权请用策略与 scope。
    /// </summary>
    public static string? ActingService(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst(ActClaimType)?.Value;
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            return document.RootElement.TryGetProperty("sub", out var sub)
                ? sub.GetString()
                : null;
        }
        catch (JsonException)
        {
            // act 必须是 RFC 8693 的嵌套 JSON 形状；扁平串一律视为不可用，避免两种形状并存
            return null;
        }
    }

    /// <summary>
    /// 委托主体（被代表的用户）。不是十进制 id 就返回 null —— 服务令牌的 sub 是 <c>service:xxx</c>，
    /// 因此业务代码拿到 null 时应当拒绝，而不是回落到 0。
    /// </summary>
    public static int? DelegatedUserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub");

        return int.TryParse(sub, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId)
            ? userId
            : null;
    }
}
