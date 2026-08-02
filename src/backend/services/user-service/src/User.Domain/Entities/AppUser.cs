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

    // 注：乐观锁行版本（PostgreSQL xmin 系统列）不放在领域实体里，原因是 EF Core 内置
    // 约定会把任何叫 RowVersion 的属性强制识别为 byte[]（SQL Server [Timestamp] 语义），
    // 无法在 CLR 层用 uint 表示。该属性在 UserConfiguration 中作为 shadow property 配置：
    // 映射到 PG 的 xmin 系统列，作为并发令牌；EF Core 在 UPDATE/DELETE 的 WHERE 中
    // 自动带上 xmin 做并发校验，冲突抛 DbUpdateConcurrencyException。
}
