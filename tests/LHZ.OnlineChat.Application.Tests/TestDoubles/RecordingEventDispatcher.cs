using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Application.Tests.TestDoubles;

/// <summary>
/// 领域事件派发器替身，两种用法：
///   1) 只记录 —— 断言「用例发出了正确的事件」（默认）
///   2) 记录 + 真派发给注册的处理器 —— 断言「事件订阅方产生了正确的副作用」
///
/// 第 2 种是关键：改造把「改密→踢会话」「拉黑→解好友」这类副作用挪到了事件订阅方，
/// 如果测试只验证事件发出、不验证订阅方执行，等于把最容易出问题的一段留在了覆盖之外。
/// </summary>
internal sealed class RecordingEventDispatcher : IDomainEventDispatcher
{
    private readonly Dictionary<Type, List<Func<IDomainEvent, CancellationToken, Task>>> _handlers = new();

    internal List<IDomainEvent> Dispatched { get; } = new();

    internal IEnumerable<TEvent> EventsOf<TEvent>() where TEvent : IDomainEvent
        => Dispatched.OfType<TEvent>();

    internal TEvent SingleEvent<TEvent>() where TEvent : IDomainEvent
        => Assert.Single(Dispatched.OfType<TEvent>());

    internal bool Has<TEvent>() where TEvent : IDomainEvent => Dispatched.OfType<TEvent>().Any();

    /// <summary>注册一个真实的事件处理器，让派发产生实际副作用</summary>
    internal void Subscribe<TEvent>(DomainEventHandler<TEvent> handler)
        where TEvent : IDomainEvent
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var list))
        {
            list = new List<Func<IDomainEvent, CancellationToken, Task>>();
            _handlers[typeof(TEvent)] = list;
        }

        list.Add((e, ct) => handler.Handle(new DomainEventNotification<TEvent>((TEvent)e), ct));
    }

    public async Task DispatchAsync(
        IEnumerable<IDomainEvent> domainEvents, CancellationToken ct = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            Dispatched.Add(domainEvent);

            if (!_handlers.TryGetValue(domainEvent.GetType(), out var handlers)) continue;
            foreach (var handler in handlers) await handler(domainEvent, ct);
        }
    }
}
