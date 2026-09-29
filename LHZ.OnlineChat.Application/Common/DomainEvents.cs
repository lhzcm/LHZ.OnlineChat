using LHZ.OnlineChat.Domain.Common;
using MediatR;

namespace LHZ.OnlineChat.Application.Common;

/// <summary>
/// 领域事件的 MediatR 包装。
/// 领域层不认识 MediatR（保持零依赖），所以在应用层套一层壳再走 INotification 发布。
/// </summary>
public sealed class DomainEventNotification<TEvent> : INotification
    where TEvent : IDomainEvent
{
    public DomainEventNotification(TEvent domainEvent) => Event = domainEvent;

    public TEvent Event { get; }
}

/// <summary>
/// 领域事件处理器基类：省掉每个订阅方都要写一遍拆包样板。
/// </summary>
public abstract class DomainEventHandler<TEvent> : INotificationHandler<DomainEventNotification<TEvent>>
    where TEvent : IDomainEvent
{
    public Task Handle(DomainEventNotification<TEvent> notification, CancellationToken cancellationToken)
        => HandleAsync(notification.Event, cancellationToken);

    protected abstract Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>
/// 领域事件派发器。由应用层用例在「聚合已持久化成功」之后调用 ——
/// 顺序很重要：先落库再派发，避免推送了 WS 通知但数据库其实失败了。
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken ct = default);
}

/// <summary>聚合根事件派发的便捷扩展</summary>
public static class DomainEventDispatcherExtensions
{
    /// <summary>取出聚合累积的事件并派发</summary>
    public static Task DispatchEventsOfAsync<TId>(
        this IDomainEventDispatcher dispatcher,
        AggregateRoot<TId> aggregate,
        CancellationToken ct = default)
        where TId : struct, IEquatable<TId>
        => dispatcher.DispatchAsync(aggregate.DequeueDomainEvents(), ct);

    /// <summary>派发单个事件</summary>
    public static Task DispatchAsync(
        this IDomainEventDispatcher dispatcher, IDomainEvent domainEvent, CancellationToken ct = default)
        => dispatcher.DispatchAsync(new[] { domainEvent }, ct);
}
