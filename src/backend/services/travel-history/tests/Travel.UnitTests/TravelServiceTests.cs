using Moq;
using Xunit;
using Travel.Application.Abstractions;
using Travel.Application.DTOs;
using Travel.Application.Services;
using Travel.Domain.Abstractions;
using Travel.Domain.Common;
using Travel.Domain.Entities;

namespace Travel.UnitTests;

/// <summary>
/// 应用服务 TravelService 的单元测试。
/// 通过 Mock 领域仓储（ITravelRepository）隔离数据库，验证用例编排与 DTO 映射。
/// </summary>
public class TravelServiceTests
{
    private readonly Mock<ITravelRepository> _repositoryMock = new();
    private readonly ITravelService _sut;

    public TravelServiceTests() => _sut = new TravelService(_repositoryMock.Object);

    private static readonly DateTimeOffset Arrived = new(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Departed = new(2026, 5, 3, 8, 0, 0, TimeSpan.Zero);

    private static TravelRecord SampleRecord(int id = 1, int userId = 10) => new()
    {
        Id = id,
        UserId = userId,
        LocationName = "Shanghai",
        Latitude = 31.2304m,
        Longitude = 121.4737m,
        ArrivedAt = Arrived,
        DepartedAt = Departed,
    };

    [Fact]
    public async Task GetPagedAsync_ReturnsMappedPagedResult()
    {
        // Arrange
        const int userId = 10;
        const int page = 1;
        const int pageSize = 10;
        var paged = new PagedResult<TravelRecord>(
            new List<TravelRecord> { SampleRecord(1), SampleRecord(2) },
            page,
            pageSize,
            TotalCount: 2,
            TotalPages: 1);

        _repositoryMock.Setup(r => r.GetPagedAsync(userId, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                                                   page, pageSize, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(paged);

        // Act
        var result = await _sut.GetPagedAsync(userId, from: null, to: null, page, pageSize);

        // Assert
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Shanghai", result.Items[0].LocationName);
        Assert.Equal(31.2304m, result.Items[0].Latitude);
        // 分页元数据应原样透传
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
    }

    [Fact]
    public async Task GetPagedAsync_ForwardsFiltersAndPageToRepository()
    {
        // Arrange
        const int userId = 10;
        const int page = 3;
        const int pageSize = 20;
        var from = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        _repositoryMock.Setup(r => r.GetPagedAsync(userId, from, to, page, pageSize, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new PagedResult<TravelRecord>(
                           new List<TravelRecord>(), page, pageSize, TotalCount: 0, TotalPages: 1));

        // Act
        await _sut.GetPagedAsync(userId, from, to, page, pageSize);

        // Assert
        _repositoryMock.Verify(
            r => r.GetPagedAsync(userId, from, to, page, pageSize, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_WhenRecordExists_ReturnsRecordDto()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord());

        // Act
        var dto = await _sut.GetByIdAsync(1);

        // Assert
        Assert.NotNull(dto);
        Assert.Equal(1, dto!.Id);
        Assert.Equal(10, dto.UserId);
        Assert.Equal("Shanghai", dto.LocationName);
    }

    [Fact]
    public async Task GetByIdAsync_WhenRecordMissing_ReturnsNull()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
                       .ReturnsAsync((TravelRecord?)null);

        // Act
        var dto = await _sut.GetByIdAsync(99);

        // Assert
        Assert.Null(dto);
    }

    [Fact]
    public async Task CreateAsync_MapsAndPersistsRecord()
    {
        // Arrange
        var created = SampleRecord(7);
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<TravelRecord>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(created);

        var dto = new CreateTravelDto(10, "Shanghai", 31.2304m, 121.4737m, Arrived, Departed);

        // Act
        var result = await _sut.CreateAsync(dto);

        // Assert
        Assert.Equal(7, result.Id);
        Assert.Equal(10, result.UserId);
        Assert.Equal("Shanghai", result.LocationName);
        Assert.Equal(Departed, result.DepartedAt);

        _repositoryMock.Verify(r => r.AddAsync(
            It.Is<TravelRecord>(t => t.UserId == 10 && t.LocationName == "Shanghai"
                                     && t.ArrivedAt == Arrived && t.DepartedAt == Departed),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithNullDepartedAt_PersistsOngoingStay()
    {
        // Arrange
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<TravelRecord>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((TravelRecord t, CancellationToken _) => t);
        var dto = new CreateTravelDto(10, "Shanghai", 31.2304m, 121.4737m, Arrived, DepartedAt: null);

        // Act
        var result = await _sut.CreateAsync(dto);

        // Assert
        Assert.Null(result.DepartedAt);
        _repositoryMock.Verify(r => r.AddAsync(
            It.Is<TravelRecord>(t => t.DepartedAt == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WhenRecordExists_UpdatesAndReturnsDto()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord());
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TravelRecord>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((TravelRecord t, CancellationToken _) => t);

        var dto = new UpdateTravelDto("Beijing", 39.9042m, 116.4074m, Arrived, Departed);

        // Act
        var result = await _sut.UpdateAsync(1, dto);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
        Assert.Equal("Beijing", result.LocationName);
        Assert.Equal(39.9042m, result.Latitude);

        _repositoryMock.Verify(r => r.UpdateAsync(
            It.Is<TravelRecord>(t => t.LocationName == "Beijing" && t.Latitude == 39.9042m
                                     && t.Longitude == 116.4074m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WhenRecordMissing_ReturnsNull()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
                       .ReturnsAsync((TravelRecord?)null);

        // Act
        var result = await _sut.UpdateAsync(99, new UpdateTravelDto("Beijing", 39.9042m, 116.4074m, Arrived));

        // Assert
        Assert.Null(result);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<TravelRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenRecordExists_ReturnsTrue()
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
    public async Task DeleteAsync_WhenRecordMissing_ReturnsFalse()
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
