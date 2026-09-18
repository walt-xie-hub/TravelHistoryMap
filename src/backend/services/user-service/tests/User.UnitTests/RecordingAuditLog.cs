using Shared.Observability;

namespace User.UnitTests;

/// <summary>记录式审计桩：断言"发生了哪个安全事件"，避免把日志实现拖进单测。</summary>
public sealed class RecordingAuditLog : IAuditLog
{
    public List<AuditEvent> Events { get; } = [];

    public void Write(AuditEvent auditEvent) => Events.Add(auditEvent);
}
