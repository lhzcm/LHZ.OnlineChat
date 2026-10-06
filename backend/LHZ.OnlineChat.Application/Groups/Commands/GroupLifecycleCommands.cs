using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using MediatR;

namespace LHZ.OnlineChat.Application.Groups.Commands;

/// <summary>创建群组（创建者自动成为群主）</summary>
public sealed class CreateGroupCommand : ICommand<ApiResponse<GroupInfo>>
{
    public int OwnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Avatar { get; set; }
}

internal sealed class CreateGroupHandler : IRequestHandler<CreateGroupCommand, ApiResponse<GroupInfo>>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public CreateGroupHandler(
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

    public async Task<ApiResponse<GroupInfo>> Handle(CreateGroupCommand command, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var group = Group.Create(command.Name, command.Avatar, command.OwnerId, now);

        await _groups.AddAsync(group, ct).ConfigureAwait(false);
        await _members
            .AddAsync(GroupMember.CreateOwner(group.Id, command.OwnerId, now), ct)
            .ConfigureAwait(false);

        await _events
            .DispatchAsync(new GroupCreated(group.Id, group.Name, command.OwnerId, now), ct)
            .ConfigureAwait(false);

        return ApiResponse<GroupInfo>.Ok(new GroupInfo
        {
            Id = group.Id,
            Name = group.Name,
            Avatar = group.Avatar,
            OwnerId = command.OwnerId,
            MemberCount = 1,
            CreatedAt = UtcTime.Normalize(group.CreatedAt),
            MyRole = (int)GroupRole.Owner,
            JoinPolicy = (int)group.JoinPolicy,
            IsOpenToJoin = group.IsOpenToJoin
        }, "群组创建成功");
    }
}

/// <summary>加入群组</summary>
public sealed class JoinGroupCommand : ICommand<ApiResponse>
{
    public long GroupId { get; set; }

    public int UserId { get; set; }
}

internal sealed class JoinGroupHandler : IRequestHandler<JoinGroupCommand, ApiResponse>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;
    private readonly IGroupMessageRepository _messages;
    private readonly IClock _clock;

    public JoinGroupHandler(
        IGroupRepository groups,
        IGroupMemberRepository members,
        IGroupMessageRepository messages,
        IClock clock)
    {
        _groups = groups;
        _members = members;
        _messages = messages;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(JoinGroupCommand command, CancellationToken ct)
    {
        var group = await _groups.GetRequiredAsync(command.GroupId, ct).ConfigureAwait(false);

        var already = await _members.ExistsAsync(command.GroupId, command.UserId, ct).ConfigureAwait(false);
        DomainException.Ensure(!already, "你已经是该群组成员");

        // 准入校验：群 ID 是连续自增的，若默认可自行加入，任何人都能枚举 ID 进群，
        // 再顺着历史/搜索接口读走该群全部消息。已是成员的情况上面已经返回，
        // 所以这里只拦「想新加入的人」。校验在任何写入之前。
        group.EnsureJoinable();

        // 已读游标 = 当前最新消息，避免入群前的历史被当成离线消息补发
        var latestId = await _messages.MaxIdOfGroupAsync(command.GroupId, ct).ConfigureAwait(false);
        var member = GroupMember.Join(command.GroupId, command.UserId, latestId, _clock.UtcNow);

        await _members.AddAsync(member, ct).ConfigureAwait(false);
        return ApiResponse.Ok("加入群组成功");
    }
}

/// <summary>退出群组（群主须先转让或解散）</summary>
public sealed class LeaveGroupCommand : ICommand<ApiResponse>
{
    public long GroupId { get; set; }

    public int UserId { get; set; }
}

internal sealed class LeaveGroupHandler : IRequestHandler<LeaveGroupCommand, ApiResponse>
{
    private readonly IGroupMemberRepository _members;

    public LeaveGroupHandler(IGroupMemberRepository members) => _members = members;

    public async Task<ApiResponse> Handle(LeaveGroupCommand command, CancellationToken ct)
    {
        var member = await _members
            .GetRequiredAsync(command.GroupId, command.UserId, ct: ct)
            .ConfigureAwait(false);

        member.EnsureCanLeave();

        await _members.RemoveAsync(command.GroupId, command.UserId, ct).ConfigureAwait(false);
        return ApiResponse.Ok("已退出群组");
    }
}

/// <summary>解散群组（仅群主）</summary>
public sealed class DismissGroupCommand : ICommand<ApiResponse>
{
    public long GroupId { get; set; }

    public int UserId { get; set; }
}

internal sealed class DismissGroupHandler : IRequestHandler<DismissGroupCommand, ApiResponse>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public DismissGroupHandler(
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

    public async Task<ApiResponse> Handle(DismissGroupCommand command, CancellationToken ct)
    {
        var group = await _groups.GetRequiredAsync(command.GroupId, ct).ConfigureAwait(false);
        group.EnsureCanBeDismissedBy(command.UserId);

        var memberIds = await _members.ListMemberIdsAsync(command.GroupId, ct).ConfigureAwait(false);

        await _members.DeleteAllOfGroupAsync(command.GroupId, ct).ConfigureAwait(false);
        await _groups.DeleteAsync(command.GroupId, ct).ConfigureAwait(false);

        group.Dissolve(memberIds, _clock.UtcNow);
        await _events.DispatchEventsOfAsync(group, ct).ConfigureAwait(false);

        return ApiResponse.Ok("群组已解散");
    }
}
