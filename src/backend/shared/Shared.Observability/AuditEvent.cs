namespace Shared.Observability;

/// <summary>审计事件的严重度：可疑事件（锁定、令牌复用、被拒）走高一级，便于告警规则区分。</summary>
public enum AuditSeverity
{
    /// <summary>正常事件（登录成功、刷新、登出、改密…）。</summary>
    Information,

    /// <summary>需要留意的安全事件（锁定、令牌复用、服务令牌被拒…）。</summary>
    Warning,
}

/// <summary>
/// 一条安全审计事件（docs/security/observability-and-audit.md）。
///
/// **类型里没有口令、没有令牌原文、没有验证码答案**：没有字段可填，也就堵住了误记的可能。
/// 固定字段为事件名 / 结果 / 严重度 / 主体（用户 id 或不可解析的账号标识）/ 来源 IP / User-Agent；
/// 事件特有的标量（familyId、lockedUntil、clientId…）放 <see cref="Data"/>。
/// </summary>
public sealed record AuditEvent(
    string Event,
    string Result = "success",
    AuditSeverity Severity = AuditSeverity.Information,
    string? Subject = null,
    string? Ip = null,
    string? UserAgent = null,
    IReadOnlyDictionary<string, object?>? Data = null);
