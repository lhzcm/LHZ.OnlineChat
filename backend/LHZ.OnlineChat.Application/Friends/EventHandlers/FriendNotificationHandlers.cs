using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Friends;

namespace LHZ.OnlineChat.Application.Friends.EventHandlers;

/// <summary>
/// 好友事件 → WS 实时通知。
///
/// 改造前 FriendService 直接持有 WsMessageHandler 并在业务方法里调 NotifyXxxAsync，
/// 业务层因此依赖了「连接管理 + 协议封包」；现在推送只是事件的一个订阅方，
/// 再加一个订阅方（比如写审计、发推送）不需要改动任何业务代码。
/// </summary>
internal sealed class NotifyOnFriendRequestSent : DomainEventHandler<FriendRequestSent>
{
    private readonly IRealtimeNotifier _notifier;

    public NotifyOnFriendRequestSent(IRealtimeNotifier notifier) => _notifier = notifier;

    protected override Task HandleAsync(FriendRequestSent e, CancellationToken ct)
        => _notifier.NotifyFriendRequestAsync(e.TargetId, e.RequesterId, ct);
}

internal sealed class NotifyOnFriendRequestAccepted : DomainEventHandler<FriendRequestAccepted>
{
    private readonly IRealtimeNotifier _notifier;

    public NotifyOnFriendRequestAccepted(IRealtimeNotifier notifier) => _notifier = notifier;

    protected override Task HandleAsync(FriendRequestAccepted e, CancellationToken ct)
        => _notifier.NotifyFriendAcceptedAsync(e.RequesterId, e.AccepterId, ct);
}

internal sealed class NotifyOnFriendRequestRejected : DomainEventHandler<FriendRequestRejected>
{
    private readonly IRealtimeNotifier _notifier;

    public NotifyOnFriendRequestRejected(IRealtimeNotifier notifier) => _notifier = notifier;

    protected override Task HandleAsync(FriendRequestRejected e, CancellationToken ct)
        => _notifier.NotifyFriendRejectedAsync(e.RequesterId, e.RejecterId, ct);
}

/// <summary>
/// 删除好友 → 双向通知。
/// 这个订阅方此前缺失，FriendRemoved 事件定义了却从未被 Raise，
/// 被删的一方只能靠自己刷新页面才发现好友没了。
/// </summary>
internal sealed class NotifyOnFriendRemoved : DomainEventHandler<FriendRemoved>
{
    private readonly IRealtimeNotifier _notifier;

    public NotifyOnFriendRemoved(IRealtimeNotifier notifier) => _notifier = notifier;

    protected override Task HandleAsync(FriendRemoved e, CancellationToken ct)
        => _notifier.NotifyFriendRemovedAsync(e.UserId, e.FriendId, ct);
}
