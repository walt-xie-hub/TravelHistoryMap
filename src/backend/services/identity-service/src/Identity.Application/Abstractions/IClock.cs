namespace Identity.Application.Abstractions;

/// <summary>统一时间来源，便于对 TTL / 锁定窗口做确定性测试。</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
