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
/// - 在迁移窗口内仍然接受旧的 HS256 令牌（<c>Jwt:Key</c>），窗口上限 7 天 = 最长令牌生命周期，
///   到期必须删除 <c>Jwt:Key</c> 与 HS256 分支（见 docs/security/authentication.md）；
/// - 算法显式白名单，避免算法混淆类问题。
/// </summary>
public static class TravelMapJwt
{
    public static IServiceCollection AddTravelMapJwt(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var legacyKey = configuration["Jwt:Key"];
        var legacyIssuer = configuration["Jwt:Issuer"] ?? "travel-map";
        var audience = configuration["Jwt:Audience"] ?? "travel-map-client";
        var identityIssuer = configuration["Identity:Issuer"];

        // 共存期结束（identity-service 上线并且旧令牌全部过期）后，把 Jwt:Key 从配置里删掉即可。
        var hasIdentityIssuer = !string.IsNullOrWhiteSpace(identityIssuer);
        if (!hasIdentityIssuer && string.IsNullOrWhiteSpace(legacyKey))
        {
            throw new InvalidOperationException(
                "既没有 Identity:Issuer（RS256 验签）也没有 Jwt:Key（旧 HS256）：令牌无法校验。");
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
                    ValidIssuers = hasIdentityIssuer ? [legacyIssuer, identityIssuer!] : [legacyIssuer],
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),

                    // 显式算法白名单：RSA 用于新令牌，HmacSha256 只服务共存期的旧令牌
                    ValidAlgorithms = hasIdentityIssuer
                        ? [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.HmacSha256]
                        : [SecurityAlgorithms.HmacSha256],
                };

                if (!string.IsNullOrWhiteSpace(legacyKey))
                {
                    parameters.IssuerSigningKeys =
                        [new SymmetricSecurityKey(Encoding.UTF8.GetBytes(legacyKey))];
                }

                options.TokenValidationParameters = parameters;
            });

        return services;
    }
}
