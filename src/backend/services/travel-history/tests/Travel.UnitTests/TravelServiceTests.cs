using Moq;
using Xunit;
using System.Text.Json;
using Travel.Application.Abstractions;
using Travel.Application.DTOs;
using Travel.Application.Services;
using Travel.Domain.Abstractions;
using Travel.Domain.Common;
using Travel.Domain.Entities;

namespace Travel.UnitTests;

/// <summary>
/// 应用服务 TravelService 的单元测试（ADR-0005：归属一律来自调用方传入的 userId，
/// 单条读取/更新/删除对非本人记录一律按“不存在”处理）。
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
    public async Task GetByIdAsync_WhenRecordOwnedByUser_ReturnsRecordDto()
    {
        // Arrange
        const int userId = 10;
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord(userId: userId));

        // Act
        var dto = await _sut.GetByIdAsync(userId, 1);

        // Assert
        Assert.NotNull(dto);
        Assert.Equal(1, dto!.Id);
        Assert.Equal(userId, dto.UserId);
        Assert.Equal("Shanghai", dto.LocationName);
    }

    [Fact]
    public async Task GetByIdAsync_WhenRecordOwnedByOtherUser_ReturnsNull()
    {
        // 越权读：记录属于用户 99，当前用户 10 不应看到（也不应暴露存在性）
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord(userId: 99));

        var dto = await _sut.GetByIdAsync(10, 1);

        Assert.Null(dto);
    }

    [Fact]
    public async Task GetByIdAsync_WhenRecordMissing_ReturnsNull()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
                       .ReturnsAsync((TravelRecord?)null);

        // Act
        var dto = await _sut.GetByIdAsync(10, 99);

        // Assert
        Assert.Null(dto);
    }

    [Fact]
    public async Task CreateAsync_AssignsTokenUserIdAndPersistsRecord()
    {
        // Arrange：dto 不再携带 UserId，归属完全由调用方 userId（来自 token）决定
        var created = SampleRecord(7, userId: 10);
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<TravelRecord>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(created);

        var dto = new CreateTravelDto("Shanghai", 31.2304m, 121.4737m, Arrived, Departed);

        // Act
        var result = await _sut.CreateAsync(10, dto);

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
        var dto = new CreateTravelDto("Shanghai", 31.2304m, 121.4737m, Arrived, DepartedAt: null);

        // Act
        var result = await _sut.CreateAsync(10, dto);

        // Assert
        Assert.Null(result.DepartedAt);
        _repositoryMock.Verify(r => r.AddAsync(
            It.Is<TravelRecord>(t => t.DepartedAt == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithTagsAndFavorite_NormalizesAndPersists()
    {
        // Arrange（ADR-0014：标签 trim / 忽略大小写去重；收藏标记落库）
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<TravelRecord>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((TravelRecord t, CancellationToken _) => t);
        var dto = new CreateTravelDto(
            "Shanghai", 31.2304m, 121.4737m, Arrived, Departed,
            Tags: new[] { " 亲子游 ", "徒步", "亲子游" },
            IsFavorite: true);

        // Act
        var result = await _sut.CreateAsync(10, dto);

        // Assert
        Assert.Equal(new[] { "亲子游", "徒步" }, result.Tags);
        Assert.True(result.IsFavorite);
        _repositoryMock.Verify(r => r.AddAsync(
            It.Is<TravelRecord>(t => t.IsFavorite
                && JsonSerializer.Deserialize<List<string>>(t.TagsJson)!
                    .SequenceEqual(new string[] { "亲子游", "徒步" })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_TooManyTags_ThrowsAndDoesNotPersist()
    {
        // Arrange（ADR-0014：最多 8 个标签）
        var many = Enumerable.Range(1, 9).Select(i => $"tag{i}").ToArray();
        var dto = new CreateTravelDto("Shanghai", 31.2304m, 121.4737m, Arrived, Tags: many);

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(10, dto));
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<TravelRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_OverlongTag_ThrowsAndDoesNotPersist()
    {
        // Arrange（ADR-0014：单个标签 ≤20 字符）
        var dto = new CreateTravelDto("Shanghai", 31.2304m, 121.4737m, Arrived,
            Tags: new[] { new string('长', 21) });

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(10, dto));
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<TravelRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_TogglesFavoriteAndStoresTags()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord(userId: 10));
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TravelRecord>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((TravelRecord t, CancellationToken _) => t);

        var dto = new UpdateTravelDto("Beijing", 39.9042m, 116.4074m, Arrived, Departed,
            Tags: new[] { "出差" }, IsFavorite: true);

        // Act
        var result = await _sut.UpdateAsync(10, 1, dto);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(new[] { "出差" }, result!.Tags);
        Assert.True(result.IsFavorite);
        _repositoryMock.Verify(r => r.UpdateAsync(
            It.Is<TravelRecord>(t => t.IsFavorite
                && JsonSerializer.Deserialize<List<string>>(t.TagsJson)!
                    .SequenceEqual(new string[] { "出差" })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WhenRecordOwnedByUser_UpdatesAndReturnsDto()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord(userId: 10));
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TravelRecord>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((TravelRecord t, CancellationToken _) => t);

        var dto = new UpdateTravelDto("Beijing", 39.9042m, 116.4074m, Arrived, Departed);

        // Act
        var result = await _sut.UpdateAsync(10, 1, dto);

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
    public async Task UpdateAsync_WhenRecordOwnedByOtherUser_ReturnsNullAndDoesNotUpdate()
    {
        // 越权改：记录属于用户 99，用户 10 的更新应失败且不触碰数据
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord(userId: 99));

        var result = await _sut.UpdateAsync(10, 1, new UpdateTravelDto("Beijing", 39.9042m, 116.4074m, Arrived));

        Assert.Null(result);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<TravelRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WhenRecordMissing_ReturnsNull()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
                       .ReturnsAsync((TravelRecord?)null);

        // Act
        var result = await _sut.UpdateAsync(10, 99, new UpdateTravelDto("Beijing", 39.9042m, 116.4074m, Arrived));

        // Assert
        Assert.Null(result);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<TravelRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenRecordOwnedByUser_MovesToTrash()
    {
        // Arrange（ADR-0013：删除=软删除，仅置 DeletedAt）
        var record = SampleRecord(userId: 10);
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(record);
        _repositoryMock.Setup(r => r.SetDeletedAtAsync(1, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(true);

        // Act
        var deleted = await _sut.DeleteAsync(10, 1);

        // Assert：软删除写入非空 DeletedAt，不物理删除
        Assert.True(deleted);
        _repositoryMock.Verify(r => r.SetDeletedAtAsync(1, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenRecordAlreadyTrashed_ReturnsFalse()
    {
        // Arrange：已在回收站，再次删除应失败
        var record = SampleRecord(userId: 10);
        record.DeletedAt = DateTimeOffset.UtcNow;
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(record);

        // Act
        var deleted = await _sut.DeleteAsync(10, 1);

        // Assert
        Assert.False(deleted);
        _repositoryMock.Verify(r => r.SetDeletedAtAsync(It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenRecordOwnedByOtherUser_ReturnsFalseAndDoesNotDelete()
    {
        // 越权删：记录属于用户 99，用户 10 删除应失败
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord(userId: 99));

        var deleted = await _sut.DeleteAsync(10, 1);

        Assert.False(deleted);
        _repositoryMock.Verify(r => r.SetDeletedAtAsync(It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenRecordMissing_ReturnsFalse()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
                       .ReturnsAsync((TravelRecord?)null);

        // Act
        var deleted = await _sut.DeleteAsync(10, 99);

        // Assert
        Assert.False(deleted);
        _repositoryMock.Verify(r => r.SetDeletedAtAsync(It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_WhenRecordTrashed_ReturnsNull()
    {
        // Arrange（ADR-0013：回收站记录对普通详情不可见）
        var record = SampleRecord(userId: 10);
        record.DeletedAt = DateTimeOffset.UtcNow;
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(record);

        // Act
        var dto = await _sut.GetByIdAsync(10, 1);

        // Assert
        Assert.Null(dto);
    }

    [Fact]
    public async Task GetTrashAsync_ReturnsMappedPagedResult()
    {
        // Arrange
        var trashed = SampleRecord(id: 3, userId: 10);
        trashed.DeletedAt = DateTimeOffset.UtcNow;
        var paged = new PagedResult<TravelRecord>(
            new List<TravelRecord> { trashed }, 1, 10, TotalCount: 1, TotalPages: 1);
        _repositoryMock.Setup(r => r.GetTrashPagedAsync(10, 1, 10, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(paged);

        // Act
        var result = await _sut.GetTrashAsync(10, 1, 10);

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("Shanghai", result.Items[0].LocationName);
    }

    [Fact]
    public async Task RestoreAsync_WhenTrashedOwnedByUser_ReturnsTrue()
    {
        // Arrange
        var record = SampleRecord(userId: 10);
        record.DeletedAt = DateTimeOffset.UtcNow;
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(record);
        _repositoryMock.Setup(r => r.SetDeletedAtAsync(1, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(true);

        // Act
        var restored = await _sut.RestoreAsync(10, 1);

        // Assert：恢复 = 写 null
        Assert.True(restored);
        _repositoryMock.Verify(r => r.SetDeletedAtAsync(1, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreAsync_WhenRecordActive_ReturnsFalse()
    {
        // Arrange：未在回收站，恢复应失败
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord(userId: 10));

        // Act
        var restored = await _sut.RestoreAsync(10, 1);

        // Assert
        Assert.False(restored);
        _repositoryMock.Verify(r => r.SetDeletedAtAsync(It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeletePermanentlyAsync_WhenOwnedByUser_HardDeletes()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord(userId: 10));
        _repositoryMock.Setup(r => r.DeleteAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(true);

        // Act
        var deleted = await _sut.DeletePermanentlyAsync(10, 1);

        // Assert
        Assert.True(deleted);
        _repositoryMock.Verify(r => r.DeleteAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_DescriptionOver4000VisibleChars_ThrowsAndDoesNotPersist()
    {
        // Arrange（ADR-0009：正文长度按可见字符 ≤4000 校验）
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(SampleRecord(userId: 10));

        var dto = new UpdateTravelDto("Shanghai", 31.2304m, 121.4737m, Arrived, Departed,
            Description: new string('字', 4001));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.UpdateAsync(10, 1, dto));
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<TravelRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_BlankDescription_ClearedToNullAndPersisted()
    {
        // Arrange
        var record = SampleRecord(userId: 10);
        record.Description = "旧文本";
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(record);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TravelRecord>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((TravelRecord t, CancellationToken _) => t);

        var dto = new UpdateTravelDto("Shanghai", 31.2304m, 121.4737m, Arrived, Departed,
            Description: "   ");

        // Act
        var result = await _sut.UpdateAsync(10, 1, dto);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result!.Description);
        _repositoryMock.Verify(r => r.UpdateAsync(
            It.Is<TravelRecord>(t => t.Description == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_DescriptionHtml_SanitizedBeforePersist()
    {
        // Arrange（ADR-0009：入库前 allow-list 消毒，脚本/事件属性剥离）
        var record = SampleRecord(userId: 10);
        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(record);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TravelRecord>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((TravelRecord t, CancellationToken _) => t);

        var dto = new UpdateTravelDto("Shanghai", 31.2304m, 121.4737m, Arrived, Departed,
            Description: "<p>你好 <strong>世界</strong></p><script>alert(1)</script>");

        // Act
        var result = await _sut.UpdateAsync(10, 1, dto);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("<strong>世界</strong>", result!.Description);
        Assert.DoesNotContain("script", result.Description);
        Assert.DoesNotContain("alert", result.Description);
    }

    [Fact]
    public async Task CreateShareAsync_WithOwnedRecords_ReturnsTokenAndPersists()
    {
        // Arrange
        var records = new List<TravelRecord> { SampleRecord(1, 10), SampleRecord(2, 10) };
        records[0].ArrivedAt = Arrived;
        records[1].ArrivedAt = Departed;
        _repositoryMock.Setup(r => r.GetByIdsAsync(10, It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);
        _repositoryMock.Setup(r => r.AddShareAsync(It.IsAny<TravelShareSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TravelShareSnapshot s, CancellationToken _) => { s.Token = "t0k3n"; return s; });

        // Act
        var token = await _sut.CreateShareAsync(10, new List<int> { 1, 2 });

        // Assert
        Assert.Equal("t0k3n", token);
        _repositoryMock.Verify(r => r.AddShareAsync(
            It.Is<TravelShareSnapshot>(s => s.UserId == 10 && s.RecordCount == 2 && s.RowsJson.Contains("Shanghai")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateShareAsync_NoOwnedRecords_ReturnsNull()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdsAsync(10, It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TravelRecord>());

        // Act
        var token = await _sut.CreateShareAsync(10, new List<int> { 99 });

        // Assert
        Assert.Null(token);
        _repositoryMock.Verify(r => r.AddShareAsync(It.IsAny<TravelShareSnapshot>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateShareAsync_EmptyOrTooMany_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateShareAsync(10, new List<int>()));
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateShareAsync(10, Enumerable.Range(1, 101).ToList()));
    }

    [Fact]
    public async Task GetShareSnapshotAsync_ValidToken_ReturnsDto()
    {
        // Arrange
        var rows = new List<ShareSnapshotRow> { new("Shanghai", Arrived, Departed, "<p>好玩</p>") };
        _repositoryMock.Setup(r => r.GetShareByTokenAsync("tok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TravelShareSnapshot
            {
                Token = "tok",
                Title = "Shanghai 等 1 站",
                CreatedAt = Arrived,
                RecordCount = 1,
                RowsJson = JsonSerializer.Serialize(rows),
            });

        // Act
        var dto = await _sut.GetShareSnapshotAsync("tok");

        // Assert
        Assert.NotNull(dto);
        Assert.Equal(1, dto!.RecordCount);
        Assert.Equal("Shanghai", dto.Rows[0].LocationName);
    }

    [Fact]
    public async Task GetShareSnapshotAsync_UnknownToken_ReturnsNull()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetShareByTokenAsync("nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TravelShareSnapshot?)null);

        // Act
        var dto = await _sut.GetShareSnapshotAsync("nope");

        // Assert
        Assert.Null(dto);
    }
}
