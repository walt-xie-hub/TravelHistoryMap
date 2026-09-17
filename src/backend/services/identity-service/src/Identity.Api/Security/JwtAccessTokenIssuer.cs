using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Security;

/// <summary>
/// RS256 访问令牌签发（ADR-0021）。私钥来自 <see cref="ISigningKeyStore"/>，
/// 令牌头里带 kid，验签方按 kid 到 JWKS 里取公钥——资源服务因此**不需要持有任何密钥**。
/// </summary>
public sealed class JwtAccessTokenIssuer(
    ISigningKeyStore signingKeys,
    IClock clock,
    IOptions<IdentityOptions> options) : IAccessTokenIssuer
{
    private readonly IdentityOptions _options = options.Value;

    public int AccessTokenLifetimeSeconds => _options.AccessTokenMinutes * 60;

    public int ServiceTokenLifetimeSeconds => _options.ServiceTokenMinutes * 60;

    /// <summary>用户令牌：sub = Users.Id（十进制字符串，稳定且不复用，ADR-0021）。</summary>
    public string CreateUserToken(UserCredential user) => Create(
        [
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            // 只带 email：name 等档案字段归 user-service 所有，不做冗余复制（ADR-0020）
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
        ],
        _options.Audience);

    /// <summary>服务令牌：sub = service:&lt;clientId&gt;，aud = 被调服务，scope 表达允许的动作。</summary>
    public string CreateServiceToken(string clientId, string audience, IReadOnlyCollection<string> scopes)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, $"service:{clientId}"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        if (scopes.Count > 0)
            claims.Add(new Claim("scope", string.Join(' ', scopes)));

        return Create(claims, audience);
    }

    private string Create(IEnumerable<Claim> claims, string audience)
    {
        var active = signingKeys.GetActiveKey();
        var now = clock.UtcNow;
        var minutes = audience == _options.Audience ? _options.AccessTokenMinutes : _options.ServiceTokenMinutes;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(minutes),
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(active.Key) { KeyId = active.Kid },
                SecurityAlgorithms.RsaSha256),
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }
}
