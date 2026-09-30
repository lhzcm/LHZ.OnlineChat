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
///
/// 用例包在事务里之后，「落库成功」的真正含义是「事务已提交」，
/// 因此派发会被 <see cref="DomainEventOutbox"/> 推迟到提交之后（见该类说明）。
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken ct = default);
}

/// <summary>
/// 领域事件的事务内缓冲区（每请求一个）。
///
/// 用例里 `await _events.DispatchAsync(...)` 的位置在事务中间，而订阅方做的是
/// 推 WebSocket、发邮件这类不可回滚的副作用 —— 若事务随后回滚，通知就发错了。
/// 所以事务期间派发只入队，由 TransactionBehavior 在 commit 之后统一放行。
///
/// 副作用是订阅方的写操作落在事务之外（跨聚合最终一致），这正是领域事件该有的语义。
/// </summary>
public sealed class DomainEventOutbox
{
    private readonly List<IDomainEvent> _pending = new();

    /// <summary>是否处于「只入队、不派发」状态（事务进行中）</summary>
    public bool IsDeferring { get; private set; }

    public void BeginDeferring() => IsDeferring = true;

    public void Enqueue(IEnumerable<IDomainEvent> domainEvents) => _pending.AddRange(domainEvents);

    /// <summary>结束缓冲并取出全部积压事件（顺序与入队一致）</summary>
    public IReadOnlyList<IDomainEvent> StopDeferringAndDrain()
    {
        IsDeferring = false;
        if (_pending.Count == 0) return Array.Empty<IDomainEvent>();

        var snapshot = _pending.ToArray();
        _pending.Clear();
        return snapshot;
    }

    /// <summary>回滚后丢弃积压事件（事务没提交，事件等于没发生）</summary>
    public void Discard()
    {
        IsDeferring = false;
        _pending.Clear();
    }
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
