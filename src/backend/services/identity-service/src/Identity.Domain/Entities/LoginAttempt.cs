namespace Identity.Domain.Entities;

/// <summary>
/// 登录失败计数（滑动窗口 + 锁定）。
///
/// 存在数据库而不是进程内存里，是因为 identity-service 可能同时有多个副本
/// （ACA max-replicas &gt; 1），失败计数必须跨副本共享才挡得住定向爆破。
/// </summary>
public class LoginAttempt
{
    /// <summary>规范化后的邮箱（小写）</summary>
    public string Key { get; set; } = string.Empty;

    public int FailedCount { get; set; }

    public DateTime FirstFailedAt { get; set; }

    /// <summary>锁定截止时间；为空表示未锁定</summary>
    public DateTime? LockedUntil { get; set; }

    public bool IsLocked(DateTime now) => LockedUntil is not null && LockedUntil > now;
}
