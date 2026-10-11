using Moq;
using Travel.Application.Services;
using Travel.Domain.Abstractions;
using Travel.Domain.Common;
using Xunit;

namespace Travel.UnitTests;

/// <summary>
/// 足迹概览聚合的映射（ADR-0025）：领域读数 → 跨服务契约。
///
/// 归属不在这里校验：内部端点已经把它**只**取自委托令牌的 sub（见 InternalEndpoints）。
/// 本组断言只负责映射不丢字段、空集合给零值 —— 后者尤其重要，
/// 因为"没记录"与"下游出错"必须在契约层就分得开。
/// </summary>
public class TravelFootprintTests
{
    private static readonly DateTimeOffset First = new(2024, 3, 5, 1, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Last = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ITravelRepository> _repository = new();

    [Fact]
    public async Task GetFootprintAsync_MapsAggregateToContract()
    {
        _repository
            .Setup(r => r.GetFootprintStatsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TravelFootprintStats(7, 3, 2, First, Last));

        var result = await new TravelService(_repository.Object).GetFootprintAsync(10);

        Assert.Equal(7, result.TotalRecords);
        Assert.Equal(3, result.CityCount);
        Assert.Equal(2, result.FavoriteCount);
        Assert.Equal(First, result.FirstArrivedAt);
        Assert.Equal(Last, result.LastArrivedAt);
        _repository.Verify(r => r.GetFootprintStatsAsync(10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetFootprintAsync_ForUserWithoutRecords_ReturnsZeros()
    {
        _repository
            .Setup(r => r.GetFootprintStatsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TravelFootprintStats(0, 0, 0, null, null));

        var result = await new TravelService(_repository.Object).GetFootprintAsync(10);

        Assert.Equal(0, result.TotalRecords);
        Assert.Equal(0, result.CityCount);
        Assert.Null(result.FirstArrivedAt);
        Assert.Null(result.LastArrivedAt);
    }
}
