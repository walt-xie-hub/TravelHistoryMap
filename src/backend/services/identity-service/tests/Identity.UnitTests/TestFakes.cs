using Identity.Application.Abstractions;

namespace Identity.UnitTests;

/// <summary>可控时钟，让 TTL / 锁定窗口的测试是确定性的。</summary>
public sealed class TestClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
}

/// <summary>记录式审计桩：断言"发生了哪个安全事件"，避免把日志实现拖进单测。</summary>
public sealed class RecordingAuditLog : IAuditLog
{
    public List<string> Events { get; } = [];

    public void LoginSucceeded(int userId, string? ip, string? userAgent) => Events.Add($"login_succeeded:{userId}");

    public void LoginFailed(string normalizedEmail, string? ip, string? userAgent) => Events.Add($"login_failed:{normalizedEmail}");

    public void LoginLocked(string normalizedEmail, DateTime lockedUntil, string? ip, string? userAgent) => Events.Add($"login_locked:{normalizedEmail}");

    public void TokenRefreshed(int userId, string? ip, string? userAgent) => Events.Add($"token_refreshed:{userId}");

    public void RefreshReuseDetected(int userId, Guid familyId, string? ip, string? userAgent) => Events.Add($"refresh_reuse_detected:{familyId}");

    public void LoggedOut(int userId, Guid familyId) => Events.Add($"logged_out:{familyId}");

    public void ServiceTokenIssued(string clientId, string audience, string scopes) => Events.Add($"service_token_issued:{clientId}");

    public void ServiceTokenRequestDenied(string clientId, string reason) => Events.Add($"service_token_denied:{reason}");

    public bool Contains(string prefix) => Events.Any(e => e.StartsWith(prefix, StringComparison.Ordinal));
}

/// <summary>假的访问令牌签发器：只回一个可断言的字符串，避免单测依赖 JWT 库。</summary>
public sealed class StubAccessTokenIssuer : IAccessTokenIssuer
{
    public int AccessTokenLifetimeSeconds => 1200;

    public int ServiceTokenLifetimeSeconds => 300;

    public string CreateUserToken(Identity.Application.Models.UserCredential user) => $"user-token:{user.Id}";

    public string CreateServiceToken(string clientId, string audience, IReadOnlyCollection<string> scopes)
        => $"service-token:{clientId}:{audience}:{string.Join(',', scopes)}";
}
