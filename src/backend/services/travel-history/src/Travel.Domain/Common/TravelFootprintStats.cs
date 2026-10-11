namespace Travel.Domain.Common;

/// <summary>
/// 足迹概览的聚合读数（只读投影，不是实体、不落表）。
/// 置于领域公共层：仓储接口（领域层）返回它，应用服务（应用层）再映射成对外契约。
/// </summary>
/// <param name="TotalRecords">活动记录数（不含回收站，ADR-0013）。</param>
/// <param name="CityCount">有 City 快照的记录去重后的城市数（派生城市不落库，不计入，ADR-0017）。</param>
/// <param name="FavoriteCount">其中的精选收藏数（ADR-0014）。</param>
/// <param name="FirstArrivedAt">最早一次到达时间；无记录时为 null。</param>
/// <param name="LastArrivedAt">最近一次到达时间；无记录时为 null。</param>
public record TravelFootprintStats(
    int TotalRecords,
    int CityCount,
    int FavoriteCount,
    DateTimeOffset? FirstArrivedAt,
    DateTimeOffset? LastArrivedAt);
