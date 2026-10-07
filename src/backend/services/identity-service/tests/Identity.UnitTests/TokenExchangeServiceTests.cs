using Identity.Application.Services;
using Identity.Application.Abstractions;
using Identity.Application.Models;
using Identity.Domain.Entities;
using Identity.Application;
using Moq;
using Xunit;

namespace Identity.UnitTests;

/// <summary>
/// 用户委托 / token exchange（ADR-0024）。
///
/// 这组断言的共同主题是**默认拒绝**：调用方身份、目标服务、主体令牌、scope 四道闸门，
/// 任何一道不满足都不签发；而且“更严”的一点是 —— 委托**必须**显式请求 scope，
/// 否则它就会退化成“用户令牌的副本”。
/// </summary>
public class TokenExchangeServiceTests
{
    private const string SubjectToken = "subject-user-token";

    private readonly Mock<IServiceClientRepository> _clients = new();
    private readonly Mock<IPasswordVerifier> _secrets = new();
    private readonly Mock<ICredentialReader> _credentials = new();
    private readonly Mock<IUserTokenValidator> _subjectTokens = new();
    private readonly RecordingAuditLog _audit = new();
    private readonly IdentityOptions _options = new();

    public TokenExchangeServiceTests()
    {
        _secrets.Setup(s => s.Verify(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string secret, string hash) => hash == "h:" + secret);

        _options.AllowedServiceAudiences.Add("travel-service");
    }

    private TokenExchangeService CreateSut() => new(
        _clients.Object,
        _secrets.Object,
        _credentials.Object,
        _subjectTokens.Object,
        new StubAccessTokenIssuer(),
        _options,
        _audit);

    private void GivenClient(string clientId = "user-service", bool active = true, params string[] scopes)
        => _clients.Setup(r => r.FindByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceClient
            {
                ClientId = clientId,
                ClientSecretHash = "h:s3cret",
                DisplayName = clientId,
                IsActive = active,
                Scopes = [.. scopes.Select(s => new ServiceClientScope { ClientId = clientId, Scope = s })],
            });

    private void GivenSubject(int userId = 42, bool active = true)
    {
        _subjectTokens.Setup(v => v.Validate(SubjectToken)).Returns(userId);
        _credentials.Setup(c => c.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserCredential(userId, "demo@travel.local", "h:p@ssw0rd", active, 1));
    }

    [Fact]
    public async Task 调用方与主体与目标都合法时_签发携带sub与act的委托令牌()
    {
        GivenClient(scopes: "travel:read");
        GivenSubject(userId: 42);

        var result = await CreateSut().ExchangeAsync(
            "user-service", "s3cret", SubjectToken, "travel-service", "travel:read");

        Assert.True(result.Succeeded);
        Assert.Equal("delegated-token:user:42:actor:user-service:travel-service:travel:read", result.AccessToken);
        Assert.Equal("travel:read", result.Scopes);
        Assert.True(_audit.Contains("delegated_token_issued"));
        Assert.Equal("service:user-service", _audit.DataOf("delegated_token_issued", "actor"));
    }

    [Fact]
    public async Task 未知客户端_拒绝()
    {
        GivenSubject();

        var result = await CreateSut().ExchangeAsync(
            "unknown-service", "s3cret", SubjectToken, "travel-service", "travel:read");

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_client", result.Error);
        Assert.True(_audit.Contains("delegated_token_denied"));
    }

    [Fact]
    public async Task 目标服务不在白名单_拒绝()
    {
        GivenClient(scopes: "travel:read");
        GivenSubject();

        var result = await CreateSut().ExchangeAsync(
            "user-service", "s3cret", SubjectToken, "some-other-service", "travel:read");

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_target", result.Error);
    }

    [Fact]
    public async Task 主体令牌无效_拒绝()
    {
        GivenClient(scopes: "travel:read");
        // 主体令牌验不过（过期/伪造/根本不是用户令牌）→ 校验器返回 null

        var result = await CreateSut().ExchangeAsync(
            "user-service", "s3cret", "garbage", "travel-service", "travel:read");

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_grant", result.Error);
    }

    [Fact]
    public async Task 用户已被停用_拒绝_即使主体令牌还在有效期内()
    {
        GivenClient(scopes: "travel:read");
        GivenSubject(userId: 42, active: false);

        var result = await CreateSut().ExchangeAsync(
            "user-service", "s3cret", SubjectToken, "travel-service", "travel:read");

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_grant", result.Error);
    }

    [Fact]
    public async Task 请求了未授予的scope_拒绝()
    {
        GivenClient(scopes: "travel:read");
        GivenSubject();

        var result = await CreateSut().ExchangeAsync(
            "user-service", "s3cret", SubjectToken, "travel-service", "travel:read travel:write");

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_scope", result.Error);
    }

    [Fact]
    public async Task 不请求任何scope_拒绝_否则退化成用户令牌的副本()
    {
        GivenClient(scopes: "travel:read");
        GivenSubject();

        var result = await CreateSut().ExchangeAsync(
            "user-service", "s3cret", SubjectToken, "travel-service", requestedScope: null);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_scope", result.Error);
    }

    [Fact]
    public async Task 调用方凭证无效时_不去触碰主体令牌()
    {
        GivenClient(scopes: "travel:read");
        GivenSubject();

        await CreateSut().ExchangeAsync("user-service", "wrong-secret", SubjectToken, "travel-service", "travel:read");

        // 先验调用方：未认证的请求不应该有机会试探主体令牌
        _subjectTokens.Verify(v => v.Validate(It.IsAny<string>()), Times.Never);
    }
}
