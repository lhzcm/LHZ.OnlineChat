using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Groups.Commands;

/// <summary>凭 token 预览群信息（邀请页用；未登录会被控制器拦掉）</summary>
public sealed class GetGroupInvitePreviewQuery : IQuery<ApiResponse<GroupInvitePreview>>
{
    public string Token { get; set; } = string.Empty;

    public int UserId { get; set; }
}

internal sealed class GetGroupInvitePreviewHandler
    : IRequestHandler<GetGroupInvitePreviewQuery, ApiResponse<GroupInvitePreview>>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;
    private readonly IGroupJoinRequestRepository _joinRequests;
    private readonly IClock _clock;

    public GetGroupInvitePreviewHandler(
        IGroupRepository groups,
        IGroupMemberRepository members,
        IGroupJoinRequestRepository joinRequests,
        IClock clock)
    {
        _groups = groups;
        _members = members;
        _joinRequests = joinRequests;
        _clock = clock;
    }

    public async Task<ApiResponse<GroupInvitePreview>> Handle(
        GetGroupInvitePreviewQuery query, CancellationToken ct)
    {
        var group = await FindGroupByTokenAsync(_groups, query.Token, ct).ConfigureAwait(false);
        group.EnsureInviteLinkUsable(_clock.UtcNow);

        var counts = await _members
            .CountByGroupAsync([group.Id], ct)
            .ConfigureAwait(false);
        var already = await _members.ExistsAsync(group.Id, query.UserId, ct).ConfigureAwait(false);
        var pending = await _joinRequests
            .FindPendingAsync(group.Id, query.UserId, ct)
            .ConfigureAwait(false);

        return ApiResponse<GroupInvitePreview>.Ok(new GroupInvitePreview
        {
            GroupId = group.Id,
            Name = group.Name,
            Avatar = group.Avatar,
            MemberCount = counts.GetValueOrDefault(group.Id),
            RequiresApproval = group.JoinApprovalRequired,
            AlreadyMember = already,
            HasPendingRequest = pending is not null
        });
    }

    /// <summary>
    /// 按 token 反查群；查不到与过期都用同一句提示 —— 不让外人区分「群不存在」和「链接失效」。
    /// </summary>
    internal static async Task<Group> FindGroupByTokenAsync(
        IGroupRepository groups, string token, CancellationToken ct)
    {
        var normalized = (token ?? string.Empty).Trim();
        DomainException.Ensure(normalized.Length > 0, "邀请链接已失效，请让群主重新生成");

        var group = await groups.FindByInviteTokenAsync(normalized, ct).ConfigureAwait(false);
        DomainException.Ensure(group is not null, "邀请链接已失效，请让群主重新生成");
        return group!;
    }
}

/// <summary>凭邀请链接加入（需审批时改为提交申请）</summary>
public sealed class SubmitJoinByTokenCommand : ICommand<ApiResponse<JoinGroupResult>>
{
    public string Token { get; set; } = string.Empty;

    public int UserId { get; set; }

    /// <summary>申请附言（可选）</summary>
    public string? Message { get; set; }
}

internal sealed class SubmitJoinByTokenHandler
    : IRequestHandler<SubmitJoinByTokenCommand, ApiResponse<JoinGroupResult>>
{
    /// <summary>单群待审批上限：防止一个公开链接被脚本刷爆管理端列表</summary>
    internal const int MaxPendingPerGroup = 200;

    /// <summary>单用户跨群待审批上限：防止一个人把每个群都申请一遍</summary>
    internal const int MaxPendingPerUser = 20;

    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;
    private readonly IGroupMessageRepository _messages;
    private readonly IGroupJoinRequestRepository _joinRequests;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public SubmitJoinByTokenHandler(
        IGroupRepository groups,
        IGroupMemberRepository members,
        IGroupMessageRepository messages,
        IGroupJoinRequestRepository joinRequests,
        IDomainEventDispatcher events,
        IClock clock)
    {
        _groups = groups;
        _members = members;
        _messages = messages;
        _joinRequests = joinRequests;
        _events = events;
        _clock = clock;
    }

    public async Task<ApiResponse<JoinGroupResult>> Handle(
        SubmitJoinByTokenCommand command, CancellationToken ct)
    {
        var group = await GetGroupInvitePreviewHandler
            .FindGroupByTokenAsync(_groups, command.Token, ct)
            .ConfigureAwait(false);

        var now = _clock.UtcNow;
        group.EnsureInviteLinkUsable(now);

        var already = await _members
            .ExistsAsync(group.Id, command.UserId, ct)
            .ConfigureAwait(false);
        DomainException.Ensure(!already, "你已经是该群组成员");

        // 免审批：链接本身就是准入凭证（不看 JoinPolicy ——「开放加入」是另一条入口）
        if (!group.JoinApprovalRequired)
        {
            await GroupMembershipJoiner
                .JoinAsync(_members, _messages, group.Id, command.UserId, now, ct)
                .ConfigureAwait(false);

            // 推给自己的其他在线设备：它们需要把该群加进会话列表
            await _events
                .DispatchAsync(
                    new GroupMembersInvited(group.Id, [command.UserId], command.UserId, now), ct)
                .ConfigureAwait(false);

            return ApiResponse<JoinGroupResult>.Ok(new JoinGroupResult
            {
                GroupId = group.Id,
                GroupName = group.Name,
                Joined = true
            }, $"已加入群「{group.Name}」");
        }

        // 需要审批：已有待审批申请时幂等返回，不重复打扰管理员
        var existing = await _joinRequests
            .FindPendingAsync(group.Id, command.UserId, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return ApiResponse<JoinGroupResult>.Ok(new JoinGroupResult
            {
                GroupId = group.Id,
                GroupName = group.Name,
                Joined = false
            }, "已提交申请，请等待管理员审批");
        }

        var groupPending = await _joinRequests
            .CountPendingOfGroupAsync(group.Id, ct)
            .ConfigureAwait(false);
        DomainException.Ensure(groupPending < MaxPendingPerGroup, "该群待审批申请已满，请稍后再试");

        var userPending = await _joinRequests
            .CountPendingOfUserAsync(command.UserId, ct)
            .ConfigureAwait(false);
        DomainException.Ensure(userPending < MaxPendingPerUser, "你提交的入群申请过多，请等待处理");

        var request = GroupJoinRequest.Submit(group.Id, command.UserId, command.Message, now);
        await _joinRequests.AddAsync(request, ct).ConfigureAwait(false);

        await _events
            .DispatchAsync(new GroupJoinRequested(group.Id, request.Id, command.UserId, now), ct)
            .ConfigureAwait(false);

        return ApiResponse<JoinGroupResult>.Ok(new JoinGroupResult
        {
            GroupId = group.Id,
            GroupName = group.Name,
            Joined = false
        }, "已提交申请，请等待管理员审批");
    }
}

/// <summary>通过入群申请（群主/管理员）</summary>
public sealed class ApproveGroupJoinRequestCommand : ICommand<ApiResponse>
{
    public long GroupId { get; set; }

    public long RequestId { get; set; }

    public int OperatorId { get; set; }
}

internal sealed class ApproveGroupJoinRequestHandler
    : IRequestHandler<ApproveGroupJoinRequestCommand, ApiResponse>
{
    private readonly IGroupMemberRepository _members;
    private readonly IGroupMessageRepository _messages;
    private readonly IGroupJoinRequestRepository _joinRequests;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public ApproveGroupJoinRequestHandler(
        IGroupMemberRepository members,
        IGroupMessageRepository messages,
        IGroupJoinRequestRepository joinRequests,
        IDomainEventDispatcher events,
        IClock clock)
    {
        _members = members;
        _messages = messages;
        _joinRequests = joinRequests;
        _events = events;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(ApproveGroupJoinRequestCommand command, CancellationToken ct)
    {
        var op = await _members
            .GetRequiredAsync(command.GroupId, command.OperatorId, ct: ct)
            .ConfigureAwait(false);
        op.EnsureCanManageGroup("审批入群申请");

        var request = await _joinRequests.GetRequiredAsync(command.RequestId, ct).ConfigureAwait(false);
        EnsureSameGroup(request, command.GroupId);
        request.EnsurePending();

        var now = _clock.UtcNow;
        var already = await _members
            .ExistsAsync(request.GroupId, request.UserId, ct)
            .ConfigureAwait(false);

        // 申请期间被群主直接拉进群：标记为通过即可，不要再插一条成员记录
        if (!already)
        {
            await GroupMembershipJoiner
                .JoinAsync(_members, _messages, request.GroupId, request.UserId, now, ct)
                .ConfigureAwait(false);
        }

        request.Approve(command.OperatorId, now);
        await _joinRequests.UpdateAsync(request, ct).ConfigureAwait(false);

        await _events
            .DispatchAsync(
                new GroupJoinRequestDecided(request.GroupId, request.Id, request.UserId, true, now), ct)
            .ConfigureAwait(false);

        return ApiResponse.Ok(already ? "该用户已在群中，已标记为通过" : "已通过入群申请");
    }

    private static void EnsureSameGroup(GroupJoinRequest request, long groupId)
        => DomainException.Ensure(request.GroupId == groupId, "该申请不属于这个群");
}

/// <summary>拒绝入群申请（群主/管理员）</summary>
public sealed class RejectGroupJoinRequestCommand : ICommand<ApiResponse>
{
    public long GroupId { get; set; }

    public long RequestId { get; set; }

    public int OperatorId { get; set; }
}

internal sealed class RejectGroupJoinRequestHandler
    : IRequestHandler<RejectGroupJoinRequestCommand, ApiResponse>
{
    private readonly IGroupMemberRepository _members;
    private readonly IGroupJoinRequestRepository _joinRequests;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public RejectGroupJoinRequestHandler(
        IGroupMemberRepository members,
        IGroupJoinRequestRepository joinRequests,
        IDomainEventDispatcher events,
        IClock clock)
    {
        _members = members;
        _joinRequests = joinRequests;
        _events = events;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(RejectGroupJoinRequestCommand command, CancellationToken ct)
    {
        var op = await _members
            .GetRequiredAsync(command.GroupId, command.OperatorId, ct: ct)
            .ConfigureAwait(false);
        op.EnsureCanManageGroup("审批入群申请");

        var request = await _joinRequests.GetRequiredAsync(command.RequestId, ct).ConfigureAwait(false);
        DomainException.Ensure(request.GroupId == command.GroupId, "该申请不属于这个群");
        request.EnsurePending();

        var now = _clock.UtcNow;
        request.Reject(command.OperatorId, now);
        await _joinRequests.UpdateAsync(request, ct).ConfigureAwait(false);

        await _events
            .DispatchAsync(
                new GroupJoinRequestDecided(request.GroupId, request.Id, request.UserId, false, now), ct)
            .ConfigureAwait(false);

        return ApiResponse.Ok("已拒绝该入群申请");
    }
}

/// <summary>待审批的入群申请列表（群主/管理员）</summary>
public sealed class GetGroupJoinRequestsQuery : IQuery<ApiResponse<List<GroupJoinRequestInfo>>>
{
    public long GroupId { get; set; }

    public int RequesterId { get; set; }
}

internal sealed class GetGroupJoinRequestsHandler
    : IRequestHandler<GetGroupJoinRequestsQuery, ApiResponse<List<GroupJoinRequestInfo>>>
{
    /// <summary>一次最多返回多少条待审批（再多请先去处理旧的）</summary>
    internal const int MaxItems = 100;

    private readonly IGroupMemberRepository _members;
    private readonly IGroupJoinRequestRepository _joinRequests;
    private readonly IUserRepository _users;

    public GetGroupJoinRequestsHandler(
        IGroupMemberRepository members,
        IGroupJoinRequestRepository joinRequests,
        IUserRepository users)
    {
        _members = members;
        _joinRequests = joinRequests;
        _users = users;
    }

    public async Task<ApiResponse<List<GroupJoinRequestInfo>>> Handle(
        GetGroupJoinRequestsQuery query, CancellationToken ct)
    {
        var op = await _members
            .GetRequiredAsync(query.GroupId, query.RequesterId, ct: ct)
            .ConfigureAwait(false);
        op.EnsureCanManageGroup("查看入群申请");

        var requests = await _joinRequests
            .ListPendingOfGroupAsync(query.GroupId, MaxItems, ct)
            .ConfigureAwait(false);
        if (requests.Count == 0) return ApiResponse<List<GroupJoinRequestInfo>>.Ok([]);

        // 一次批量取用户，避免逐条查（N+1）
        var users = await _users
            .GetManyAsync(requests.Select(r => r.UserId).Distinct(), ct)
            .ConfigureAwait(false);

        var items = requests.Select(r =>
        {
            var user = users.GetValueOrDefault(r.UserId);
            return new GroupJoinRequestInfo
            {
                Id = r.Id,
                UserId = r.UserId,
                Nickname = user?.Nickname ?? $"用户{r.UserId}",
                Avatar = user?.Avatar,
                Message = r.Message,
                CreatedAt = UtcTime.Normalize(r.CreatedAt)
            };
        }).ToList();

        return ApiResponse<List<GroupJoinRequestInfo>>.Ok(items);
    }
}
