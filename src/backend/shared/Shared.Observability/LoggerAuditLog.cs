using Microsoft.Extensions.Logging;

namespace Shared.Observability;

/// <summary>
/// 审计日志实现（docs/security/observability-and-audit.md）：事件名与字段固定，
/// 经 ILogger → OpenTelemetry → 可查询、可留存的后端。
///
/// 事件特有的标量走 <c>BeginScope</c>：查询时它们仍是**属性**，而不是被拼进消息文本。
/// </summary>
public sealed class LoggerAuditLog(ILogger<LoggerAuditLog> logger) : IAuditLog
{
    public void Write(AuditEvent auditEvent)
    {
        using var scope = auditEvent.Data is { Count: > 0 }
            ? logger.BeginScope(auditEvent.Data)
            : null;

        logger.Log(
            auditEvent.Severity == AuditSeverity.Warning ? LogLevel.Warning : LogLevel.Information,
            "audit {Event} result={Result} subject={Subject} ip={Ip} userAgent={UserAgent}",
            auditEvent.Event,
            auditEvent.Result,
            auditEvent.Subject,
            auditEvent.Ip,
            auditEvent.UserAgent);
    }
}
