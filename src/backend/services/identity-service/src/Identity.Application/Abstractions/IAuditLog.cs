namespace Identity.Application.Abstractions;

/// <summary>
/// 安全事件审计（docs/security/observability-and-audit.md）。
///
/// 硬约束：**绝不记录口令、令牌原文、验证码答案**。字段固定为
/// 时间 / 事件 / 用户 id 或不可解析标识 / 来源 IP / User-Agent / 结果，
/// 由实现写入结构化日志，经 OTel 落到可查询、可留存的后端。
/// </summary>
public interface IAuditLog
{
    void LoginSucceeded(int userId, string? ip, string? userAgent);

    void LoginFailed(string normalizedEmail, string? ip, string? userAgent);

    void LoginLocked(string normalizedEmail, DateTime lockedUntil, string? ip, string? userAgent);

    void TokenRefreshed(int userId, string? ip, string? userAgent);

    void RefreshReuseDetected(int userId, Guid familyId, string? ip, string? userAgent);

    void LoggedOut(int userId, Guid familyId);

    void ServiceTokenIssued(string clientId, string audience, string scopes);

    void ServiceTokenRequestDenied(string clientId, string reason);
}
