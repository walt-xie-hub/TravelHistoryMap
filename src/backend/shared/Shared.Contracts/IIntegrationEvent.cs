namespace Shared.Contracts;

/// <summary>
/// 跨服务集成事件的统一契约。任何微服务发布的事件都应实现此接口，
/// 以便接入统一的总线 / Outbox（如未来引入 MassTransit / CAP / RabbitMQ）。
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>事件唯一标识，用于消费端幂等处理。</summary>
    Guid EventId { get; }

    /// <summary>事件发生时间（UTC）。</summary>
    DateTime OccurredOnUtc { get; }
}
