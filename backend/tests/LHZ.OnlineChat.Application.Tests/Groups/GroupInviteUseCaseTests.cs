using LHZ.OnlineChat.Application.Groups;
using LHZ.OnlineChat.Application.Groups.Commands;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Application.Tests.TestDoubles;
using LHZ.OnlineChat.Application.Groups.EventHandlers;

namespace LHZ.OnlineChat.Application.Tests.Groups;

/// <summary>
/// 邀请链接与入群审批的用例层。
/// 覆盖三类容易出事的点：权限（谁能生成/审批）、准入（链接失效/已在群/免审批直进）、
/// 状态机（幂等申请、重复审批、跨群 requestId）。
/// </summary>
public class GroupInviteLinkUseCaseTests
{
    private readonly ApplicationTestContext _ctx = new();

    private CreateGroupInviteLinkHandler CreateHandler()
        => new(_ctx.Groups, _ctx.GroupMembers, _ctx.Clock);

    private GetGroupInviteLinkHandler GetHandler()
        => new(_ctx.Groups, _ctx.GroupMembers, _ctx.Clock);

    private DisableGroupInviteLinkHandler DisableHandler()
        => new(_ctx.Groups, _ctx.GroupMembers);

    private SetGroupJoinApprovalHandler ApprovalHandler()
        => new(_ctx.Groups, _ctx.GroupMembers);

    [Fact(DisplayName = "群主生成链接：token 足够长、有 7 天有效期、默认需要审批")]
    public async Task Create_ByOwner_ReturnsTokenWithExpiry()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var result = await CreateHandler().Handle(
            new CreateGroupInviteLinkCommand { GroupId = group.Id, OperatorId = owner.Id }, default);

        var link = result.Data!;
        Assert.True(link.Token!.Length >= Group.InviteTokenMinLength);
        Assert.Equal(_ctx.Clock.UtcNow.AddDays(Group.InviteLinkTtlDays), link.ExpiresAt);
        Assert.True(link.RequiresApproval);
        Assert.Equal(link.Token, (await _ctx.Groups.GetRequiredAsync(group.Id))!.InviteToken);
    }

    [Fact(DisplayName = "重复生成会换掉旧 token")]
    public async Task Create_Twice_ReplacesToken()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var first = await CreateHandler().Handle(
            new CreateGroupInviteLinkCommand { GroupId = group.Id, OperatorId = owner.Id }, default);
        var second = await CreateHandler().Handle(
            new CreateGroupInviteLinkCommand { GroupId = group.Id, OperatorId = owner.Id }, default);

        Assert.NotEqual(first.Data!.Token, second.Data!.Token);
    }

    [Fact(DisplayName = "普通成员不能生成邀请链接")]
    public async Task Create_ByMember_IsRejected()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => CreateHandler().Handle(
            new CreateGroupInviteLinkCommand { GroupId = group.Id, OperatorId = member.Id }, default));

        Assert.Contains("管理员", ex.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "查看链接：未开启时 token 为空")]
    public async Task Get_NoLink_ReturnsNullToken()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var result = await GetHandler().Handle(
            new GetGroupInviteLinkQuery { GroupId = group.Id, RequesterId = owner.Id }, default);

        Assert.Null(result.Data!.Token);
        Assert.True(result.Data.RequiresApproval);
    }

    [Fact(DisplayName = "关闭链接后 token 为空")]
    public async Task Disable_ClearsToken()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);
        await CreateHandler().Handle(
            new CreateGroupInviteLinkCommand { GroupId = group.Id, OperatorId = owner.Id }, default);

        await DisableHandler().Handle(
            new DisableGroupInviteLinkCommand { GroupId = group.Id, OperatorId = owner.Id }, default);

        Assert.Null((await _ctx.Groups.GetRequiredAsync(group.Id))!.InviteToken);
    }

    [Fact(DisplayName = "审批开关可关闭（凭链接直接入群）")]
    public async Task SetApproval_False_Persists()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        await ApprovalHandler().Handle(new SetGroupJoinApprovalCommand
        {
            GroupId = group.Id, OperatorId = owner.Id, Required = false
        }, default);

        Assert.False((await _ctx.Groups.GetRequiredAsync(group.Id))!.JoinApprovalRequired);
    }
}

public class GroupJoinRequestUseCaseTests
{
    private readonly ApplicationTestContext _ctx = new();

    private const string Token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>
    /// 测试里的 IDomainEventDispatcher 只记录事件、不跑订阅方，
    /// 想验证谁收到了推送必须显式订阅（与 GroupUseCaseTests 的做法一致）。
    /// </summary>
    private void SubscribeHandlers()
    {
        _ctx.Events.Subscribe(new NotifyOnGroupJoinRequested(_ctx.Notifier, _ctx.GroupMembers));
        _ctx.Events.Subscribe(new NotifyOnGroupJoinRequestDecided(_ctx.Notifier));
        _ctx.Events.Subscribe(new NotifyOnGroupMembersInvited(_ctx.Notifier));
    }

    private CreateGroupInviteLinkHandler CreateHandler()
        => new(_ctx.Groups, _ctx.GroupMembers, _ctx.Clock);

    private GetGroupInvitePreviewHandler PreviewHandler()
        => new(_ctx.Groups, _ctx.GroupMembers, _ctx.JoinRequests, _ctx.Clock);

    private SubmitJoinByTokenHandler SubmitHandler()
        => new(_ctx.Groups, _ctx.GroupMembers, _ctx.GroupMessages, _ctx.JoinRequests, _ctx.Events, _ctx.Clock);

    private ApproveGroupJoinRequestHandler ApproveHandler()
        => new(_ctx.GroupMembers, _ctx.GroupMessages, _ctx.JoinRequests, _ctx.Events, _ctx.Clock);

    private RejectGroupJoinRequestHandler RejectHandler()
        => new(_ctx.GroupMembers, _ctx.JoinRequests, _ctx.Events, _ctx.Clock);

    private GetGroupJoinRequestsHandler ListHandler()
        => new(_ctx.GroupMembers, _ctx.JoinRequests, _ctx.Users);

    /// <summary>建一个已开启邀请链接（默认需审批）的群，返回 (群主, 群, token)</summary>
    private async Task<(Domain.Users.User Owner, Group Group, string Token)> GroupWithLinkAsync(
        bool approvalRequired = true)
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var group = _ctx.GivenGroup(owner.Id);

        var created = await CreateHandler().Handle(
            new CreateGroupInviteLinkCommand { GroupId = group.Id, OperatorId = owner.Id }, default);
        var token = created.Data!.Token!;

        if (!approvalRequired)
        {
            group.SetJoinApprovalRequired(false);
            await _ctx.Groups.UpdateAsync(group);
        }

        return (owner, group, token);
    }

    [Fact(DisplayName = "预览：有效 token 返回群名与人数，并标出是否需要审批")]
    public async Task Preview_ValidToken_ReturnsGroupInfo()
    {
        var (_, group, token) = await GroupWithLinkAsync();
        var user = _ctx.GivenUser("路人", "u@test.local");

        var result = await PreviewHandler().Handle(
            new GetGroupInvitePreviewQuery { Token = token, UserId = user.Id }, default);

        Assert.Equal(group.Id, result.Data!.GroupId);
        Assert.Equal("测试群", result.Data.Name);
        Assert.True(result.Data.RequiresApproval);
        Assert.False(result.Data.AlreadyMember);
        Assert.False(result.Data.HasPendingRequest);
    }

    [Fact(DisplayName = "预览：无效 token 提示「已失效」")]
    public async Task Preview_InvalidToken_IsRejected()
    {
        var user = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(() => PreviewHandler().Handle(
            new GetGroupInvitePreviewQuery { Token = "not-a-real-token", UserId = user.Id }, default));

        Assert.Equal("邀请链接已失效，请让群主重新生成", ex.Message);
    }

    [Fact(DisplayName = "预览：过期链接提示「已失效」")]
    public async Task Preview_ExpiredToken_IsRejected()
    {
        var (_, group, token) = await GroupWithLinkAsync();
        var user = _ctx.GivenUser("路人", "u@test.local");
        _ctx.Clock.Advance(TimeSpan.FromDays(Group.InviteLinkTtlDays + 1));

        var ex = await Assert.ThrowsAsync<DomainException>(() => PreviewHandler().Handle(
            new GetGroupInvitePreviewQuery { Token = token, UserId = user.Id }, default));

        Assert.Equal("邀请链接已失效，请让群主重新生成", ex.Message);
        Assert.NotNull(group);
    }

    [Fact(DisplayName = "需审批：提交申请后不写成员，只落一条待审批并派发事件")]
    public async Task Submit_RequiresApproval_CreatesPendingOnly()
    {
        SubscribeHandlers();
        var (_, group, token) = await GroupWithLinkAsync();
        var user = _ctx.GivenUser("路人", "u@test.local");

        var result = await SubmitHandler().Handle(
            new SubmitJoinByTokenCommand { Token = token, UserId = user.Id, Message = "想进群" }, default);

        Assert.False(result.Data!.Joined);
        Assert.False(await _ctx.GroupMembers.ExistsAsync(group.Id, user.Id));
        var pending = Assert.Single(_ctx.JoinRequests.All);
        Assert.Equal(JoinRequestStatus.Pending, pending.Status);
        Assert.Equal("想进群", pending.Message);
        Assert.True(_ctx.Events.Has<GroupJoinRequested>());
    }

    [Fact(DisplayName = "需审批：重复提交幂等，不产生第二条申请")]
    public async Task Submit_Twice_IsIdempotent()
    {
        var (_, _, token) = await GroupWithLinkAsync();
        var user = _ctx.GivenUser("路人", "u@test.local");

        await SubmitHandler().Handle(new SubmitJoinByTokenCommand { Token = token, UserId = user.Id }, default);
        await SubmitHandler().Handle(new SubmitJoinByTokenCommand { Token = token, UserId = user.Id }, default);

        Assert.Single(_ctx.JoinRequests.All);
    }

    [Fact(DisplayName = "免审批：直接入群，读游标=群最新消息（不补发历史）")]
    public async Task Submit_NoApproval_JoinsDirectly()
    {
        SubscribeHandlers();
        var (owner, group, token) = await GroupWithLinkAsync(approvalRequired: false);
        _ctx.GivenGroupMessage(group.Id, owner.Id, "入群前的历史");
        var user = _ctx.GivenUser("路人", "u@test.local");
        var latest = await _ctx.GroupMessages.MaxIdOfGroupAsync(group.Id);

        var result = await SubmitHandler().Handle(
            new SubmitJoinByTokenCommand { Token = token, UserId = user.Id }, default);

        Assert.True(result.Data!.Joined);
        var member = await _ctx.GroupMembers.GetRequiredAsync(group.Id, user.Id);
        Assert.Equal(latest, member.LastReadMessageId);
        Assert.Empty(_ctx.JoinRequests.All);
        // 推给自己的其他设备，让它们把群加进会话列表
        Assert.True(_ctx.Notifier.Any(PushKind.GroupInvited));
    }

    [Fact(DisplayName = "已是成员：凭链接再点提示已在群中")]
    public async Task Submit_AlreadyMember_IsRejected()
    {
        var (_, group, token) = await GroupWithLinkAsync();
        var owner = (await _ctx.GroupMembers.ListOfGroupAsync(group.Id))
            .First(m => m.Role == GroupRole.Owner);

        var ex = await Assert.ThrowsAsync<DomainException>(() => SubmitHandler().Handle(
            new SubmitJoinByTokenCommand { Token = token, UserId = owner.UserId }, default));

        Assert.Equal("你已经是该群组成员", ex.Message);
    }

    [Fact(DisplayName = "审批通过：写入成员并通知申请人")]
    public async Task Approve_CreatesMember_AndNotifiesApplicant()
    {
        SubscribeHandlers();
        var (owner, group, token) = await GroupWithLinkAsync();
        var user = _ctx.GivenUser("路人", "u@test.local");
        await SubmitHandler().Handle(new SubmitJoinByTokenCommand { Token = token, UserId = user.Id }, default);
        var request = Assert.Single(_ctx.JoinRequests.All);

        var result = await ApproveHandler().Handle(new ApproveGroupJoinRequestCommand
        {
            GroupId = group.Id, RequestId = request.Id, OperatorId = owner.Id
        }, default);

        Assert.Equal("已通过入群申请", result.Message);
        Assert.True(await _ctx.GroupMembers.ExistsAsync(group.Id, user.Id));
        Assert.Equal(JoinRequestStatus.Approved, request.Status);
        var push = Assert.Single(_ctx.Notifier.OfKind(PushKind.GroupJoinResult));
        Assert.Equal(user.Id, push.ToUserId);
        Assert.Equal("approved", push.Content);
    }

    [Fact(DisplayName = "审批通过：重复点通过被拒（状态机只能处理一次）")]
    public async Task Approve_Twice_IsRejected()
    {
        var (owner, group, token) = await GroupWithLinkAsync();
        var user = _ctx.GivenUser("路人", "u@test.local");
        await SubmitHandler().Handle(new SubmitJoinByTokenCommand { Token = token, UserId = user.Id }, default);
        var request = Assert.Single(_ctx.JoinRequests.All);

        await ApproveHandler().Handle(new ApproveGroupJoinRequestCommand
        {
            GroupId = group.Id, RequestId = request.Id, OperatorId = owner.Id
        }, default);

        await Assert.ThrowsAsync<DomainException>(() => ApproveHandler().Handle(
            new ApproveGroupJoinRequestCommand
            {
                GroupId = group.Id, RequestId = request.Id, OperatorId = owner.Id
            }, default));
    }

    [Fact(DisplayName = "审批：跨群 requestId 被拒")]
    public async Task Approve_CrossGroupRequest_IsRejected()
    {
        var (owner, group, token) = await GroupWithLinkAsync();
        var user = _ctx.GivenUser("路人", "u@test.local");
        await SubmitHandler().Handle(new SubmitJoinByTokenCommand { Token = token, UserId = user.Id }, default);
        var request = Assert.Single(_ctx.JoinRequests.All);

        var otherGroup = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => ApproveHandler().Handle(
            new ApproveGroupJoinRequestCommand
            {
                GroupId = otherGroup.Id, RequestId = request.Id, OperatorId = owner.Id
            }, default));

        Assert.Equal("该申请不属于这个群", ex.Message);
    }

    [Fact(DisplayName = "审批：普通成员不能处理申请")]
    public async Task Approve_ByMember_IsRejected()
    {
        var (owner, group, token) = await GroupWithLinkAsync();
        var member = _ctx.GivenUser("成员", "m@test.local");
        await _ctx.GroupMembers.AddAsync(GroupMember.Join(group.Id, member.Id, 0, _ctx.Clock.UtcNow));

        var applicant = _ctx.GivenUser("路人", "u@test.local");
        await SubmitHandler().Handle(
            new SubmitJoinByTokenCommand { Token = token, UserId = applicant.Id }, default);
        var request = Assert.Single(_ctx.JoinRequests.All);

        await Assert.ThrowsAsync<DomainException>(() => ApproveHandler().Handle(
            new ApproveGroupJoinRequestCommand
            {
                GroupId = group.Id, RequestId = request.Id, OperatorId = member.Id
            }, default));

        Assert.NotNull(owner);
    }

    [Fact(DisplayName = "拒绝：不写成员，并通知申请人")]
    public async Task Reject_DoesNotAddMember_AndNotifies()
    {
        SubscribeHandlers();
        var (owner, group, token) = await GroupWithLinkAsync();
        var user = _ctx.GivenUser("路人", "u@test.local");
        await SubmitHandler().Handle(new SubmitJoinByTokenCommand { Token = token, UserId = user.Id }, default);
        var request = Assert.Single(_ctx.JoinRequests.All);

        await RejectHandler().Handle(new RejectGroupJoinRequestCommand
        {
            GroupId = group.Id, RequestId = request.Id, OperatorId = owner.Id
        }, default);

        Assert.False(await _ctx.GroupMembers.ExistsAsync(group.Id, user.Id));
        Assert.Equal(JoinRequestStatus.Rejected, request.Status);
        var push = Assert.Single(_ctx.Notifier.OfKind(PushKind.GroupJoinResult));
        Assert.Equal("rejected", push.Content);
    }

    [Fact(DisplayName = "列表：管理员看得到待审批，普通成员看不到")]
    public async Task List_OnlyManagersCanSee()
    {
        var (owner, group, token) = await GroupWithLinkAsync();
        var applicant = _ctx.GivenUser("路人", "u@test.local");
        await SubmitHandler().Handle(
            new SubmitJoinByTokenCommand { Token = token, UserId = applicant.Id }, default);

        var admin = _ctx.GivenUser("管理员", "a@test.local");
        var adminMember = GroupMember.Join(group.Id, admin.Id, 0, _ctx.Clock.UtcNow);
        await _ctx.GroupMembers.AddAsync(adminMember);
        adminMember.ChangeAdminRole(true);
        await _ctx.GroupMembers.UpdateAsync(adminMember);

        var visible = await ListHandler().Handle(
            new GetGroupJoinRequestsQuery { GroupId = group.Id, RequesterId = owner.Id }, default);
        var item = Assert.Single(visible.Data!);
        Assert.Equal(applicant.Id, item.UserId);
        Assert.Equal("路人", item.Nickname);

        var member = _ctx.GivenUser("成员", "m@test.local");
        await _ctx.GroupMembers.AddAsync(GroupMember.Join(group.Id, member.Id, 0, _ctx.Clock.UtcNow));
        await Assert.ThrowsAsync<DomainException>(() => ListHandler().Handle(
            new GetGroupJoinRequestsQuery { GroupId = group.Id, RequesterId = member.Id }, default));
    }

    [Fact(DisplayName = "上限：单用户跨群待审批超过 20 条被拒")]
    public async Task Submit_TooManyPendingForUser_IsRejected()
    {
        // 一个群装满 20 条待审批不容易构造（同一人同群只有一条），
        // 这里直接验证上限逻辑：先占满 20 个群，再申请第 21 个
        var user = _ctx.GivenUser("路人", "u@test.local");
        for (var i = 0; i < SubmitJoinByTokenHandler.MaxPendingPerUser; i++)
        {
            var owner = _ctx.GivenUser($"群主{i}", $"o{i}@test.local");
            var group = _ctx.GivenGroup(owner.Id);
            var created = await CreateHandler().Handle(
                new CreateGroupInviteLinkCommand { GroupId = group.Id, OperatorId = owner.Id }, default);
            await SubmitHandler().Handle(
                new SubmitJoinByTokenCommand { Token = created.Data!.Token!, UserId = user.Id }, default);
        }

        var lastOwner = _ctx.GivenUser("最后一个群主", "last@test.local");
        var lastGroup = _ctx.GivenGroup(lastOwner.Id);
        var lastLink = await CreateHandler().Handle(
            new CreateGroupInviteLinkCommand { GroupId = lastGroup.Id, OperatorId = lastOwner.Id }, default);

        var ex = await Assert.ThrowsAsync<DomainException>(() => SubmitHandler().Handle(
            new SubmitJoinByTokenCommand { Token = lastLink.Data!.Token!, UserId = user.Id }, default));

        Assert.Equal("你提交的入群申请过多，请等待处理", ex.Message);
    }
}