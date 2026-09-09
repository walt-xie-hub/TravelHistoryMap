using Moq;
using Xunit;
using Travel.Application.Abstractions;
using Travel.Application.Services;
using Travel.Domain.Abstractions;
using Travel.Domain.Entities;

namespace Travel.UnitTests;

/// <summary>
/// 应用服务 TravelImageService 单图删除（DeleteAsync）的单元测试。
/// 校验模式与 ADR-0005 一致：记录非本人所有、记录/图片不存在、或图片不属于该记录时一律返回 false
/// （表现层按 404 处理）；媒体文件清理失败（IOException）不影响数据库删除的权威性。
/// </summary>
public class TravelImageServiceTests
{
    private readonly Mock<ITravelRepository> _repositoryMock = new();
    private readonly Mock<ITravelImageStorage> _storageMock = new();
    private readonly TravelImageService _sut;

    public TravelImageServiceTests() => _sut = new TravelImageService(_repositoryMock.Object, _storageMock.Object);

    private static TravelRecord SampleRecord(int id = 1, int userId = 10) => new()
    {
        Id = id,
        UserId = userId,
        LocationName = "Shanghai",
        Latitude = 31.2304m,
        Longitude = 121.4737m,
        ArrivedAt = new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
        DepartedAt = new DateTimeOffset(2026, 5, 3, 8, 0, 0, TimeSpan.Zero),
    };

    private static TravelImage SampleImage(int id = 5, int recordId = 1) => new()
    {
        Id = id,
        TravelRecordId = recordId,
        OriginalFileName = "photo.jpg",
        ContentType = "image/jpeg",
        FileSize = 1024,
        OriginalPath = "/original/abc.jpg",
        ThumbnailPath = "/thumbnail/abc.webp",
    };

    [Fact]
    public async Task DeleteAsync_WhenImageOwnedViaRecord_DeletesMediaAndRowAndReturnsTrue()
    {
        // Arrange
        const int userId = 10;
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord(id: 1, userId: userId));
        _repositoryMock.Setup(r => r.GetImageAsync(5, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleImage(id: 5, recordId: 1));
        _repositoryMock.Setup(r => r.DeleteImageAsync(5, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(true);

        // Act
        var deleted = await _sut.DeleteAsync(userId, 1, 5);

        // Assert
        Assert.True(deleted);
        _storageMock.Verify(s => s.Delete("/original/abc.jpg"), Times.Once);
        _storageMock.Verify(s => s.Delete("/thumbnail/abc.webp"), Times.Once);
        _repositoryMock.Verify(r => r.DeleteImageAsync(5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenImageBelongsToAnotherRecord_ReturnsFalseAndDeletesNothing()
    {
        // Arrange：image 属于记录 2，但调用方以记录 1 的名义删除
        const int userId = 10;
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord(id: 1, userId: userId));
        _repositoryMock.Setup(r => r.GetImageAsync(5, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleImage(id: 5, recordId: 2));

        // Act
        var deleted = await _sut.DeleteAsync(userId, 1, 5);

        // Assert
        Assert.False(deleted);
        _storageMock.Verify(s => s.Delete(It.IsAny<string>()), Times.Never);
        _repositoryMock.Verify(r => r.DeleteImageAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenRecordOwnedByOtherUser_ReturnsFalseAndDeletesNothing()
    {
        // Arrange：记录属于用户 99，当前用户 10 越权删除
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord(id: 1, userId: 99));
        _repositoryMock.Setup(r => r.GetImageAsync(5, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleImage(id: 5, recordId: 1));

        // Act
        var deleted = await _sut.DeleteAsync(10, 1, 5);

        // Assert
        Assert.False(deleted);
        _storageMock.Verify(s => s.Delete(It.IsAny<string>()), Times.Never);
        _repositoryMock.Verify(r => r.DeleteImageAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenRecordMissing_ReturnsFalse()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
                       .ReturnsAsync((TravelRecord?)null);

        // Act
        var deleted = await _sut.DeleteAsync(10, 99, 5);

        // Assert
        Assert.False(deleted);
        _storageMock.Verify(s => s.Delete(It.IsAny<string>()), Times.Never);
        _repositoryMock.Verify(r => r.DeleteImageAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenImageMissing_ReturnsFalse()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord(id: 1, userId: 10));
        _repositoryMock.Setup(r => r.GetImageAsync(99, It.IsAny<CancellationToken>()))
                       .ReturnsAsync((TravelImage?)null);

        // Act
        var deleted = await _sut.DeleteAsync(10, 1, 99);

        // Assert
        Assert.False(deleted);
        _storageMock.Verify(s => s.Delete(It.IsAny<string>()), Times.Never);
        _repositoryMock.Verify(r => r.DeleteImageAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenRowVanishedBetweenCheckAndDelete_ReturnsFalse()
    {
        // Arrange：图片此刻已被删除（如并发删除），DeleteImageAsync 返回 false
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord(id: 1, userId: 10));
        _repositoryMock.Setup(r => r.GetImageAsync(5, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleImage(id: 5, recordId: 1));
        _repositoryMock.Setup(r => r.DeleteImageAsync(5, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(false);

        // Act
        var deleted = await _sut.DeleteAsync(10, 1, 5);

        // Assert
        Assert.False(deleted);
    }

    [Fact]
    public async Task DeleteAsync_WhenMediaCleanupFails_DbDeletionStillAuthoritative()
    {
        // Arrange：存储抛 IOException（如文件已被外部清理）
        const int userId = 10;
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord(id: 1, userId: userId));
        _repositoryMock.Setup(r => r.GetImageAsync(5, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleImage(id: 5, recordId: 1));
        _repositoryMock.Setup(r => r.DeleteImageAsync(5, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(true);
        _storageMock.Setup(s => s.Delete(It.IsAny<string>())).Throws(new IOException("file gone"));

        // Act
        var deleted = await _sut.DeleteAsync(userId, 1, 5);

        // Assert
        Assert.True(deleted);
        _repositoryMock.Verify(r => r.DeleteImageAsync(5, It.IsAny<CancellationToken>()), Times.Once);
    }
}
