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
/// 应用服务 UserService 的单元测试。
/// 通过 Mock 领域仓储（IUserRepository）隔离数据库，验证用例编排与 DTO 映射。
/// </summary>
public class UserServiceTests
{
    private readonly Mock<IUserRepository> _repositoryMock = new();
    private readonly IUserService _sut;

    public UserServiceTests() => _sut = new UserService(_repositoryMock.Object);

    [Fact]
    public async Task GetPagedAsync_ReturnsMappedPagedResult()
    {
        // Arrange
        var page = 1;
        var pageSize = 10;
        var paged = new PagedResult<AppUser>(
            new List<AppUser>
            {
                new() { Id = 1, Name = "Alice", Email = "alice@example.com" },
                new() { Id = 2, Name = "Bob", Email = "bob@example.com" },
            },
            page,
            pageSize,
            TotalCount: 2,
            TotalPages: 1);

        _repositoryMock.Setup(r => r.GetPagedAsync(page, pageSize, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(paged);

        // Act
        var result = await _sut.GetPagedAsync(page, pageSize);

        // Assert
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Alice", result.Items[0].Name);
        Assert.Equal("bob@example.com", result.Items[1].Email);
        // 分页元数据应原样透传
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
    }

    [Fact]
    public async Task GetPagedAsync_ForwardsPageAndPageSizeToRepository()
    {
        // Arrange
        const int page = 3;
        const int pageSize = 20;
        _repositoryMock.Setup(r => r.GetPagedAsync(page, pageSize, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new PagedResult<AppUser>(
                           new List<AppUser>(), page, pageSize, TotalCount: 0, TotalPages: 1));

        // Act
        await _sut.GetPagedAsync(page, pageSize);

        // Assert
        _repositoryMock.Verify(
            r => r.GetPagedAsync(page, pageSize, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_WhenUserExists_ReturnsUserDto()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new AppUser { Id = 1, Name = "Alice", Email = "a@x.com" });

        // Act
        var dto = await _sut.GetByIdAsync(1);

        // Assert
        Assert.NotNull(dto);
        Assert.Equal(1, dto!.Id);
        Assert.Equal("Alice", dto.Name);
    }

    [Fact]
    public async Task GetByIdAsync_WhenUserMissing_ReturnsNull()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
                       .ReturnsAsync((AppUser?)null);

        // Act
        var dto = await _sut.GetByIdAsync(99);

        // Assert
        Assert.Null(dto);
    }

    [Fact]
    public async Task CreateAsync_MapsAndPersistsUser()
    {
        // Arrange
        var created = new AppUser { Id = 7, Name = "Carol", Email = "carol@example.com" };
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<AppUser>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(created);

        var dto = new CreateUserDto("Carol", "carol@example.com");

        // Act
        var result = await _sut.CreateAsync(dto);

        // Assert
        Assert.Equal(7, result.Id);
        Assert.Equal("Carol", result.Name);
        Assert.Equal("carol@example.com", result.Email);

        _repositoryMock.Verify(r => r.AddAsync(
            It.Is<AppUser>(u => u.Name == "Carol" && u.Email == "carol@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WhenUserExists_UpdatesAndReturnsDto()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new AppUser { Id = 1, Name = "Old", Email = "old@x.com" });
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<AppUser>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((AppUser u, CancellationToken _) => u);

        var dto = new UpdateUserDto("New", "new@x.com", "13900000000", "https://cdn/x.png", true);

        // Act
        var result = await _sut.UpdateAsync(1, dto);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
        Assert.Equal("New", result.Name);
        Assert.Equal("new@x.com", result.Email);

        _repositoryMock.Verify(r => r.UpdateAsync(
            It.Is<AppUser>(u => u.Name == "New" && u.Email == "new@x.com"
                                && u.PhoneNumber == "13900000000" && u.IsActive),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WhenUserMissing_ReturnsNull()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
                       .ReturnsAsync((AppUser?)null);

        // Act
        var result = await _sut.UpdateAsync(99, new UpdateUserDto("X", "x@x.com"));

        // Assert
        Assert.Null(result);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<AppUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenUserExists_ReturnsTrue()
    {
        // Arrange
        _repositoryMock.Setup(r => r.DeleteAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(true);

        // Act
        var deleted = await _sut.DeleteAsync(1);

        // Assert
        Assert.True(deleted);
        _repositoryMock.Verify(r => r.DeleteAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenUserMissing_ReturnsFalse()
    {
        // Arrange
        _repositoryMock.Setup(r => r.DeleteAsync(99, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(false);

        // Act
        var deleted = await _sut.DeleteAsync(99);

        // Assert
        Assert.False(deleted);
        _repositoryMock.Verify(r => r.DeleteAsync(99, It.IsAny<CancellationToken>()), Times.Once);
    }
}
