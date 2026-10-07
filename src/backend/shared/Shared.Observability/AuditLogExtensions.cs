namespace Shared.Observability;

/// <summary>
/// 安全事件的事件名、字段与严重度都固化在这里。
///
/// 调用方只表达"发生了什么"，字段与事件名由这一处决定 —— 于是同一个事件不会在两个服务里
/// 长出两套字段，改名也只需改一处（<see cref="AuditEventNames"/>）。
/// </summary>
public static class AuditLogExtensions
{
    public static void LoginSucceeded(this IAuditLog audit, int userId, string? ip, string? userAgent)
        => audit.Write(new AuditEvent(
            AuditEventNames.LoginSucceeded,
            Subject: $"user:{userId}",
            Ip: ip,
            UserAgent: userAgent));

    public static void LoginFailed(this IAuditLog audit, string normalizedEmail, string? ip, string? userAgent)
        => audit.Write(new AuditEvent(
            AuditEventNames.LoginFailed,
            Result: "failure",
            Subject: normalizedEmail,
            Ip: ip,
            UserAgent: userAgent));

    public static void LoginLocked(this IAuditLog audit, string normalizedEmail, DateTime lockedUntil, string? ip, string? userAgent)
        => audit.Write(new AuditEvent(
            AuditEventNames.LoginLocked,
            Result: "denied",
            Severity: AuditSeverity.Warning,
            Subject: normalizedEmail,
            Ip: ip,
            UserAgent: userAgent,
            Data: new Dictionary<string, object?> { ["lockedUntil"] = lockedUntil }));

    public static void TokenRefreshed(this IAuditLog audit, int userId, string? ip, string? userAgent)
        => audit.Write(new AuditEvent(
            AuditEventNames.TokenRefreshed,
            Subject: $"user:{userId}",
            Ip: ip,
            UserAgent: userAgent));

    public static void RefreshReuseDetected(this IAuditLog audit, int userId, Guid familyId, string? ip, string? userAgent)
        => audit.Write(new AuditEvent(
            AuditEventNames.RefreshReuseDetected,
            Result: "denied",
            Severity: AuditSeverity.Warning,
            Subject: $"user:{userId}",
            Ip: ip,
            UserAgent: userAgent,
            Data: new Dictionary<string, object?> { ["familyId"] = familyId }));

    public static void LoggedOut(this IAuditLog audit, int userId, Guid familyId)
        => audit.Write(new AuditEvent(
            AuditEventNames.LoggedOut,
            Subject: $"user:{userId}",
            Data: new Dictionary<string, object?> { ["familyId"] = familyId }));

    public static void ServiceTokenIssued(this IAuditLog audit, string clientId, string audience, string scopes)
        => audit.Write(new AuditEvent(
            AuditEventNames.ServiceTokenIssued,
            Subject: $"client:{clientId}",
            Data: new Dictionary<string, object?> { ["audience"] = audience, ["scopes"] = scopes }));

    public static void ServiceTokenRequestDenied(this IAuditLog audit, string clientId, string reason)
        => audit.Write(new AuditEvent(
            AuditEventNames.ServiceTokenRequestDenied,
            Result: "denied",
            Severity: AuditSeverity.Warning,
            Subject: $"client:{clientId}",
            Data: new Dictionary<string, object?> { ["reason"] = reason }));

    /// <summary>
    /// 用户委托令牌签发（token exchange）。Subject 记**用户**，actor 记**代理发起方** ——
    /// 审计要能同时回答“代表谁”与“谁在调”，这两者缺一就无法归因。
    /// </summary>
    public static void DelegatedTokenIssued(this IAuditLog audit, int userId, string actorClientId, string audience, string scopes)
        => audit.Write(new AuditEvent(
            AuditEventNames.DelegatedTokenIssued,
            Subject: $"user:{userId}",
            Data: new Dictionary<string, object?>
            {
                ["actor"] = $"service:{actorClientId}",
                ["audience"] = audience,
                ["scopes"] = scopes,
            }));

    public static void DelegatedTokenDenied(this IAuditLog audit, string actorClientId, string reason)
        => audit.Write(new AuditEvent(
            AuditEventNames.DelegatedTokenDenied,
            Result: "denied",
            Severity: AuditSeverity.Warning,
            Subject: $"client:{actorClientId}",
            Data: new Dictionary<string, object?> { ["reason"] = reason }));

    public static void PasswordChanged(this IAuditLog audit, int userId, string? ip, string? userAgent)
        => audit.Write(new AuditEvent(
            AuditEventNames.PasswordChanged,
            Subject: $"user:{userId}",
            Ip: ip,
            UserAgent: userAgent));
}
