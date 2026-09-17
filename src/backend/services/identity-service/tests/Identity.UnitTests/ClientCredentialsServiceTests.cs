using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Services;
using Identity.Domain.Entities;
using Moq;
using Xunit;

namespace Identity.UnitTests;

/// <summary>
/// 服务身份（ADR-0022）：默认拒绝——未注册的 client、未放行的 audience、未授予的 scope
/// 都换不到令牌；只有三者都满足才签发。
/// </summary>
public class ClientCredentialsServiceTests
{
    private readonly Mock<IServiceClientRepository> _clients = new();
    private readonly Mock<IPasswordVerifier> _secrets = new();
    private readonly RecordingAuditLog _audit = new();
    private readonly IdentityOptions _options = new();

    public ClientCredentialsServiceTests()
    {
        _secrets.Setup(s => s.Verify(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string secret, string hash) => hash == "h:" + secret);
    }

    private ClientCredentialsService CreateSut() => new(
        _clients.Object,
        _secrets.Object,
        new StubAccessTokenIssuer(),
        _options,
        _audit);

    private void GivenClient(string clientId = "travel-service", bool active = true, params string[] scopes)
        => _clients.Setup(r => r.FindByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceClient
            {
                ClientId = clientId,
                ClientSecretHash = "h:s3cret",
                DisplayName = clientId,
                IsActive = active,
                Scopes = [.. scopes.Select(s => new ServiceClientScope { ClientId = clientId, Scope = s })],
            });

    [Fact]
    public async Task CreateTokenAsync_WithRegisteredClientAndAllowedAudience_IssuesToken()
    {
        _options.AllowedServiceAudiences.Add("user-service");
        GivenClient(scopes: "profile:read");

        var result = await CreateSut().CreateTokenAsync("travel-service", "s3cret", "user-service", "profile:read");

        Assert.True(result.Succeeded);
        Assert.Equal("service-token:travel-service:user-service:profile:read", result.AccessToken);
        Assert.Equal("profile:read", result.Scopes);
        Assert.True(_audit.Contains("service_token_issued"));
    }

    [Fact]
    public async Task CreateTokenAsync_WithUnknownClient_Denies()
    {
        _options.AllowedServiceAudiences.Add("user-service");
        _clients.Setup(r => r.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ServiceClient?)null);

        var result = await CreateSut().CreateTokenAsync("ghost", "s3cret", "user-service", null);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_client", result.Error);
    }

    [Fact]
    public async Task CreateTokenAsync_WithWrongSecret_Denies()
    {
        _options.AllowedServiceAudiences.Add("user-service");
        GivenClient();

        var result = await CreateSut().CreateTokenAsync("travel-service", "wrong", "user-service", null);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_client", result.Error);
    }

    [Fact]
    public async Task CreateTokenAsync_WithInactiveClient_Denies()
    {
        _options.AllowedServiceAudiences.Add("user-service");
        GivenClient(active: false);

        var result = await CreateSut().CreateTokenAsync("travel-service", "s3cret", "user-service", null);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_client", result.Error);
    }

    [Fact]
    public async Task CreateTokenAsync_DefaultDeny_WhenAudienceNotAllowed()
    {
        // AllowedServiceAudiences 默认为空：谁都不该换到服务令牌
        GivenClient();

        var result = await CreateSut().CreateTokenAsync("travel-service", "s3cret", "user-service", null);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_target", result.Error);
    }

    [Fact]
    public async Task CreateTokenAsync_WithUnregisteredScope_Denies()
    {
        _options.AllowedServiceAudiences.Add("user-service");
        GivenClient();          // 没有授予任何 scope

        var result = await CreateSut().CreateTokenAsync("travel-service", "s3cret", "user-service", "profile:read");

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_scope", result.Error);
        Assert.True(_audit.Contains("service_token_denied"));
    }
}
