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

/// <summary>
/// 有人申请入群 → 推送该群所有群主/管理员。
/// 收件人在这里查（而不是让领域事件携带），避免聚合去理解"谁是管理员"。
/// </summary>
internal sealed class NotifyOnGroupJoinRequested : DomainEventHandler<GroupJoinRequested>
{
    private readonly IRealtimeNotifier _notifier;
    private readonly IGroupMemberRepository _members;

    public NotifyOnGroupJoinRequested(IRealtimeNotifier notifier, IGroupMemberRepository members)
    {
        _notifier = notifier;
        _members = members;
    }

    protected override async Task HandleAsync(GroupJoinRequested e, CancellationToken ct)
    {
        var members = await _members.ListOfGroupAsync(e.GroupId, ct).ConfigureAwait(false);
        foreach (var member in members.Where(m => m.CanManage))
        {
            await _notifier
                .NotifyGroupJoinRequestedAsync(member.UserId, e.GroupId, e.RequestId, ct)
                .ConfigureAwait(false);
        }
    }
}

/// <summary>入群申请结果 → 推给申请人（多端同步：其他设备也要刷新群列表）</summary>
internal sealed class NotifyOnGroupJoinRequestDecided : DomainEventHandler<GroupJoinRequestDecided>
{
    private readonly IRealtimeNotifier _notifier;

    public NotifyOnGroupJoinRequestDecided(IRealtimeNotifier notifier) => _notifier = notifier;

    protected override Task HandleAsync(GroupJoinRequestDecided e, CancellationToken ct)
        => _notifier.NotifyGroupJoinResultAsync(e.UserId, e.GroupId, e.Approved, ct);
}