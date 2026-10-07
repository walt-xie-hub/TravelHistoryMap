using Identity.Application.Abstractions;
using Shared.Observability;

namespace Identity.UnitTests;

/// <summary>可控时钟，让 TTL / 锁定窗口的测试是确定性的。</summary>
public sealed class TestClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
}

/// <summary>记录式审计桩：断言"发生了什么安全事件"，避免把日志实现拖进单测。</summary>
public sealed class RecordingAuditLog : IAuditLog
{
    public List<AuditEvent> Events { get; } = [];

    public void Write(AuditEvent auditEvent) => Events.Add(auditEvent);

    /// <summary>是否出现过某个事件。事件名是契约，所以请用 <see cref="AuditEventNames"/> 的常量。</summary>
    public bool Contains(string eventName) => Events.Any(e => e.Event == eventName);

    /// <summary>取某事件的数据字段（如 familyId、lockedUntil），用于断言"撤的是哪一族"。</summary>
    public object? DataOf(string eventName, string key)
        => Events.LastOrDefault(e => e.Event == eventName)?.Data is { } data
           && data.TryGetValue(key, out var value)
            ? value
            : null;
}

/// <summary>假的访问令牌签发器：只回一个可断言的字符串，避免单测依赖 JWT 库。</summary>
public sealed class StubAccessTokenIssuer : IAccessTokenIssuer
{
    public int AccessTokenLifetimeSeconds => 1200;

    public int ServiceTokenLifetimeSeconds => 300;

    public string CreateUserToken(Identity.Application.Models.UserCredential user) => $"user-token:{user.Id}";

    public string CreateServiceToken(string clientId, string audience, IReadOnlyCollection<string> scopes)
        => $"service-token:{clientId}:{audience}:{string.Join(',', scopes)}";

    public string CreateDelegatedToken(int userId, string actorClientId, string audience, IReadOnlyCollection<string> scopes)
        => $"delegated-token:user:{userId}:actor:{actorClientId}:{audience}:{string.Join(',', scopes)}";
}
