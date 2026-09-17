using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Shared.Security;

/// <summary>
/// 资源服务侧的令牌校验（ADR-0021）。**取代**此前两个服务各自复制一份 HS256 校验代码的做法。
///
/// 认证归 identity-service（ADR-0020），资源服务只验签、不签发，因此这里：
/// - RS256 公钥经 OIDC 发现从 identity-service 的 JWKS 获取（<c>Identity:Issuer</c>），
///   服务本身**不再持有任何签名密钥**；
/// - 旧 HS256 令牌**默认不再接受**：必须显式开启 <c>Jwt:AllowLegacyHs256</c> 并给出
///   <c>Jwt:LegacyUntil</c>（不超过 7 天 = 最长令牌生命周期）。窗口到期后自动失效，
///   配置缺失、写错或超期一律让服务**拒绝启动**（见 <see cref="ResolveLegacyWindow"/>）；
/// - 算法显式白名单，避免算法混淆；窗口到期不仅由启动期门控拦下，请求路径上也会再拦一次，
///   因此不需要等 pod 重启就能失效。
/// </summary>
public static class TravelMapJwt
{
    /// <summary>显式开启旧 HS256 校验的配置键。**默认关闭**。</summary>
    public const string AllowLegacyHs256ConfigKey = "Jwt:AllowLegacyHs256";

    /// <summary>共存窗口的到期时刻（ISO-8601 UTC）。开启时必须提供，且不得超过 <see cref="MaxLegacyWindow"/>。</summary>
    public const string LegacyUntilConfigKey = "Jwt:LegacyUntil";

    /// <summary>窗口上限：7 天 = refresh token 的最长寿命。再长就没有"共存"的理由了。</summary>
    public static readonly TimeSpan MaxLegacyWindow = TimeSpan.FromDays(7);

    /// <summary>共存窗口（ADR-0021）。<c>Enabled=false</c> 时其余字段无意义。</summary>
    public readonly record struct LegacyTokenWindow(
        bool Enabled,
        string? Key,
        string? Issuer,
        DateTimeOffset? Until);

    public static IServiceCollection AddTravelMapJwt(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var window = ResolveLegacyWindow(configuration, DateTimeOffset.UtcNow);
        var audience = configuration["Jwt:Audience"] ?? "travel-map-client";
        var identityIssuer = configuration["Identity:Issuer"];
        var hasIdentityIssuer = !string.IsNullOrWhiteSpace(identityIssuer);

        if (!hasIdentityIssuer && !window.Enabled)
        {
            throw new InvalidOperationException(
                $"既没有 Identity:Issuer（RS256 验签）也没开启 {AllowLegacyHs256ConfigKey}：令牌无法校验。");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = true;   // sub → NameIdentifier，与既有代码一致
                options.RequireHttpsMetadata = !environment.IsDevelopment();

                if (hasIdentityIssuer)
                {
                    // 由 OIDC 发现文档拿到 jwks_uri，再取公钥；因此校验方不需要预置密钥。
                    // 注意：issuer 必须能被本服务解析到（dev 用容器名，生产用网关域名）。
                    options.Authority = identityIssuer;
                }

                var parameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers = BuildValidIssuers(window, identityIssuer),
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),

                    // 显式算法白名单：RSA 用于新令牌，HmacSha256 只服务共存期的旧令牌
                    ValidAlgorithms = window.Enabled
                        ? (hasIdentityIssuer
                            ? [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.HmacSha256]
                            : [SecurityAlgorithms.HmacSha256])
                        : [SecurityAlgorithms.RsaSha256],
                };

                if (window.Enabled && window.Key is { Length: > 0 } legacyKey)
                {
                    parameters.IssuerSigningKeys =
                        [new SymmetricSecurityKey(Encoding.UTF8.GetBytes(legacyKey))];
                }

                options.TokenValidationParameters = parameters;

                if (window.Enabled)
                {
                    // 窗口在**运行期**到期也要失效：伸缩组里的 pod 可能几周不重启，
                    // 启动期的门控拦不住它。这里按 issuer 认旧令牌（HS256 时代用的是
                    // <c>Jwt:Issuer</c>，与 identity-service 的 issuer 不同），到期即拒。
                    // 用 SecurityToken.Issuer 而不是 alg 头：不依赖具体令牌实现类型。
                    options.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = context =>
                        {
                            if (window.Until is { } until
                                && DateTimeOffset.UtcNow > until
                                && string.Equals(context.SecurityToken.Issuer, window.Issuer, StringComparison.Ordinal))
                            {
                                context.Fail("HS256 共存窗口已到期（ADR-0021）：请改用 identity-service 签发的 RS256 令牌。");
                            }

                            return Task.CompletedTask;
                        },
                    };
                }
            });

        return services;
    }

    /// <summary>
    /// 解析共存窗口（ADR-0021）。**默认关闭**，只有同时满足下列条件才接受旧 HS256 令牌：
    /// <list type="item">
    /// <item><c>Jwt:AllowLegacyHs256=true</c>（显式开启，而不是"配了 Jwt:Key 就自动接受"）；</item>
    /// <item><c>Jwt:LegacyUntil</c> 是未过期的 ISO-8601 UTC 时间点；</item>
    /// <item>该时间点距今不超过 <see cref="MaxLegacyWindow"/>（7 天 = refresh token 最长寿命）。</item>
    /// </list>
    /// 配置不合法一律**拒绝启动**（fail closed）：窗口"会自己关掉"因此是代码里的事实，
    /// 而不是靠人记得回来删配置。
    /// </summary>
    public static LegacyTokenWindow ResolveLegacyWindow(IConfiguration configuration, DateTimeOffset now)
    {
        if (!bool.TryParse(configuration[AllowLegacyHs256ConfigKey], out var allow) || !allow)
            return new LegacyTokenWindow(false, null, null, null);

        var untilRaw = configuration[LegacyUntilConfigKey];
        if (!DateTimeOffset.TryParse(
                untilRaw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var until))
        {
            throw new InvalidOperationException(
                $"{AllowLegacyHs256ConfigKey}=true 时必须提供 {LegacyUntilConfigKey}（ISO-8601 UTC，例如 2026-09-25T00:00:00Z）。");
        }

        if (until <= now)
        {
            throw new InvalidOperationException(
                $"{LegacyUntilConfigKey}={until:u} 已过期，共存窗口结束：请删掉 {AllowLegacyHs256ConfigKey}、{LegacyUntilConfigKey} 与 Jwt:Key（ADR-0021）。");
        }

        if (until - now > MaxLegacyWindow)
        {
            throw new InvalidOperationException(
                $"{LegacyUntilConfigKey}={until:u} 超出上限：共存窗口最多 {MaxLegacyWindow.TotalDays:0} 天（ADR-0021）。");
        }

        var key = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                $"{AllowLegacyHs256ConfigKey}=true 但没有 Jwt:Key：旧 HS256 令牌无法验签。");
        }

        return new LegacyTokenWindow(
            true,
            key,
            configuration["Jwt:Issuer"] ?? "travel-map",
            until);
    }

    private static string[] BuildValidIssuers(LegacyTokenWindow window, string? identityIssuer)
    {
        var issuers = new List<string>();

        if (!string.IsNullOrWhiteSpace(identityIssuer))
            issuers.Add(identityIssuer);

        // 旧令牌的 issuer 只在窗口内有效；窗口关掉后这个 issuer 也应该被拒。
        if (window.Enabled && !string.IsNullOrWhiteSpace(window.Issuer))
            issuers.Add(window.Issuer);

        return [.. issuers];
    }
}
