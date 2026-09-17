using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Identity.Application;
using Identity.Application.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Security;

/// <summary>
/// 服务身份的鉴权方案与策略（ADR-0022）。
///
/// 用**两个 JwtBearer 方案**把"用户令牌"和"服务令牌"从受众上彻底分开：
/// 用户令牌 aud = 客户端标识，服务令牌 aud = 被调服务标识。这样
/// "服务令牌不能访问用户资源端点"不靠代码里的 if，而靠方案本身表达出来。
/// </summary>
public static class ServiceIdentity
{
    /// <summary>服务令牌专用的认证方案名。</summary>
    public const string Scheme = "Service";

    /// <summary>要求服务身份的授权策略名。</summary>
    public const string PolicyName = "ServiceIdentity";

    public static IServiceCollection AddServiceIdentityPolicy(this IServiceCollection services)
    {
        services.AddAuthorization(options => options.AddPolicy(PolicyName, policy => policy
            .AddAuthenticationSchemes(Scheme)
            .RequireAuthenticatedUser()));

        return services;
    }

    /// <summary>给两个方案补上校验参数：issuer / 受众 / 只接受 RS256 / 按 kid 找公钥。</summary>
    public sealed class JwtBearerConfiguration(
        ISigningKeyStore signingKeys,
        IOptions<IdentityOptions> options) : IPostConfigureOptions<JwtBearerOptions>
    {
        private readonly IdentityOptions _options = options.Value;

        public void PostConfigure(string? name, JwtBearerOptions bearer)
        {
            bearer.MapInboundClaims = true;   // sub → NameIdentifier，保持与既有服务一致

            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _options.Issuer,
                ValidateAudience = true,
                ValidAudience = name == Scheme ? _options.ServiceId : _options.Audience,
                ValidateIssuerSigningKey = true,
                // 按 kid 到本地密钥集合里取公钥；本服务是签发方，无需走 HTTP 取 JWKS。
                IssuerSigningKeyResolver = (_, _, kid, _) => signingKeys.GetKeys()
                    .Where(k => kid is null || k.Kid == kid)
                    .Select(k => (SecurityKey)new RsaSecurityKey(k.Key) { KeyId = k.Kid })
                    .ToArray(),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                // 算法白名单：显式只接受 RS256，避免算法混淆类问题
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            };
        }
    }

    /// <summary>取调用方标识（服务令牌的 sub 形如 service:xxx）。</summary>
    public static string CallerSubject(this ClaimsPrincipal user)
        => user.FindFirstValue(JwtRegisteredClaimNames.Sub)
           ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? "unknown";
}
