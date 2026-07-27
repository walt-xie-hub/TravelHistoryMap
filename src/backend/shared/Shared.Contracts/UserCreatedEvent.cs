namespace Shared.Contracts;

/// <summary>
/// 示例事件：用户创建。放在 Contracts 中，发布方与消费方均可直接复用同一类型，
/// 避免各服务重复定义 DTO 导致的契约漂移。
/// </summary>
public sealed class UserCreatedEvent : IIntegrationEvent
{
    public UserCreatedEvent(Guid userId, string email)
    {
        EventId = Guid.NewGuid();
        OccurredOnUtc = DateTime.UtcNow;
        UserId = userId;
        Email = email;
    }

    public Guid EventId { get; }

    public DateTime OccurredOnUtc { get; }

    public Guid UserId { get; }

    public string Email { get; }
}
