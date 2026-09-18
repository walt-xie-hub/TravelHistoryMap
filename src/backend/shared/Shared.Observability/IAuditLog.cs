namespace Shared.Observability;

/// <summary>
/// 安全事件审计端口（docs/security/observability-and-audit.md）。
///
/// 故意只留一个方法：具体事件名与字段由 <see cref="AuditLogExtensions"/> 的扩展方法固化，
/// 于是"系统里有哪些安全事件"集中在一处，而落点（结构化日志、库、队列）可以替换。
///
/// 两个服务（identity-service 与 user-service）共用这一个端口 —— 审计不是一个服务的事。
/// </summary>
public interface IAuditLog
{
    void Write(AuditEvent auditEvent);
}
