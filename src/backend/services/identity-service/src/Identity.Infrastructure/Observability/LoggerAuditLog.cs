using Identity.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Identity.Infrastructure.Observability;

/// <summary>
/// 审计日志实现（docs/security/observability-and-audit.md）：事件名固定、字段固定，
/// 经 ILogger → OpenTelemetry → 可查询后端留存。
///
/// **这里刻意只接受非敏感字段**：没有口令、没有令牌原文、没有验证码答案 ——
/// 接口签名本身就堵住了误记的可能。
/// </summary>
public sealed class LoggerAuditLog(ILogger<LoggerAuditLog> logger) : IAuditLog
{
    public void LoginSucceeded(int userId, string? ip, string? userAgent)
        => logger.LogInformation("audit {Event} userId={UserId} ip={Ip} userAgent={UserAgent}",
            "login_succeeded", userId, ip, userAgent);

    public void LoginFailed(string normalizedEmail, string? ip, string? userAgent)
        => logger.LogInformation("audit {Event} account={Account} ip={Ip} userAgent={UserAgent}",
            "login_failed", normalizedEmail, ip, userAgent);

    public void LoginLocked(string normalizedEmail, DateTime lockedUntil, string? ip, string? userAgent)
        => logger.LogWarning("audit {Event} account={Account} lockedUntil={LockedUntil} ip={Ip} userAgent={UserAgent}",
            "login_locked", normalizedEmail, lockedUntil, ip, userAgent);

    public void TokenRefreshed(int userId, string? ip, string? userAgent)
        => logger.LogInformation("audit {Event} userId={UserId} ip={Ip} userAgent={UserAgent}",
            "token_refreshed", userId, ip, userAgent);

    public void RefreshReuseDetected(int userId, Guid familyId, string? ip, string? userAgent)
        => logger.LogWarning("audit {Event} userId={UserId} familyId={FamilyId} ip={Ip} userAgent={UserAgent}",
            "refresh_reuse_detected", userId, familyId, ip, userAgent);

    public void LoggedOut(int userId, Guid familyId)
        => logger.LogInformation("audit {Event} userId={UserId} familyId={FamilyId}",
            "logged_out", userId, familyId);

    public void ServiceTokenIssued(string clientId, string audience, string scopes)
        => logger.LogInformation("audit {Event} clientId={ClientId} audience={Audience} scopes={Scopes}",
            "service_token_issued", clientId, audience, scopes);

    public void ServiceTokenRequestDenied(string clientId, string reason)
        => logger.LogWarning("audit {Event} clientId={ClientId} reason={Reason}",
            "service_token_denied", clientId, reason);
}
