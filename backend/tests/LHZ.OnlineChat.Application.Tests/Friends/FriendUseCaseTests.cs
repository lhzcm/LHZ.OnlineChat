using LHZ.OnlineChat.Application.Blacklists;
using LHZ.OnlineChat.Application.Friends.Commands;
using LHZ.OnlineChat.Application.Friends.EventHandlers;
using LHZ.OnlineChat.Application.Friends.Queries;
using LHZ.OnlineChat.Application.Tests.TestDoubles;
using LHZ.OnlineChat.Domain.Blacklists;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Friends;

namespace LHZ.OnlineChat.Application.Tests.Friends;

public class SendFriendRequestTests
{
    private readonly ApplicationTestContext _ctx = new();

    private SendFriendRequestHandler Handler()
        => new(_ctx.Users, _ctx.Friendships, _ctx.Blacklist, _ctx.Events, _ctx.Clock);

    [Fact(DisplayName = "申请成功并发出事件")]
    public async Task SendRequest_Succeeds_RaisesEvent()
    {
        var a = _ctx.GivenUser("张三", "a@test.local");
        var b = _ctx.GivenUser("李四", "b@test.local");

        var result = await Handler().Handle(
            new SendFriendRequestCommand { RequesterId = a.Id, AccountId = b.Id }, default);

        Assert.Equal("好友申请已发送", result.Message);
        var e = _ctx.Events.SingleEvent<FriendRequestSent>();
        Assert.Equal(a.Id, e.RequesterId);
        Assert.Equal(b.Id, e.TargetId);
    }

    [Fact(DisplayName = "申请记录为待确认状态")]
    public async Task SendRequest_CreatesPendingFriendship()
    {
        var a = _ctx.GivenUser("张三", "a@test.local");
        var b = _ctx.GivenUser("李四", "b@test.local");

        await Handler().Handle(
            new SendFriendRequestCommand { RequesterId = a.Id, AccountId = b.Id }, default);

        var friendship = Assert.Single(_ctx.Friendships.All);
        Assert.Equal(FriendshipStatus.Pending, friendship.Status);
        Assert.Equal(a.Id, friendship.UserId);
        Assert.Equal(b.Id, friendship.FriendId);
    }

    [Fact(DisplayName = "目标用户不存在时抛出")]
    public async Task SendRequest_TargetMissing_Throws()
    {
        var a = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SendFriendRequestCommand { RequesterId = a.Id, AccountId = 99999 }, default));

        Assert.Equal("用户不存在", ex.Message);
    }

    [Theory(DisplayName = "账号ID非法时抛出")]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SendRequest_InvalidAccountId_Throws(int accountId)
    {
        var a = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SendFriendRequestCommand { RequesterId = a.Id, AccountId = accountId }, default));

        Assert.Equal("请输入正确的账号 ID", ex.Message);
    }

    [Fact(DisplayName = "重复申请时提示语来自关系状态")]
    public async Task SendRequest_Duplicate_MessageComesFromStatus()
    {
        var a = _ctx.GivenUser("张三", "a@test.local");
        var b = _ctx.GivenUser("李四", "b@test.local");
        var command = new SendFriendRequestCommand { RequesterId = a.Id, AccountId = b.Id };
        await Handler().Handle(command, default);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(command, default));

        Assert.Equal("已发送好友申请，等待对方确认", ex.Message);
        Assert.Single(_ctx.Friendships.All);
    }

    [Fact(DisplayName = "已是好友时提示已经是好友")]
    public async Task SendRequest_AlreadyFriends_ReportsFriendship()
    {
        var (a, b) = _ctx.GivenFriends();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SendFriendRequestCommand { RequesterId = a.Id, AccountId = b.Id }, default));

        Assert.Equal("你们已经是好友了", ex.Message);
    }

    [Fact(DisplayName = "反向已有申请时也算已存在关系")]
    public async Task SendRequest_ReverseRequest_CountsAsExisting()
    {
        var a = _ctx.GivenUser("张三", "a@test.local");
        var b = _ctx.GivenUser("李四", "b@test.local");
        await Handler().Handle(
            new SendFriendRequestCommand { RequesterId = b.Id, AccountId = a.Id }, default);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SendFriendRequestCommand { RequesterId = a.Id, AccountId = b.Id }, default));

        Assert.Equal("已发送好友申请，等待对方确认", ex.Message);
    }

    [Fact(DisplayName = "任一方向存在拉黑时被拒")]
    public async Task SendRequest_EitherDirectionBlocked_Rejected()
    {
        var a = _ctx.GivenUser("张三", "a@test.local");
        var b = _ctx.GivenUser("李四", "b@test.local");
        await _ctx.Blacklist.AddAsync(BlacklistEntry.Create(b.Id, a.Id, _ctx.Now));

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SendFriendRequestCommand { RequesterId = a.Id, AccountId = b.Id }, default));

        Assert.Equal("无法发送好友申请（你或对方已在黑名单中）", ex.Message);
    }

    [Fact(DisplayName = "黑名单校验先于关系校验_不泄露关系状态")]
    public async Task SendRequest_BlocklistCheckedFirst_NoRelationshipLeak()
    {
        var a = _ctx.GivenUser("张三", "a@test.local");
        var b = _ctx.GivenUser("李四", "b@test.local");
        await _ctx.Blacklist.AddAsync(BlacklistEntry.Create(a.Id, b.Id, _ctx.Now));

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SendFriendRequestCommand { RequesterId = a.Id, AccountId = b.Id }, default));

        Assert.Contains("黑名单", ex.Message, StringComparison.Ordinal);
    }
}

public class AcceptRejectFriendRequestTests
{
    private readonly ApplicationTestContext _ctx = new();

    private async Task<(int RequesterId, int TargetId, long RequestId)> GivenPendingAsync()
    {
        var a = _ctx.GivenUser("张三", "a@test.local");
        var b = _ctx.GivenUser("李四", "b@test.local");
        var friendship = Friendship.Request(a.Id, b.Id, _ctx.Now);
        await _ctx.Friendships.AddAsync(friendship);
        return (a.Id, b.Id, friendship.Id);
    }

    [Fact(DisplayName = "接受成功并双向通知")]
    public async Task Accept_Succeeds_NotifiesBothSides()
    {
        var (requesterId, targetId, requestId) = await GivenPendingAsync();
        _ctx.Events.Subscribe(new NotifyOnFriendRequestAccepted(_ctx.Notifier));

        var result = await new AcceptFriendRequestHandler(_ctx.Friendships, _ctx.Events, _ctx.Clock)
            .Handle(new AcceptFriendRequestCommand
            {
                RequestId = requestId, OperatorId = targetId
            }, default);

        Assert.Equal("已添加为好友", result.Message);

        // 双方都要收到，否则一方的好友列表不会刷新
        var pushes = _ctx.Notifier.OfKind(PushKind.FriendAccepted).ToList();
        Assert.Equal(2, pushes.Count);
        Assert.Contains(pushes, p => p.ToUserId == requesterId);
        Assert.Contains(pushes, p => p.ToUserId == targetId);
    }

    [Fact(DisplayName = "接受后状态变为已接受")]
    public async Task Accept_SetsStatusToAccepted()
    {
        var (_, targetId, requestId) = await GivenPendingAsync();

        await new AcceptFriendRequestHandler(_ctx.Friendships, _ctx.Events, _ctx.Clock)
            .Handle(new AcceptFriendRequestCommand
            {
                RequestId = requestId, OperatorId = targetId
            }, default);

        Assert.True((await _ctx.Friendships.FindByIdAsync(requestId))!.IsAccepted);
    }

    [Fact(DisplayName = "申请方不能自己接受")]
    public async Task Accept_ByRequester_IsForbidden()
    {
        var (requesterId, _, requestId) = await GivenPendingAsync();

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new AcceptFriendRequestHandler(_ctx.Friendships, _ctx.Events, _ctx.Clock)
                .Handle(new AcceptFriendRequestCommand
                {
                    RequestId = requestId, OperatorId = requesterId
                }, default));

        Assert.Equal("无权操作此申请", ex.Message);
    }

    [Fact(DisplayName = "申请不存在时抛出")]
    public async Task Accept_UnknownRequest_Throws()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => new AcceptFriendRequestHandler(_ctx.Friendships, _ctx.Events, _ctx.Clock)
                .Handle(new AcceptFriendRequestCommand
                {
                    RequestId = 99999, OperatorId = 10001
                }, default));
    }

    [Fact(DisplayName = "拒绝后删除申请记录并通知申请人")]
    public async Task Reject_RemovesRequest_NotifiesRequester()
    {
        var (requesterId, targetId, requestId) = await GivenPendingAsync();
        _ctx.Events.Subscribe(new NotifyOnFriendRequestRejected(_ctx.Notifier));

        var result = await new RejectFriendRequestHandler(_ctx.Friendships, _ctx.Events, _ctx.Clock)
            .Handle(new RejectFriendRequestCommand
            {
                RequestId = requestId, OperatorId = targetId
            }, default);

        Assert.Equal("已拒绝好友申请", result.Message);
        Assert.Empty(_ctx.Friendships.All);

        var push = Assert.Single(_ctx.Notifier.OfKind(PushKind.FriendRejected));
        Assert.Equal(requesterId, push.ToUserId);
    }

    [Fact(DisplayName = "非被申请方不能拒绝")]
    public async Task Reject_ByNonTarget_IsForbidden()
    {
        var (requesterId, _, requestId) = await GivenPendingAsync();

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new RejectFriendRequestHandler(_ctx.Friendships, _ctx.Events, _ctx.Clock)
                .Handle(new RejectFriendRequestCommand
                {
                    RequestId = requestId, OperatorId = requesterId
                }, default));

        Assert.Equal("无权操作此申请", ex.Message);
        Assert.Single(_ctx.Friendships.All);
    }
}

public class RemoveFriendTests
{
    private readonly ApplicationTestContext _ctx = new();

    [Fact(DisplayName = "删除好友成功")]
    public async Task Remove_Succeeds_DeletesFriendship()
    {
        var (a, b) = _ctx.GivenFriends();

        var result = await new RemoveFriendHandler(_ctx.Friendships, _ctx.Events, _ctx.Clock).Handle(
            new RemoveFriendCommand { UserId = a.Id, FriendId = b.Id }, default);

        Assert.Equal("已删除好友", result.Message);
        Assert.Empty(_ctx.Friendships.All);
    }

    [Fact(DisplayName = "反向也能删除")]
    public async Task Remove_ReverseDirection_Succeeds()
    {
        var (a, b) = _ctx.GivenFriends();

        await new RemoveFriendHandler(_ctx.Friendships, _ctx.Events, _ctx.Clock).Handle(
            new RemoveFriendCommand { UserId = b.Id, FriendId = a.Id }, default);

        Assert.Empty(_ctx.Friendships.All);
    }

    [Fact(DisplayName = "非好友时抛出")]
    public async Task Remove_NonFriend_Throws()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new RemoveFriendHandler(_ctx.Friendships, _ctx.Events, _ctx.Clock).Handle(
                new RemoveFriendCommand { UserId = 10001, FriendId = 10002 }, default));

        Assert.Equal("好友关系不存在", ex.Message);
    }

    [Fact(DisplayName = "待确认的申请不会被当作好友删除")]
    public async Task Remove_PendingRequest_NotTreatedAsFriend()
    {
        var a = _ctx.GivenUser("张三", "a@test.local");
        var b = _ctx.GivenUser("李四", "b@test.local");
        await _ctx.Friendships.AddAsync(Friendship.Request(a.Id, b.Id, _ctx.Now));

        await Assert.ThrowsAsync<DomainException>(
            () => new RemoveFriendHandler(_ctx.Friendships, _ctx.Events, _ctx.Clock).Handle(
                new RemoveFriendCommand { UserId = a.Id, FriendId = b.Id }, default));

        Assert.Single(_ctx.Friendships.All);
    }

    [Fact(DisplayName = "删除好友后双方都收到实时通知")]
    public async Task Remove_BothSidesReceiveRealtimePush()
    {
        var (a, b) = _ctx.GivenFriends();

        // FriendRemoved 此前定义了却从未被 Raise，于是被删的一方只能靠刷新页面才发现。
        // 这里把真实订阅方挂上，验证「事件 → 通知」整条链路。
        _ctx.Events.Subscribe(new NotifyOnFriendRemoved(_ctx.Notifier));

        await new RemoveFriendHandler(_ctx.Friendships, _ctx.Events, _ctx.Clock).Handle(
            new RemoveFriendCommand { UserId = a.Id, FriendId = b.Id }, default);

        var pushes = _ctx.Notifier.OfKind(PushKind.FriendRemoved).ToList();
        Assert.Equal(2, pushes.Count);
        Assert.Contains(pushes, p => p.ToUserId == b.Id && p.ContextId == a.Id);
        Assert.Contains(pushes, p => p.ToUserId == a.Id && p.ContextId == b.Id);
    }

    [Fact(DisplayName = "删除好友失败时不发通知")]
    public async Task Remove_Failure_SendsNoPush()
    {
        _ctx.Events.Subscribe(new NotifyOnFriendRemoved(_ctx.Notifier));

        await Assert.ThrowsAsync<DomainException>(
            () => new RemoveFriendHandler(_ctx.Friendships, _ctx.Events, _ctx.Clock).Handle(
                new RemoveFriendCommand { UserId = 10001, FriendId = 10002 }, default));

        Assert.Empty(_ctx.Notifier.OfKind(PushKind.FriendRemoved));
    }
}

public class FriendSettingUseCaseTests
{
    private readonly ApplicationTestContext _ctx = new();

    private FriendSettingWriter Writer()
        => new(_ctx.Friendships, _ctx.FriendSettings, _ctx.Clock);

    [Fact(DisplayName = "首次设置备注时创建记录")]
    public async Task SetRemark_FirstTime_CreatesRecord()
    {
        var (a, b) = _ctx.GivenFriends();

        var result = await new SetFriendRemarkHandler(Writer()).Handle(
            new SetFriendRemarkCommand { UserId = a.Id, FriendId = b.Id, Remark = "老王" }, default);

        Assert.Equal("备注已保存", result.Message);
        var setting = Assert.Single(_ctx.FriendSettings.All);
        Assert.Equal("老王", setting.Remark);
    }

    [Fact(DisplayName = "再次设置备注时复用同一条记录")]
    public async Task SetRemark_SecondTime_ReusesSameRecord()
    {
        var (a, b) = _ctx.GivenFriends();
        var command = new SetFriendRemarkCommand { UserId = a.Id, FriendId = b.Id, Remark = "老王" };
        await new SetFriendRemarkHandler(Writer()).Handle(command, default);

        command.Remark = "王总";
        await new SetFriendRemarkHandler(Writer()).Handle(command, default);

        var setting = Assert.Single(_ctx.FriendSettings.All);
        Assert.Equal("王总", setting.Remark);
    }

    [Fact(DisplayName = "清除备注时提示已清除")]
    public async Task SetRemark_Empty_ClearsAndReports()
    {
        var (a, b) = _ctx.GivenFriends();

        var result = await new SetFriendRemarkHandler(Writer()).Handle(
            new SetFriendRemarkCommand { UserId = a.Id, FriendId = b.Id, Remark = "" }, default);

        Assert.Equal("已清除备注", result.Message);
    }

    [Fact(DisplayName = "设置分类与备注互不影响")]
    public async Task SetCategory_DoesNotAffectRemark()
    {
        var (a, b) = _ctx.GivenFriends();
        await new SetFriendRemarkHandler(Writer()).Handle(
            new SetFriendRemarkCommand { UserId = a.Id, FriendId = b.Id, Remark = "老王" }, default);

        await new SetFriendCategoryHandler(Writer()).Handle(
            new SetFriendCategoryCommand { UserId = a.Id, FriendId = b.Id, Category = "同事" },
            default);

        var setting = Assert.Single(_ctx.FriendSettings.All);
        Assert.Equal("老王", setting.Remark);
        Assert.Equal("同事", setting.Category);
    }

    [Fact(DisplayName = "非好友不能设置备注")]
    public async Task SetRemark_NonFriend_IsForbidden()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new SetFriendRemarkHandler(Writer()).Handle(
                new SetFriendRemarkCommand { UserId = 10001, FriendId = 10002, Remark = "x" },
                default));

        Assert.Equal("好友关系不存在", ex.Message);
        Assert.Empty(_ctx.FriendSettings.All);
    }

    [Fact(DisplayName = "备注超长时抛出且不留下空记录")]
    public async Task SetRemark_TooLong_ThrowsWithoutRecord()
    {
        var (a, b) = _ctx.GivenFriends();

        await Assert.ThrowsAsync<DomainException>(
            () => new SetFriendRemarkHandler(Writer()).Handle(new SetFriendRemarkCommand
            {
                UserId = a.Id, FriendId = b.Id, Remark = new string('x', 51)
            }, default));

        Assert.Empty(_ctx.FriendSettings.All);
    }

    [Fact(DisplayName = "分类清除时提示已清除分类")]
    public async Task SetCategory_Blank_ReportsCleared()
    {
        var (a, b) = _ctx.GivenFriends();

        var result = await new SetFriendCategoryHandler(Writer()).Handle(
            new SetFriendCategoryCommand { UserId = a.Id, FriendId = b.Id, Category = "  " },
            default);

        Assert.Equal("已清除分类", result.Message);
    }
}

public class FriendQueryTests
{
    private readonly ApplicationTestContext _ctx = new();

    private GetFriendsHandler Handler()
        => new(_ctx.Friendships, _ctx.FriendSettings, _ctx.Users, _ctx.Presence);

    [Fact(DisplayName = "好友列表包含在线状态与备注")]
    public async Task GetFriends_IncludesPresenceAndRemark()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.Presence.SetOnline(b.Id);
        await _ctx.FriendSettings.AddAsync(
            SeedSetting(a.Id, b.Id, remark: "老王", category: "同事"));

        var result = await Handler().Handle(new GetFriendsQuery { UserId = a.Id }, default);

        var friend = Assert.Single(result.Data!);
        Assert.Equal(b.Id, friend.UserId);
        Assert.Equal("李四", friend.Nickname);
        Assert.True(friend.IsOnline);
        Assert.Equal("老王", friend.Remark);
        Assert.Equal("同事", friend.Category);
        Assert.Equal(1, friend.Status);
    }

    [Fact(DisplayName = "无好友时返回空列表")]
    public async Task GetFriends_NoFriends_ReturnsEmptyList()
    {
        var user = _ctx.GivenUser();

        var result = await Handler().Handle(new GetFriendsQuery { UserId = user.Id }, default);

        Assert.True(result.Success);
        Assert.Empty(result.Data!);
    }

    [Fact(DisplayName = "在线好友排在前面")]
    public async Task GetFriends_OnlineFriends_SortedFirst()
    {
        var me = _ctx.GivenUser("我", "me@test.local");
        var offline = _ctx.GivenUser("甲离线", "x@test.local");
        var online = _ctx.GivenUser("乙在线", "y@test.local");
        await _ctx.Friendships.AddAsync(Friendship.EstablishDirectly(me.Id, offline.Id, _ctx.Now));
        await _ctx.Friendships.AddAsync(Friendship.EstablishDirectly(me.Id, online.Id, _ctx.Now));
        _ctx.Presence.SetOnline(online.Id);

        var result = await Handler().Handle(new GetFriendsQuery { UserId = me.Id }, default);

        Assert.Equal(online.Id, result.Data![0].UserId);
        Assert.Equal(offline.Id, result.Data[1].UserId);
    }

    [Fact(DisplayName = "待确认的申请不出现在好友列表")]
    public async Task GetFriends_PendingRequest_Excluded()
    {
        var a = _ctx.GivenUser("张三", "a@test.local");
        var b = _ctx.GivenUser("李四", "b@test.local");
        await _ctx.Friendships.AddAsync(Friendship.Request(a.Id, b.Id, _ctx.Now));

        var result = await Handler().Handle(new GetFriendsQuery { UserId = a.Id }, default);

        Assert.Empty(result.Data!);
    }

    [Fact(DisplayName = "机器人好友被标记IsBot")]
    public async Task GetFriends_BotFriend_IsMarkedAsBot()
    {
        var owner = _ctx.GivenUser();
        var (_, botUser) = _ctx.GivenRobot(owner.Id);

        var result = await Handler().Handle(new GetFriendsQuery { UserId = owner.Id }, default);

        var friend = Assert.Single(result.Data!);
        Assert.Equal(botUser.Id, friend.UserId);
        Assert.True(friend.IsBot);
    }

    [Fact(DisplayName = "待处理申请列表带申请人信息")]
    public async Task GetPendingRequests_IncludesRequesterInfo()
    {
        var a = _ctx.GivenUser("张三", "a@test.local");
        var b = _ctx.GivenUser("李四", "b@test.local");
        await _ctx.Friendships.AddAsync(Friendship.Request(a.Id, b.Id, _ctx.Now));

        var result = await new GetPendingFriendRequestsHandler(_ctx.Friendships, _ctx.Users)
            .Handle(new GetPendingFriendRequestsQuery { UserId = b.Id }, default);

        var request = Assert.Single(result.Data!);
        Assert.Equal(a.Id, request.UserId);
        Assert.Equal("张三", request.Nickname);
    }

    [Fact(DisplayName = "自己发出的申请不出现在待处理列表")]
    public async Task GetPendingRequests_OwnRequest_Excluded()
    {
        var a = _ctx.GivenUser("张三", "a@test.local");
        var b = _ctx.GivenUser("李四", "b@test.local");
        await _ctx.Friendships.AddAsync(Friendship.Request(a.Id, b.Id, _ctx.Now));

        var result = await new GetPendingFriendRequestsHandler(_ctx.Friendships, _ctx.Users)
            .Handle(new GetPendingFriendRequestsQuery { UserId = a.Id }, default);

        Assert.Empty(result.Data!);
    }

    private static FriendSetting SeedSetting(int userId, int friendId, string remark, string category)
    {
        var setting = FriendSetting.CreateFor(userId, friendId, FakeClock.Default);
        setting.SetRemark(remark, FakeClock.Default);
        setting.SetCategory(category, FakeClock.Default);
        return setting;
    }
}

/// <summary>
/// 拉黑的两个副作用（解好友 + 推通知）由同一个事件的订阅方完成。
/// 改造前它们一个写在 Service、一个写在 Controller，被劈成两半。
/// </summary>
public class BlacklistUseCaseTests
{
    private readonly ApplicationTestContext _ctx = new();

    private BlockUserHandler Handler()
        => new(_ctx.Blacklist, _ctx.Users, _ctx.Events, _ctx.Clock);

    [Fact(DisplayName = "拉黑成功")]
    public async Task Block_Succeeds_AddsEntry()
    {
        var (a, b) = _ctx.GivenFriends();

        var result = await Handler().Handle(
            new BlockUserCommand { UserId = a.Id, BlockedUserId = b.Id }, default);

        Assert.Equal("已拉黑", result.Message);
        Assert.Single(_ctx.Blacklist.All);
    }

    [Fact(DisplayName = "拉黑后自动解除好友关系并通知对方")]
    public async Task Block_AlsoRemovesFriendship_NotifiesTarget()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.Events.Subscribe(new HandleUserBlocked(_ctx.Friendships, _ctx.Notifier));

        await Handler().Handle(new BlockUserCommand { UserId = a.Id, BlockedUserId = b.Id }, default);

        Assert.Empty(_ctx.Friendships.All);
        var push = Assert.Single(_ctx.Notifier.OfKind(PushKind.Blocked));
        Assert.Equal(b.Id, push.ToUserId);
        Assert.Equal("你已被对方拉黑", push.Content);
    }

    [Fact(DisplayName = "不能拉黑自己")]
    public async Task Block_Self_IsRejected()
    {
        var a = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new BlockUserCommand { UserId = a.Id, BlockedUserId = a.Id }, default));

        Assert.Equal("不能拉黑自己", ex.Message);
    }

    [Fact(DisplayName = "目标不存在时抛出")]
    public async Task Block_TargetMissing_Throws()
    {
        var a = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new BlockUserCommand { UserId = a.Id, BlockedUserId = 99999 }, default));

        Assert.Equal("用户不存在", ex.Message);
    }

    [Fact(DisplayName = "重复拉黑被拒")]
    public async Task Block_AlreadyBlocked_IsRejected()
    {
        var (a, b) = _ctx.GivenFriends();
        var command = new BlockUserCommand { UserId = a.Id, BlockedUserId = b.Id };
        await Handler().Handle(command, default);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(command, default));

        Assert.Equal("该用户已在黑名单中", ex.Message);
        Assert.Single(_ctx.Blacklist.All);
    }

    [Fact(DisplayName = "解除拉黑成功")]
    public async Task Unblock_Succeeds_RemovesEntry()
    {
        var (a, b) = _ctx.GivenFriends();
        await Handler().Handle(new BlockUserCommand { UserId = a.Id, BlockedUserId = b.Id }, default);

        var result = await new UnblockUserHandler(_ctx.Blacklist).Handle(
            new UnblockUserCommand { UserId = a.Id, BlockedUserId = b.Id }, default);

        Assert.Equal("已解除拉黑", result.Message);
        Assert.Empty(_ctx.Blacklist.All);
    }

    [Fact(DisplayName = "解除不在黑名单的用户时抛出")]
    public async Task Unblock_NotBlocked_Throws()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new UnblockUserHandler(_ctx.Blacklist).Handle(
                new UnblockUserCommand { UserId = 10001, BlockedUserId = 10002 }, default));

        Assert.Equal("该用户不在你的黑名单中", ex.Message);
    }

    [Fact(DisplayName = "黑名单列表带用户信息与拉黑时间")]
    public async Task GetBlacklist_IncludesUserInfoAndBlockedAt()
    {
        var (a, b) = _ctx.GivenFriends();
        await Handler().Handle(new BlockUserCommand { UserId = a.Id, BlockedUserId = b.Id }, default);

        var result = await new GetBlacklistHandler(_ctx.Blacklist, _ctx.Users).Handle(
            new GetBlacklistQuery { UserId = a.Id }, default);

        var entry = Assert.Single(result.Data!);
        Assert.Equal(b.Id, entry.UserId);
        Assert.Equal("李四", entry.Nickname);
        Assert.Equal(_ctx.Now, entry.BlockedAt);
    }

    [Fact(DisplayName = "黑名单为空时返回空列表")]
    public async Task GetBlacklist_Empty_ReturnsEmptyList()
    {
        var result = await new GetBlacklistHandler(_ctx.Blacklist, _ctx.Users).Handle(
            new GetBlacklistQuery { UserId = 10001 }, default);

        Assert.True(result.Success);
        Assert.Empty(result.Data!);
    }
}
