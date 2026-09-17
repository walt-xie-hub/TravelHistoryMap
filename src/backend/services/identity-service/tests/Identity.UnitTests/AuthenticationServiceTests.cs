using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Models;
using Identity.Application.Services;
using Identity.Domain.Common;
using Identity.Domain.Entities;
using Moq;
using Xunit;

namespace Identity.UnitTests;

/// <summary>
/// 登录路径的不变量：失败原因一律不可区分、失败计数与锁定生效、停用账号拒绝、
/// 账号不存在时也走一次哈希校验（响应时间不泄露账号是否存在）。
/// </summary>
public class AuthenticationServiceTests
{
    private readonly Mock<ICredentialReader> _credentials = new();
    private readonly Mock<IPasswordVerifier> _passwords = new();
    private readonly Mock<ILoginAttemptRepository> _attempts = new();
    private readonly Mock<IRefreshTokenRepository> _refreshRepository = new();
    private readonly TestClock _clock = new();
    private readonly RecordingAuditLog _audit = new();
    private readonly IdentityOptions _options = new();

    private LoginAttempt? _storedAttempt;

    public AuthenticationServiceTests()
    {
        _passwords.SetupGet(p => p.DummyHash).Returns("dummy-hash");
        _passwords.Setup(p => p.Verify(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string password, string hash) => hash == "h:" + password);

        _attempts.Setup(r => r.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _storedAttempt);
        _attempts.Setup(r => r.AddAsync(It.IsAny<LoginAttempt>(), It.IsAny<CancellationToken>()))
            .Callback<LoginAttempt, CancellationToken>((attempt, _) => _storedAttempt = attempt)
            .Returns(Task.CompletedTask);
    }

    private AuthenticationService CreateSut() => new(
        _credentials.Object,
        _passwords.Object,
        _attempts.Object,
        new RefreshTokenService(
            _refreshRepository.Object,
            _credentials.Object,
            new StubAccessTokenIssuer(),
            _clock,
            _options,
            _audit),
        _clock,
        _options,
        _audit);

    private static UserCredential ActiveUser(int id = 1) => new(id, "alice@example.com", "h:p@ssw0rd", true, 1);

    [Fact]
    public async Task LoginAsync_WithValidCredentials_IssuesTokensAndAudits()
    {
        _credentials.Setup(r => r.FindByEmailAsync("alice@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveUser());

        var tokens = await CreateSut().LoginAsync("ALICE@example.com ", "p@ssw0rd", "1.2.3.4", "agent");

        Assert.Equal("user-token:1", tokens.AccessToken);
        Assert.NotEmpty(tokens.RefreshToken);
        Assert.True(_audit.Contains("login_succeeded"));
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ThrowsAndRecordsFailure()
    {
        _credentials.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveUser());

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => CreateSut().LoginAsync("alice@example.com", "wrong", null, null));

        Assert.Equal(1, _storedAttempt!.FailedCount);
        Assert.True(_audit.Contains("login_failed"));
    }

    [Fact]
    public async Task LoginAsync_WithUnknownAccount_ThrowsAndStillHashesOnce()
    {
        _credentials.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserCredential?)null);

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => CreateSut().LoginAsync("nobody@example.com", "whatever", null, null));

        // 关键：账号不存在时也调用一次 Verify（用 DummyHash），把响应耗时拉平
        _passwords.Verify(p => p.Verify("whatever", "dummy-hash"), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WithDeactivatedAccount_Throws()
    {
        _credentials.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserCredential(1, "alice@example.com", "h:p@ssw0rd", false, 1));

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => CreateSut().LoginAsync("alice@example.com", "p@ssw0rd", null, null));
    }

    [Fact]
    public async Task LoginAsync_AfterMaxFailedAttempts_LocksAccount()
    {
        _credentials.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveUser());

        var sut = CreateSut();
        for (var i = 0; i < _options.Lockout.MaxFailedAttempts; i++)
        {
            await Assert.ThrowsAsync<InvalidCredentialsException>(
                () => sut.LoginAsync("alice@example.com", "wrong", null, null));
        }

        Assert.NotNull(_storedAttempt!.LockedUntil);
        Assert.True(_audit.Contains("login_locked"));
    }

    [Fact]
    public async Task LoginAsync_WhenLocked_RejectsWithoutTouchingCredentials()
    {
        _storedAttempt = new LoginAttempt
        {
            Key = "alice@example.com",
            FailedCount = _options.Lockout.MaxFailedAttempts,
            FirstFailedAt = _clock.UtcNow,
            LockedUntil = _clock.UtcNow.AddMinutes(10),
        };

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => CreateSut().LoginAsync("alice@example.com", "p@ssw0rd", null, null));

        // 锁定期间连查询都不做：既省资源，也不给爆破者任何时序差异
        _credentials.Verify(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(_audit.Contains("login_locked"));
    }

    [Fact]
    public async Task LoginAsync_AfterSuccessfulLogin_ClearsFailureCounter()
    {
        _storedAttempt = new LoginAttempt
        {
            Key = "alice@example.com",
            FailedCount = 3,
            FirstFailedAt = _clock.UtcNow,
        };
        _credentials.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveUser());

        await CreateSut().LoginAsync("alice@example.com", "p@ssw0rd", null, null);

        Assert.Equal(0, _storedAttempt.FailedCount);
        Assert.Null(_storedAttempt.LockedUntil);
    }

    [Fact]
    public async Task LoginAsync_FailuresOutsideWindow_DoNotAccumulate()
    {
        _storedAttempt = new LoginAttempt
        {
            Key = "alice@example.com",
            FailedCount = _options.Lockout.MaxFailedAttempts - 1,
            FirstFailedAt = _clock.UtcNow.AddMinutes(-_options.Lockout.WindowMinutes - 1),   // 窗口已过
        };
        _credentials.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveUser());

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => CreateSut().LoginAsync("alice@example.com", "wrong", null, null));

        Assert.Equal(1, _storedAttempt.FailedCount);       // 重新从 1 开始
        Assert.Null(_storedAttempt.LockedUntil);
    }
}
