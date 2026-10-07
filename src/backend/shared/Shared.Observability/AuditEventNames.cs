namespace Shared.Observability;

/// <summary>
/// 审计事件名的唯一来源（docs/security/authentication.md 的 P1 事件表）。
///
/// 名字一旦发布就是查询与告警的**契约**，改名等于改契约，所以集中在这里而不是散在各调用点。
/// </summary>
public static class AuditEventNames
{
    public const string LoginSucceeded = "login_succeeded";

    public const string LoginFailed = "login_failed";

    public const string LoginLocked = "login_locked";

    public const string TokenRefreshed = "token_refreshed";

    public const string RefreshReuseDetected = "refresh_reuse_detected";

    /// <summary>登出。事件表里就叫 <c>logout</c>。</summary>
    public const string LoggedOut = "logout";

    /// <summary>改密。由 user-service 发（它是 Users 的唯一写者，ADR-0020）。</summary>
    public const string PasswordChanged = "password_changed";

    public const string ServiceTokenIssued = "service_token_issued";

    public const string ServiceTokenRequestDenied = "service_token_denied";

    /// <summary>用户委托令牌签发（token exchange）：sub=用户，act=代理服务。</summary>
    public const string DelegatedTokenIssued = "delegated_token_issued";

    public const string DelegatedTokenDenied = "delegated_token_denied";
}
