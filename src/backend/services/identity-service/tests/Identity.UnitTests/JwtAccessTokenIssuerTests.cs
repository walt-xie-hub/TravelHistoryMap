using System.Security.Cryptography;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using Identity.Api.Security;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Models;
using Microsoft.Extensions.Options;
using Xunit;

namespace Identity.UnitTests;

/// <summary>
/// 委托令牌的**形状**（ADR-0024）。这里不用桩、直接用真签发器读回令牌，
/// 因为要证明的恰恰是“线上会发出什么”：
/// <list type="bullet">
/// <item>sub 是用户的十进制 id（不是 service:*）；</item>
/// <item>aud 是被调服务，一个令牌只对一个目标有效；</item>
/// <item>act 是 RFC 8693 的**嵌套 JSON** —— 接收侧按这个形状解析，写成扁平串就对不上；</item>
/// <item>沿用服务令牌的短 TTL，而不是用户令牌的 20 分钟。</item>
/// </list>
/// </summary>
public class JwtAccessTokenIssuerTests
{
    private sealed class StubSigningKeyStore : ISigningKeyStore
    {
        private readonly SigningKey _key = new("test-kid", RSA.Create(2048), DateTime.UnixEpoch);

        public IReadOnlyList<SigningKey> GetKeys() => [_key];

        public SigningKey GetActiveKey() => _key;
    }

    private static JwtAccessTokenIssuer CreateIssuer(IdentityOptions options, TestClock clock)
        => new(new StubSigningKeyStore(), clock, Options.Create(options));

    private static IdentityOptions DefaultOptions() => new()
    {
        Issuer = "http://localhost:8090/identity",
        AccessTokenMinutes = 20,
        ServiceTokenMinutes = 5,
    };

    private static JwtSecurityToken Read(string jwt) => new JwtSecurityTokenHandler().ReadJwtToken(jwt);

    [Fact]
    public void 委托令牌_sub是用户id_aud是被调服务_act是嵌套JSON()
    {
        var issuer = CreateIssuer(DefaultOptions(), new TestClock());

        var parsed = Read(issuer.CreateDelegatedToken(42, "user-service", "travel-service", ["travel:read"]));

        Assert.Equal("http://localhost:8090/identity", parsed.Issuer);
        Assert.Equal("42", parsed.Subject);
        Assert.Equal("travel-service", Assert.Single(parsed.Audiences));
        Assert.Equal("travel:read", parsed.Claims.Single(c => c.Type == "scope").Value);

        var act = parsed.Claims.Single(c => c.Type == "act").Value;
        using var document = JsonDocument.Parse(act);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Equal("service:user-service", document.RootElement.GetProperty("sub").GetString());
    }

    [Fact]
    public void 委托令牌沿用服务令牌的短TTL_而不是用户令牌的20分钟()
    {
        var issuer = CreateIssuer(DefaultOptions(), new TestClock());

        var parsed = Read(issuer.CreateDelegatedToken(42, "user-service", "travel-service", ["travel:read"]));

        Assert.Equal(5, (parsed.ValidTo - parsed.ValidFrom).TotalMinutes, 1);
    }

    [Fact]
    public void 用户令牌仍是20分钟_aud仍是客户端标识()
    {
        var options = DefaultOptions();
        var issuer = CreateIssuer(options, new TestClock());

        var parsed = Read(issuer.CreateUserToken(new UserCredential(7, "demo@travel.local", "h:p", true, 1)));

        Assert.Equal("7", parsed.Subject);
        Assert.Equal(options.Audience, Assert.Single(parsed.Audiences));
        Assert.Equal(20, (parsed.ValidTo - parsed.ValidFrom).TotalMinutes, 1);
        // 用户令牌没有 act：接收侧正是靠这一点区分“用户直连”与“服务代调”
        Assert.DoesNotContain(parsed.Claims, c => c.Type == "act");
    }

    [Fact]
    public void 服务令牌的sub仍是service前缀_且没有act()
    {
        var issuer = CreateIssuer(DefaultOptions(), new TestClock());

        var parsed = Read(issuer.CreateServiceToken("travel-service", "user-service", ["profile:read"]));

        Assert.Equal("service:travel-service", parsed.Subject);
        Assert.DoesNotContain(parsed.Claims, c => c.Type == "act");
    }
}
