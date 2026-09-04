namespace Travel.Domain.Entities;

/// <summary>
/// 旅行记录聚合根（领域实体）：一名用户在某地点的一次停留。不依赖任何基础设施或框架。
/// 命名为 TravelRecord 以避免与根命名空间 Travel.* 中的隐式 "Travel" 命名空间同名冲突。
/// 表列与数据库 TravelRecords 表一一对应（见 TravelRecordConfiguration）。
/// 领域语义：arrivedAt 必填；departedAt 为空表示停留仍在进行中（人还在当地）。
/// </summary>
public class TravelRecord
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>归属用户 id（引用 user-service 的 Users.Id，无导航属性，跨服务零耦合）</summary>
    public int UserId { get; set; }

    /// <summary>地点名称快照（自由文本，如 "上海"）</summary>
    public string LocationName { get; set; } = string.Empty;

    /// <summary>纬度快照（WGS84，-90 ~ 90）</summary>
    public decimal Latitude { get; set; }

    /// <summary>经度快照（WGS84，-180 ~ 180）</summary>
    public decimal Longitude { get; set; }

    /// <summary>到达时间（UTC 时刻，必填）</summary>
    public DateTimeOffset ArrivedAt { get; set; }

    /// <summary>离开时间（UTC 时刻；为空 = 停留仍在进行中，离开后补录）</summary>
    public DateTimeOffset? DepartedAt { get; set; }

    /// <summary>创建时间（UTC）</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最后更新时间（UTC，可选）</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    // 注：乐观锁行版本（PostgreSQL xmin 系统列）不放在领域实体里，原因是 EF Core 内置
    // 约定会把任何叫 RowVersion 的属性强制识别为 byte[]（SQL Server [Timestamp] 语义），
    // 无法在 CLR 层用 uint 表示。该属性在 TravelRecordConfiguration 中作为 shadow property 配置：
    // 映射到 PG 的 xmin 系统列，作为并发令牌；EF Core 在 UPDATE/DELETE 的 WHERE 中
    // 自动带上 xmin 做并发校验，冲突抛 DbUpdateConcurrencyException。
}
