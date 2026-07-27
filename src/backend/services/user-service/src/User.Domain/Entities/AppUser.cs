namespace User.Domain.Entities;

/// <summary>
/// 用户聚合根（领域实体）。不依赖任何基础设施或框架。
/// 命名为 AppUser 以避免与根命名空间 User.* 中的隐式 "User" 命名空间同名冲突。
/// 表列与数据库 Users 表一一对应（见 UserConfiguration）。
/// </summary>
public class AppUser
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>显示名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>登录/联系邮箱（唯一）</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>手机号（可选）</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>密码哈希（可选，预留鉴权字段）</summary>
    public string? PasswordHash { get; set; }

    /// <summary>头像 URL（可选）</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>账号是否启用</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>创建时间（UTC）</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最后更新时间（UTC，可选）</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 乐观锁行版本（uint 类型）。Npgsql EF Core 会自动把它映射到 PostgreSQL 的隐藏系统列 xmin
    /// （事务 ID），由数据库每次 UPDATE 时自动维护；EF Core 在 UPDATE/DELETE 的 WHERE 中带上做并发校验。
    /// 数据库表里无需显式创建该列（xmin 是每张表的隐式系统列）。
    /// </summary>
    public uint RowVersion { get; set; }
}
