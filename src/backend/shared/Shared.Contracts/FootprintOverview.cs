namespace Shared.Contracts;

/// <summary>
/// 足迹概览（Footprint overview）——跨服务**同步**调用的载荷契约（ADR-0025）。
///
/// 放在 Shared.Contracts 而不是各服务各写一份，理由与集成事件相同：两端共用同一类型，
/// 字段改名会在编译期暴露，而不是变成"对面反序列化出一堆 null"。
///
/// 语义：
/// <list type="bullet">
/// <item>只统计**活动**记录（已移入回收站的记录不计入，ADR-0013）；</item>
/// <item><c>CityCount</c> 按 City 快照去重 —— 派生城市不落库（ADR-0017），因此不计入；</item>
/// <item><c>FirstArrivedAt</c>/<c>LastArrivedAt</c> 是到达时间的极值，空集合时为 null。</item>
/// </list>
/// </summary>
public record FootprintOverview(
    int TotalRecords,
    int CityCount,
    int FavoriteCount,
    DateTimeOffset? FirstArrivedAt,
    DateTimeOffset? LastArrivedAt);
