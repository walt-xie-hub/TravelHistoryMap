using Moq;
using Xunit;
using User.Application.Abstractions;
using User.Application.DTOs;
using User.Application.Services;
using User.Domain.Abstractions;
using User.Domain.Common;
using User.Domain.Entities;

namespace User.UnitTests;

/// <summary>
/// 应用服务 UserService 的单元测试（ADR-0005 认证用例）。
/// 通过 Mock 领域仓储（IUserRepository）与密码哈希（IPasswordHasher）隔离数据库与算法。
/// </summary>
public class UserServiceTests
{
    private readonly Mock<IUserRepository> _repositoryMock = new();
    private readonly Mock<IPasswordHasher> _hasherMock = new();
    private readonly IUserService _sut;

    public UserServiceTests()
    {
        // 简单桩：Hash(p) => "h:{p}"；Verify(p, hash) => hash == "h:{p}"
        _hasherMock.Setup(h => h.Hash(It.IsAny<string>()))
                   .Returns((string p) => "h:" + p);
        _hasherMock.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()))
                   .Returns((string p, string hash) => hash == "h:" + p);

        _sut = new UserService(_repositoryMock.Object, _hasherMock.Object);
    }

    [Fact]
    public async Task RegisterAsync_PersistsUserWithHashedPasswordAndNormalizedEmail()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByEmailAsync("alice@example.com", It.IsAny<CancellationToken>()))
                       .ReturnsAsync((AppUser?)null);
        var created = new AppUser { Id = 1, Name = "Alice", Email = "alice@example.com", PasswordHash = "h:p@ssw0rd" };
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<AppUser>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(created);

        // Act
        var result = await _sut.RegisterAsync(" Alice ", "ALICE@Example.com ", "p@ssw0rd");

        // Assert
        Assert.Equal(1, result.Id);
        _repositoryMock.Verify(r => r.AddAsync(
            It.Is<AppUser>(u => u.Name == "Alice"
                                && u.Email == "alice@example.com"      // 邮箱小写规范化
                                && u.PasswordHash == "h:p@ssw0rd"),     // 明文绝不入库
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WhenEmailExists_ThrowsEmailAlreadyExists()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByEmailAsync("alice@example.com", It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new AppUser { Id = 1, Email = "alice@example.com" });

        // Act / Assert
        await Assert.ThrowsAsync<EmailAlreadyExistsException>(
            () => _sut.RegisterAsync("Alice", "alice@example.com", "p@ssw0rd"));
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<AppUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsUser()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByEmailAsync("alice@example.com", It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new AppUser { Id = 1, Name = "Alice", Email = "alice@example.com", PasswordHash = "h:p@ssw0rd" });

        // Act
        var user = await _sut.LoginAsync("alice@example.com", "p@ssw0rd");

        // Assert
        Assert.Equal(1, user.Id);
        Assert.Equal("Alice", user.Name);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ThrowsInvalidCredentials()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByEmailAsync("alice@example.com", It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new AppUser { Id = 1, Email = "alice@example.com", PasswordHash = "h:p@ssw0rd" });

        // Act / Assert
        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _sut.LoginAsync("alice@example.com", "wrong-password"));
    }

    [Fact]
    public async Task LoginAsync_WhenUserHasNoPasswordHash_ThrowsInvalidCredentials()
    {
        // 旧数据/无凭据用户一律无法登录（不泄露“账号是否存在”）
        _repositoryMock.Setup(r => r.GetByEmailAsync("legacy@example.com", It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new AppUser { Id = 2, Email = "legacy@example.com", PasswordHash = null });

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _sut.LoginAsync("legacy@example.com", "anything"));
    }

    [Fact]
    public async Task GetByIdAsync_WhenUserExists_ReturnsUserDto()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new AppUser { Id = 1, Name = "Alice", Email = "a@x.com", PhoneNumber = "139", AvatarUrl = "https://x/a.png" });

        var dto = await _sut.GetByIdAsync(1);

        Assert.NotNull(dto);
        Assert.Equal("139", dto!.PhoneNumber);
        Assert.Equal("https://x/a.png", dto.AvatarUrl);
    }

    [Fact]
    public async Task GetByIdAsync_WhenUserMissing_ReturnsNull()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
                       .ReturnsAsync((AppUser?)null);

        Assert.Null(await _sut.GetByIdAsync(99));
    }

    [Fact]
    public async Task UpdateProfileAsync_UpdatesFieldsAndKeepsIsActive()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new AppUser { Id = 1, Name = "Old", Email = "old@x.com", IsActive = false });
        _repositoryMock.Setup(r => r.AnyByEmailAsync("new@x.com", 1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(false);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<AppUser>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((AppUser u, CancellationToken _) => u);

        // Act
        var result = await _sut.UpdateProfileAsync(1, new UpdateProfileDto("New", "new@x.com", "13900000000", "https://cdn/x.png"));

        // Assert
        Assert.NotNull(result);
        Assert.Equal("new@x.com", result!.Email);
        Assert.Equal("https://cdn/x.png", result.AvatarUrl);
        _repositoryMock.Verify(r => r.UpdateAsync(
            It.Is<AppUser>(u => u.Name == "New"
                                && u.Email == "new@x.com"
                                && u.PhoneNumber == "13900000000"
                                && !u.IsActive),      // 不触碰启用状态
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenEmailTakenByOther_ThrowsEmailAlreadyExists()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new AppUser { Id = 1, Name = "A", Email = "a@x.com" });
        _repositoryMock.Setup(r => r.AnyByEmailAsync("b@x.com", 1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(true);

        await Assert.ThrowsAsync<EmailAlreadyExistsException>(
            () => _sut.UpdateProfileAsync(1, new UpdateProfileDto("A", "b@x.com")));
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<AppUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenUserMissing_ThrowsUserNotFound()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
                       .ReturnsAsync((AppUser?)null);

        await Assert.ThrowsAsync<UserNotFoundException>(
            () => _sut.UpdateProfileAsync(99, new UpdateProfileDto("X", "x@x.com")));
    }

    [Fact]
    public async Task ChangePasswordAsync_WithValidCurrentPassword_RehashesNewPassword()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new AppUser { Id = 1, Email = "a@x.com", PasswordHash = "h:old-pass" });
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<AppUser>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((AppUser u, CancellationToken _) => u);

        // Act
        await _sut.ChangePasswordAsync(1, "old-pass", "new-pass");

        // Assert：新密码已哈希且旧密码哈希被替换
        _repositoryMock.Verify(r => r.UpdateAsync(
            It.Is<AppUser>(u => u.PasswordHash == "h:new-pass"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePasswordAsync_WithWrongCurrentPassword_ThrowsInvalidCredentials()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new AppUser { Id = 1, Email = "a@x.com", PasswordHash = "h:old-pass" });

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _sut.ChangePasswordAsync(1, "wrong", "new-pass"));
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<AppUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenUserHasNoPassword_ThrowsInvalidCredentials()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new AppUser { Id = 1, Email = "a@x.com", PasswordHash = null });

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _sut.ChangePasswordAsync(1, "anything", "new-pass"));
    }
}
