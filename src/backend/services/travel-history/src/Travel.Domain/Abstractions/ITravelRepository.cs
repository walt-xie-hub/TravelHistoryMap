using Travel.Domain.Common;
using Travel.Domain.Entities;

namespace Travel.Domain.Abstractions;

/// <summary>
/// 旅行记录仓储接口。定义在领域层，由基础设施层实现（依赖倒置）。
/// </summary>
public interface ITravelRepository
{
    /// <summary>
    /// 按用户分页查询旅行记录。page 从 1 开始；可按到达时间窗 [from, to] 过滤；
    /// 按到达时间倒序（最新在前）。返回本页数据、总条数与总页数。
    /// </summary>
    Task<PagedResult<TravelRecord>> GetPagedAsync(
        int userId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<TravelRecord?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<TravelRecord> AddAsync(TravelRecord record, CancellationToken cancellationToken = default);

    /// <summary>更新已有记录（EF 会基于 xmin 做乐观锁校验）。</summary>
    Task<TravelRecord?> UpdateAsync(TravelRecord record, CancellationToken cancellationToken = default);

    /// <summary>按主键删除记录；不存在时返回 false。</summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TravelImage>> GetImagesAsync(int recordId, CancellationToken cancellationToken = default);
    Task<TravelImage?> GetImageAsync(int imageId, CancellationToken cancellationToken = default);
    Task<TravelImage> AddImageAsync(TravelImage image, CancellationToken cancellationToken = default);
    Task<bool> DeleteImageAsync(int imageId, CancellationToken cancellationToken = default);
}
