using Moq;
using Shared.Observability;
using User.Application.Abstractions;
using User.Application.Services;
using User.Domain.Abstractions;
using User.Domain.Common;
using User.Domain.Entities;
using Xunit;

namespace User.UnitTests;

/// <summary>
/// 改密的审计承诺（docs/security/authentication.md 的 P1 事件表）：
/// 成功改密必须留下 <c>password_changed</c> 事件并带上来源信息；失败则**不记任何事件**。
///
/// 为什么值得单独测：改密是"凭据被改写"唯一直接的审计证据；而失败路径若也发事件，
/// 审计日志就会退化成一份口令猜测记录（还会把别人的重试噪声灌进去）。
/// </summary>
public class ChangePasswordAuditTests
{
    private readonly Mock<IUserRepository> _repositoryMock = new();
    private readonly Mock<IPasswordHasher> _hasherMock = new();
    private readonly RecordingAuditLog _audit = new();
    private readonly UserService _sut;

    public ChangePasswordAuditTests()
    {
        // 与 UserServiceTests 相同的简单桩：Hash(p) => "h:{p}"，Verify(p, hash) => hash == "h:{p}"
        _hasherMock.Setup(h => h.Hash(It.IsAny<string>()))
                   .Returns((string p) => "h:" + p);
        _hasherMock.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()))
                   .Returns((string p, string hash) => hash == "h:" + p);

        _sut = new UserService(_repositoryMock.Object, _hasherMock.Object, _audit);
    }

    [Fact]
    public async Task ChangePasswordAsync_OnSuccess_EmitsPasswordChangedWithRequestContext()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new AppUser { Id = 1, Email = "a@x.com", PasswordHash = "h:old-pass" });

        await _sut.ChangePasswordAsync(1, "old-pass", "new-pass", "203.0.113.7", "unit-test-agent");

        var auditEvent = Assert.Single(_audit.Events);
        Assert.Equal(AuditEventNames.PasswordChanged, auditEvent.Event);
        Assert.Equal("success", auditEvent.Result);
        Assert.Equal("user:1", auditEvent.Subject);
        Assert.Equal("203.0.113.7", auditEvent.Ip);
        Assert.Equal("unit-test-agent", auditEvent.UserAgent);
    }

    [Fact]
    public async Task ChangePasswordAsync_WithWrongCurrentPassword_EmitsNothing()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new AppUser { Id = 1, Email = "a@x.com", PasswordHash = "h:old-pass" });

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _sut.ChangePasswordAsync(1, "wrong", "new-pass", null, null));

        Assert.Empty(_audit.Events);
    }
}
