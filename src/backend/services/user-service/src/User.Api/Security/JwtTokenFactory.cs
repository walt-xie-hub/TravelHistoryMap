using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using User.Application.DTOs;

namespace User.Api.Security;

/// <summary>
/// JWT 签发器（组合根组件，仅 user-service 需要）。读取 Jwt:Key/Issuer/Audience/ExpiresInDays 配置
/// （ADR-0005）：HS256、access-token-only、默认 7 天；travel-history 等下游服务用同一组配置做验证。
/// </summary>
public sealed class JwtTokenFactory
{
    private readonly string _key;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiresInDays;

    public JwtTokenFactory(IConfiguration configuration)
    {
        var section = configuration.GetSection("Jwt");
        _key = section["Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        _issuer = section["Issuer"] ?? "travel-map";
        _audience = section["Audience"] ?? "travel-map-client";
        _expiresInDays = int.TryParse(section["ExpiresInDays"], out var days) ? days : 7;
    }

    public string Create(UserDto user)
    {
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Email, user.Email),
            ],
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddDays(_expiresInDays),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
