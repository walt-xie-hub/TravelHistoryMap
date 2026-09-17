using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Models;
using Identity.Application.Services;
using Identity.Domain.Entities;
using Moq;
using Xunit;

namespace Identity.UnitTests;

/// <summary>
/// 刷新令牌的核心不变量（ADR-0021）：轮换、重用即整族撤销、凭据版本变化即失效。
/// </summary>
public class RefreshTokenServiceTests
{
    private readonly Mock<IRefreshTokenRepository> _repository = new();
    private readonly Mock<ICredentialReader> _credentials = new();
    private readonly TestClock _clock = new();
    private readonly RecordingAuditLog _audit = new();
    private readonly IdentityOptions _options = new();

    private readonly List<RefreshToken> _added = [];
    private Guid _revokedFamily;
    private int _revokeCalls;

    public RefreshTokenServiceTests()
    {
        _repository.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((token, _) => _added.Add(token))
            .Returns(Task.CompletedTask);
        _repository.Setup(r => r.RevokeFamilyAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTime, CancellationToken>((family, _, _) => { _revokedFamily = family; _revokeCalls++; })
            .ReturnsAsync(1);
    }

    private RefreshTokenService CreateSut() => new(
        _repository.Object,
        _credentials.Object,
        new StubAccessTokenIssuer(),
        _clock,
        _options,
        _audit);

    private static UserCredential ActiveUser(int id = 1, int credentialVersion = 1)
        => new(id, "alice@example.com", "h:p@ssw0rd", true, credentialVersion);

    [Fact]
    public async Task IssueNewFamilyAsync_ReturnsTokensAndStoresOnlyHash()
    {
        var tokens = await CreateSut().IssueNewFamilyAsync(ActiveUser(), _options.Audience);

        Assert.Equal("user-token:1", tokens.AccessToken);
        Assert.Equal(_options.RefreshTokenDays, tokens.RefreshTokenExpiresInDays);

        var stored = Assert.Single(_added);
        Assert.DoesNotContain(tokens.RefreshToken, stored.TokenHash);        // 原文绝不入库
        Assert.Equal(TokenHasher.Hash(tokens.RefreshToken), stored.TokenHash);
        Assert.Equal(1, stored.CredentialVersionAtIssue);
    }

    [Fact]
    public async Task RotateAsync_RotatesTokenAndMarksOldAsReplaced()
    {
        var existing = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = 1,
            ClientId = _options.Audience,
            TokenHash = TokenHasher.Hash("old-token"),
            FamilyId = Guid.NewGuid(),
            IssuedAt = _clock.UtcNow,
            ExpiresAt = _clock.UtcNow.AddDays(7),
            CredentialVersionAtIssue = 1,
        };
        _repository.Setup(r => r.FindByHashAsync(TokenHasher.Hash("old-token"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _credentials.Setup(r => r.FindByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveUser());

        var tokens = await CreateSut().RotateAsync("old-token", "1.2.3.4", "test-agent");

        Assert.NotNull(tokens);
        Assert.NotEqual("old-token", tokens!.RefreshToken);
        Assert.Equal(TokenHasher.Hash(tokens.RefreshToken), existing.ReplacedByHash);   // 旧令牌被标记已轮换
        Assert.Equal(existing.FamilyId, Assert.Single(_added).FamilyId);                // 新令牌留在同一族
        Assert.True(_audit.Contains("token_refreshed"));
    }

    [Fact]
    public async Task RotateAsync_ReusingAnAlreadyRotatedToken_RevokesWholeFamily()
    {
        var familyId = Guid.NewGuid();
        var reused = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = 1,
            TokenHash = TokenHasher.Hash("stolen-token"),
            FamilyId = familyId,
            IssuedAt = _clock.UtcNow,
            ExpiresAt = _clock.UtcNow.AddDays(7),
            ReplacedByHash = "already-rotated",      // 已被用过一次
        };
        _repository.Setup(r => r.FindByHashAsync(TokenHasher.Hash("stolen-token"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reused);

        var tokens = await CreateSut().RotateAsync("stolen-token", null, null);

        Assert.Null(tokens);
        Assert.Equal(familyId, _revokedFamily);
        Assert.True(_audit.Contains("refresh_reuse_detected"));
    }

    [Fact]
    public async Task RotateAsync_WhenCredentialVersionChanged_RevokesFamilyAndRejects()
    {
        var familyId = Guid.NewGuid();
        var existing = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = 1,
            TokenHash = TokenHasher.Hash("old-token"),
            FamilyId = familyId,
            IssuedAt = _clock.UtcNow,
            ExpiresAt = _clock.UtcNow.AddDays(7),
            CredentialVersionAtIssue = 1,       // 签发时是第 1 版
        };
        _repository.Setup(r => r.FindByHashAsync(TokenHasher.Hash("old-token"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        // 用户改过密码 → 现在是第 2 版
        _credentials.Setup(r => r.FindByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveUser(credentialVersion: 2));

        var tokens = await CreateSut().RotateAsync("old-token", null, null);

        Assert.Null(tokens);
        Assert.Equal(familyId, _revokedFamily);
    }

    [Fact]
    public async Task RotateAsync_WhenUserDeactivated_RevokesFamilyAndRejects()
    {
        var existing = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = 1,
            TokenHash = TokenHasher.Hash("old-token"),
            FamilyId = Guid.NewGuid(),
            IssuedAt = _clock.UtcNow,
            ExpiresAt = _clock.UtcNow.AddDays(7),
            CredentialVersionAtIssue = 1,
        };
        _repository.Setup(r => r.FindByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _credentials.Setup(r => r.FindByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserCredential(1, "alice@example.com", "h", false, 1));

        Assert.Null(await CreateSut().RotateAsync("old-token", null, null));
        Assert.Equal(1, _revokeCalls);
    }

    [Fact]
    public async Task RotateAsync_WhenExpired_RejectsWithoutIssuing()
    {
        var existing = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = 1,
            TokenHash = TokenHasher.Hash("old-token"),
            FamilyId = Guid.NewGuid(),
            IssuedAt = _clock.UtcNow.AddDays(-8),
            ExpiresAt = _clock.UtcNow.AddMinutes(-1),
            CredentialVersionAtIssue = 1,
        };
        _repository.Setup(r => r.FindByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        Assert.Null(await CreateSut().RotateAsync("old-token", null, null));
        Assert.Empty(_added);
    }

    [Fact]
    public async Task RotateAsync_UnknownToken_Rejects()
    {
        _repository.Setup(r => r.FindByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        Assert.Null(await CreateSut().RotateAsync("nope", null, null));
    }

    [Fact]
    public async Task RevokeByRawTokenAsync_RevokesFamily()
    {
        var familyId = Guid.NewGuid();
        _repository.Setup(r => r.FindByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshToken { UserId = 7, FamilyId = familyId, ExpiresAt = _clock.UtcNow.AddDays(1) });

        Assert.True(await CreateSut().RevokeByRawTokenAsync("some-token"));

        Assert.Equal(familyId, _revokedFamily);
        Assert.True(_audit.Contains("logged_out"));
    }
}
