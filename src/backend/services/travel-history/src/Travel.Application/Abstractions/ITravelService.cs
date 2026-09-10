namespace Travel.Application.Abstractions;

/// <summary>
/// 旅行记录用例接口。表现层只依赖此抽象。
/// ADR-0005：userId 一律由表现层从认证身份（JWT NameIdentifier）解析后传入，
/// 不再信任客户端在 body/query 中声明的归属者。
/// </summary>
public interface ITravelService
{
    /// <summary>
    /// 按用户分页查询旅行记录。from/to 为可选到达时间窗（UTC）；favoriteOnly 只取精选收藏（ADR-0014）；按到达时间倒序。
    /// </summary>
    Task<Domain.Common.PagedResult<DTOs.TravelRecordDto>> GetPagedAsync(
        int userId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        bool favoriteOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>按主键读取记录；记录不属于该用户时返回 null（不泄露存在性）。</summary>
    Task<DTOs.TravelRecordDto?> GetByIdAsync(int userId, int id, CancellationToken cancellationToken = default);

    /// <summary>为 userId 创建一条记录（dto 不再携带归属者）。</summary>
    Task<DTOs.TravelRecordDto> CreateAsync(int userId, DTOs.CreateTravelDto dto, CancellationToken cancellationToken = default);

    /// <summary>更新 userId 名下的记录；非本人或不存在返回 null。</summary>
    Task<DTOs.TravelRecordDto?> UpdateAsync(int userId, int id, DTOs.UpdateTravelDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除 userId 名下的记录——移入回收站（软删除，ADR-0013），不物理删除图片/行；非本人或不存在返回 false。
    /// </summary>
    Task<bool> DeleteAsync(int userId, int id, CancellationToken cancellationToken = default);

    /// <summary>回收站分页：userId 名下已软删除的记录（按删除时间倒序）。</summary>
    Task<Domain.Common.PagedResult<DTOs.TravelRecordDto>> GetTrashAsync(
        int userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>从回收站恢复 userId 名下的记录；非本人、不存在或不在回收站返回 false。</summary>
    Task<bool> RestoreAsync(int userId, int id, CancellationToken cancellationToken = default);

    /// <summary>彻底删除 userId 名下的记录（物理删除行）；图片媒体与图片行由表现层先清理。</summary>
    Task<bool> DeletePermanentlyAsync(int userId, int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 为 userId 创建只读分享快照（复制所选记录的分享行），返回不可猜测的 Token；
    /// 无任何属主/无有效记录时返回 null。
    /// </summary>
    Task<string?> CreateShareAsync(int userId, IReadOnlyList<int> travelIds, CancellationToken cancellationToken = default);

    /// <summary>按 Token 公开读取分享快照（只读，不校验归属）；不存在返回 null。</summary>
    Task<DTOs.PublicShareSnapshotDto?> GetShareSnapshotAsync(string token, CancellationToken cancellationToken = default);
}
