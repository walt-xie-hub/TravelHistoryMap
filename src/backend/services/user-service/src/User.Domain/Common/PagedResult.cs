namespace User.Domain.Common;

/// <summary>
/// 分页结果封装：包含本页数据、当前页、每页大小、总条数与总页数。
/// 置于领域公共层，仓储接口（领域层）与应用服务（应用层）均可复用。
/// </summary>
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
