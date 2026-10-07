using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Identity.Application;
using Identity.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Security;

/// <summary>
/// 校验本服务签发的**用户令牌**（token exchange 的 subject_token，ADR-0024）。
///
/// 不复用 JwtBearer 中间件：token 端点本身是匿名端点，主体令牌是**请求参数**而不是调用凭据，
/// 所以这里用签名密钥集合做一次离线验签，与 <see cref="ServiceIdentity.JwtBearerConfiguration"/>
/// 取公钥的方式一致（本服务是签发方，不必走 HTTP 拉 JWKS）。
///
/// 两道“只能当主体”的约束（缺一都可能让服务令牌伪装成用户）：
/// <list type="number">
/// <item>aud 必须是客户端标识——服务令牌/委托令牌的 aud 是某个服务，直接不匹配；</item>
/// <item>sub 必须是十进制用户 id——<c>service:xxx</c> 解析不出数字。</item>
/// </list>
/// </summary>
public sealed class DelegatedTokenValidator(
    ISigningKeyStore signingKeys,
    IOptions<IdentityOptions> options) : IUserTokenValidator
{
    private readonly IdentityOptions _options = options.Value;

    public int? Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        // 用 JwtSecurityTokenHandler（与签发端同一套库）：验签是纯 CPU 工作，不需要异步；
        // 这里也不走 JwtBearer 中间件 —— token 端点本身是匿名的，主体令牌是请求参数而非调用凭据。
        // MapInboundClaims=false：保持 sub 原样，避免“主体 id”被隐式改写成别的声明名。
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeyResolver = (_, _, kid, _) => signingKeys.GetKeys()
                .Where(k => kid is null || k.Kid == kid)
                .Select(k => (SecurityKey)new RsaSecurityKey(k.Key) { KeyId = k.Kid })
                .ToArray(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            // 算法白名单：显式只接受 RS256，避免算法混淆
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        };

        try
        {
            var principal = handler.ValidateToken(token, parameters, out _);

            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

            return int.TryParse(sub, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId)
                ? userId
                : null;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            // 验签/解析失败一律当作“无效主体”：fail closed，而不是把异常抛给调用方
            return null;
        }
    }
}
