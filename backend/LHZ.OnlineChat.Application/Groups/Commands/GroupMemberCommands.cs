using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Friends;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using MediatR;

namespace LHZ.OnlineChat.Application.Groups.Commands;

/// <summary>邀请好友入群（仅群主/管理员，且只能邀请自己的好友）</summary>
public sealed class InviteGroupMembersCommand : ICommand<ApiResponse>
{
    public long GroupId { get; set; }

    public int OperatorId { get; set; }

    public List<int> UserIds { get; set; } = new();
}

internal sealed class InviteGroupMembersHandler : IRequestHandler<InviteGroupMembersCommand, ApiResponse>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;
    private readonly IGroupMessageRepository _messages;
    private readonly IFriendshipRepository _friendships;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public InviteGroupMembersHandler(
        IGroupRepository groups,
        IGroupMemberRepository members,
        IGroupMessageRepository messages,
        IFriendshipRepository friendships,
        IDomainEventDispatcher events,
        IClock clock)
    {
        _groups = groups;
        _members = members;
        _messages = messages;
        _friendships = friendships;
        _events = events;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(InviteGroupMembersCommand command, CancellationToken ct)
    {
        DomainException.Ensure(command.UserIds.Count > 0, "请选择要邀请的好友");

        _ = await _groups.GetRequiredAsync(command.GroupId, ct).ConfigureAwait(false);

        var op = await _members.GetRequiredAsync(command.GroupId, command.OperatorId, ct: ct).ConfigureAwait(false);
        op.EnsureCanManageGroup("邀请成员");

        var targetIds = command.UserIds.Distinct().ToList();

        // 只能邀请自己的好友
        var friendIds = (await _friendships.ListFriendIdsOfAsync(command.OperatorId, ct).ConfigureAwait(false))
            .ToHashSet();
        DomainException.Ensure(targetIds.All(friendIds.Contains), "只能邀请自己的好友加入群组");

        // 排除已在群中的
        var existing = (await _members.FilterExistingAsync(command.GroupId, targetIds, ct).ConfigureAwait(false))
            .ToHashSet();
        var toInvite = targetIds.Where(id => !existing.Contains(id)).ToList();
        DomainException.Ensure(toInvite.Count > 0, "所选好友都已在该群中");

        var now = _clock.UtcNow;
        var latestId = await _messages.MaxIdOfGroupAsync(command.GroupId, ct).ConfigureAwait(false);

        await _members
            .AddRangeAsync(toInvite.Select(id => GroupMember.Join(command.GroupId, id, latestId, now)), ct)
            .ConfigureAwait(false);

        await _events
            .DispatchAsync(new GroupMembersInvited(command.GroupId, toInvite, command.OperatorId, now), ct)
            .ConfigureAwait(false);

        return ApiResponse.Ok($"已邀请 {toInvite.Count} 位好友加入群组");
    }
}

/// <summary>踢出成员（群主/管理员；不能踢群主，管理员之间不能互踢）</summary>
public sealed class KickGroupMemberCommand : ICommand<ApiResponse>
{
    public long GroupId { get; set; }

    public int OperatorId { get; set; }

    public int TargetUserId { get; set; }
}

internal sealed class KickGroupMemberHandler : IRequestHandler<KickGroupMemberCommand, ApiResponse>
{
    private readonly IGroupMemberRepository _members;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public KickGroupMemberHandler(
        IGroupMemberRepository members, IDomainEventDispatcher events, IClock clock)
    {
        _members = members;
        _events = events;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(KickGroupMemberCommand command, CancellationToken ct)
    {
        var op = await _members.GetRequiredAsync(command.GroupId, command.OperatorId, ct: ct).ConfigureAwait(false);
        var target = await _members
            .GetRequiredAsync(command.GroupId, command.TargetUserId, "目标用户不是群成员", ct)
            .ConfigureAwait(false);

        // 三条踢人规则都在 GroupMember 上
        op.EnsureCanRemove(target);

        await _members.RemoveAsync(command.GroupId, command.TargetUserId, ct).ConfigureAwait(false);

        // 通知被踢的人主动退出会话：否则他的客户端会一直留着这个群，
        // 直到自己刷新（解散群走的是同一套「客户端自动退出会话」语义）
        await _events
            .DispatchAsync(new GroupMemberRemoved(command.GroupId, command.TargetUserId, _clock.UtcNow), ct)
            .ConfigureAwait(false);

        return ApiResponse.Ok("已踢出成员");
    }
}

/// <summary>设置/取消管理员（仅群主）</summary>
public sealed class SetGroupAdminCommand : ICommand<ApiResponse>
{
    public long GroupId { get; set; }

    public int OperatorId { get; set; }

    public int TargetUserId { get; set; }

    public bool IsAdmin { get; set; }
}

internal sealed class SetGroupAdminHandler : IRequestHandler<SetGroupAdminCommand, ApiResponse>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;

    public SetGroupAdminHandler(IGroupRepository groups, IGroupMemberRepository members)
    {
        _groups = groups;
        _members = members;
    }

    public async Task<ApiResponse> Handle(SetGroupAdminCommand command, CancellationToken ct)
    {
        _ = await _groups.GetRequiredAsync(command.GroupId, ct).ConfigureAwait(false);

        var op = await _members.GetRequiredAsync(command.GroupId, command.OperatorId, ct: ct).ConfigureAwait(false);
        op.EnsureIsOwner("设置管理员");

        var target = await _members
            .GetRequiredAsync(command.GroupId, command.TargetUserId, "目标用户不是群成员", ct)
            .ConfigureAwait(false);

        target.ChangeAdminRole(command.IsAdmin);
        await _members.UpdateAsync(target, ct).ConfigureAwait(false);

        return ApiResponse.Ok(command.IsAdmin ? "已设为管理员" : "已取消管理员");
    }
}

/// <summary>设置/清除群公告（仅群主/管理员）</summary>
public sealed class SetGroupAnnouncementCommand : ICommand<ApiResponse>
{
    public long GroupId { get; set; }

    public int OperatorId { get; set; }

    public string Announcement { get; set; } = string.Empty;
}

internal sealed class SetGroupAnnouncementHandler : IRequestHandler<SetGroupAnnouncementCommand, ApiResponse>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public SetGroupAnnouncementHandler(
        IGroupRepository groups,
        IGroupMemberRepository members,
        IDomainEventDispatcher events,
        IClock clock)
    {
        _groups = groups;
        _members = members;
        _events = events;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(SetGroupAnnouncementCommand command, CancellationToken ct)
    {
        var op = await _members.GetRequiredAsync(command.GroupId, command.OperatorId, ct: ct).ConfigureAwait(false);
        op.EnsureCanManageGroup("设置公告");

        var group = await _groups.GetRequiredAsync(command.GroupId, ct).ConfigureAwait(false);
        group.SetAnnouncement(command.Announcement, command.OperatorId, _clock.UtcNow);

        await _groups.UpdateAsync(group, ct).ConfigureAwait(false);
        await _events.DispatchEventsOfAsync(group, ct).ConfigureAwait(false);

        return ApiResponse.Ok(group.Announcement is null ? "公告已清除" : "公告已更新");
    }
}

/// <summary>
/// 设置入群方式（仅群主/管理员）。
/// 默认「仅限邀请」，需要公开招募时由群内管理者显式开放 —— 这是把
/// 「谁能进群」的决定权交回群主，而不是让群 ID 的可枚举性替所有人做决定。
/// </summary>
public sealed class SetGroupJoinPolicyCommand : ICommand<ApiResponse>
{
    public long GroupId { get; set; }

    public int OperatorId { get; set; }

    /// <summary>true=开放加入（知道群 ID 即可加入），false=仅限邀请</summary>
    public bool OpenToJoin { get; set; }
}

internal sealed class SetGroupJoinPolicyHandler : IRequestHandler<SetGroupJoinPolicyCommand, ApiResponse>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public SetGroupJoinPolicyHandler(
        IGroupRepository groups,
        IGroupMemberRepository members,
        IDomainEventDispatcher events,
        IClock clock)
    {
        _groups = groups;
        _members = members;
        _events = events;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(SetGroupJoinPolicyCommand command, CancellationToken ct)
    {
        var op = await _members
            .GetRequiredAsync(command.GroupId, command.OperatorId, ct: ct)
            .ConfigureAwait(false);
        op.EnsureCanManageGroup("修改入群方式");

        var group = await _groups.GetRequiredAsync(command.GroupId, ct).ConfigureAwait(false);
        group.SetJoinPolicy(
            command.OpenToJoin ? GroupJoinPolicy.Open : GroupJoinPolicy.InviteOnly,
            command.OperatorId,
            _clock.UtcNow);

        await _groups.UpdateAsync(group, ct).ConfigureAwait(false);
        await _events.DispatchEventsOfAsync(group, ct).ConfigureAwait(false);

        return ApiResponse.Ok(group.IsOpenToJoin ? "已允许任何人加入" : "已改为仅限邀请加入");
    }
}
