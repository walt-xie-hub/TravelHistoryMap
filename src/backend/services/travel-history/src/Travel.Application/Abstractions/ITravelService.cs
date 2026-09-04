namespace Travel.Application.Abstractions;

/// <summary>
/// 旅行记录用例接口。表现层只依赖此抽象。
/// </summary>
public interface ITravelService
{
    /// <summary>
    /// 按用户分页查询旅行记录。from/to 为可选到达时间窗（UTC），按到达时间倒序。
    /// </summary>
    Task<Domain.Common.PagedResult<DTOs.TravelRecordDto>> GetPagedAsync(
        int userId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<DTOs.TravelRecordDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<DTOs.TravelRecordDto> CreateAsync(DTOs.CreateTravelDto dto, CancellationToken cancellationToken = default);

    Task<DTOs.TravelRecordDto?> UpdateAsync(int id, DTOs.UpdateTravelDto dto, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
