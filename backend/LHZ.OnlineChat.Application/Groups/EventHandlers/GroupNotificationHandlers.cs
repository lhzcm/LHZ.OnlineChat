using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Application.Groups.EventHandlers;

/// <summary>被邀请入群 → 逐个推送 group_invited</summary>
internal sealed class NotifyOnGroupMembersInvited : DomainEventHandler<GroupMembersInvited>
{
    private readonly IRealtimeNotifier _notifier;

    public NotifyOnGroupMembersInvited(IRealtimeNotifier notifier) => _notifier = notifier;

    protected override async Task HandleAsync(GroupMembersInvited e, CancellationToken ct)
    {
        foreach (var userId in e.InvitedUserIds)
        {
            await _notifier.NotifyGroupInvitedAsync(userId, e.GroupId, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// 群解散 → 通知解散前的成员 + 清理这些人的会话设置。
/// 改造前「通知在线成员」只写在 AdminService.DissolveGroupAsync 里，
/// 用户自己解散群（GroupService.DismissGroupAsync）并不会通知 —— 两条路径行为不一致。
/// 现在两者都发同一个事件，行为自然统一。
/// </summary>
internal sealed class HandleGroupDissolved : DomainEventHandler<GroupDissolved>
{
    private readonly IRealtimeNotifier _notifier;
    private readonly ISessionSettingRepository _sessionSettings;

    public HandleGroupDissolved(IRealtimeNotifier notifier, ISessionSettingRepository sessionSettings)
    {
        _notifier = notifier;
        _sessionSettings = sessionSettings;
    }

    protected override async Task HandleAsync(GroupDissolved e, CancellationToken ct)
    {
        await _sessionSettings
            .DeleteBySessionAsync(ChatSessionType.Group, e.GroupId, ct)
            .ConfigureAwait(false);

        await _notifier
            .NotifyGroupDissolvedAsync(e.MemberIds, e.GroupId, e.Name, ct)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// 被移出群 → 通知本人退出会话。
/// 与「解散群」同语义（客户端移除该会话），此前 GroupMemberRemoved 从未被 Raise，
/// 被踢的人客户端里那个群会一直留着，点进去才发现自己已不是成员。
/// </summary>
internal sealed class NotifyOnGroupMemberRemoved : DomainEventHandler<GroupMemberRemoved>
{
    private readonly IRealtimeNotifier _notifier;

    public NotifyOnGroupMemberRemoved(IRealtimeNotifier notifier) => _notifier = notifier;

    protected override Task HandleAsync(GroupMemberRemoved e, CancellationToken ct)
        => _notifier.NotifyRemovedFromGroupAsync(e.UserId, e.GroupId, ct);
}
