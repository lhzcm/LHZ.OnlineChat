using LHZ.OnlineChat.Application.Messaging.Commands;
using LHZ.OnlineChat.Application.Tests.TestDoubles;
using LHZ.OnlineChat.Domain.Blacklists;
using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Application.Tests.Messaging;

public class SendPrivateMessageTests
{
    private readonly ApplicationTestContext _ctx = new();

    private SendPrivateMessageHandler Handler()
        => new(_ctx.PrivateMessages, _ctx.Blacklist, _ctx.Friendships, _ctx.Users,
            _ctx.Cache, _ctx.Notifier, _ctx.Events, _ctx.Clock);

    private static SendPrivateMessageCommand Command(int senderId, int receiverId, string content = "你好")
        => new()
        {
            SenderId = senderId, ReceiverId = receiverId, Content = content,
            Kind = MessageKind.Text, ClientMessageId = "cmid-1"
        };

    [Fact]
    public async Task 发送成功并落库()
    {
        var (a, b) = _ctx.GivenFriends();

        var result = await Handler().Handle(Command(a.Id, b.Id), default);

        Assert.True(result.Delivered);
        var message = Assert.Single(_ctx.PrivateMessages.All);
        Assert.Equal("你好", message.Content);
        Assert.Equal(a.Id, message.SenderId);
        Assert.Equal(b.Id, message.ReceiverId);
        Assert.False(message.IsRead);
    }

    [Fact]
    public async Task 保留客户端消息ID用于乐观发送去重()
    {
        var (a, b) = _ctx.GivenFriends();

        await Handler().Handle(Command(a.Id, b.Id), default);

        Assert.Equal("cmid-1", Assert.Single(_ctx.PrivateMessages.All).ClientMessageId);
    }

    [Fact]
    public async Task 未带客户端ID时对外标识回落数据库ID()
    {
        var (a, b) = _ctx.GivenFriends();
        var command = Command(a.Id, b.Id);
        command.ClientMessageId = null;

        await Handler().Handle(command, default);

        var message = Assert.Single(_ctx.PrivateMessages.All);
        Assert.Equal(message.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            message.PublicMessageId);
    }

    [Fact]
    public async Task 同一客户端消息ID重复提交只落一行_幂等()
    {
        var (a, b) = _ctx.GivenFriends();

        var first = await Handler().Handle(Command(a.Id, b.Id), default);
        var second = await Handler().Handle(Command(a.Id, b.Id), default);

        // 重试(网络抖动、用户点重试)不该插入第二条 —— 前端按 messageId 去重
        // 只能掩盖表现层的重复,刷新一次就会现形
        Assert.True(first.Delivered);
        Assert.True(second.Delivered);
        Assert.Single(_ctx.PrivateMessages.All);
    }

    [Fact]
    public async Task 非好友发送被拒_不落库不广播()
    {
        var a = _ctx.GivenUser("张三", "a@test.local");
        var b = _ctx.GivenUser("李四", "b@test.local");

        var result = await Handler().Handle(Command(a.Id, b.Id), default);

        Assert.False(result.Delivered);
        Assert.Equal("你们还不是好友，无法发送私聊消息", result.RejectionReason);
        Assert.Empty(_ctx.PrivateMessages.All);
        Assert.Empty(_ctx.Notifier.OfKind(PushKind.PrivateMessage));
        Assert.Empty(_ctx.Events.Dispatched);
        // 只回执发送者：不能因为一次越权尝试去打扰接收者
        Assert.Equal(a.Id, Assert.Single(_ctx.Notifier.OfKind(PushKind.MessageBlocked)).ToUserId);
    }

    [Fact]
    public async Task 客户端消息ID相同但发送者不同时各自落库()
    {
        var a = _ctx.GivenUser("张三", "a@test.local");
        var b = _ctx.GivenUser("李四", "b@test.local");
        var c = _ctx.GivenUser("王五", "c@test.local");
        await _ctx.Friendships.AddAsync(
            Domain.Friends.Friendship.EstablishDirectly(a.Id, b.Id, _ctx.Now));
        await _ctx.Friendships.AddAsync(
            Domain.Friends.Friendship.EstablishDirectly(c.Id, b.Id, _ctx.Now));

        // 唯一索引是 (SenderId, ClientMessageId):不同发送者用同一个客户端 ID 互不影响
        await Handler().Handle(Command(a.Id, b.Id), default);
        await Handler().Handle(Command(c.Id, b.Id), default);

        Assert.Equal(2, _ctx.PrivateMessages.All.Count);
    }

    [Fact]
    public async Task 推给接收方并回显给发送方_多端同步()
    {
        var (a, b) = _ctx.GivenFriends();

        await Handler().Handle(Command(a.Id, b.Id), default);

        var toReceiver = Assert.Single(_ctx.Notifier.OfKind(PushKind.PrivateMessage));
        Assert.Equal(b.Id, toReceiver.ToUserId);

        var echo = Assert.Single(_ctx.Notifier.OfKind(PushKind.PrivateMessageEcho));
        Assert.Equal(a.Id, echo.ToUserId);
    }

    [Fact]
    public async Task 推送载荷携带发送者昵称与头像()
    {
        var (a, b) = _ctx.GivenFriends();

        await Handler().Handle(Command(a.Id, b.Id), default);

        var push = Assert.Single(_ctx.Notifier.OfKind(PushKind.PrivateMessage));
        Assert.Equal("张三", push.Message!.SenderName);
        Assert.Equal(ChatSessionType.Private, push.Message.SessionType);
    }

    [Fact]
    public async Task 写入最近消息缓存_键按双向归一化()
    {
        var (a, b) = _ctx.GivenFriends();

        await Handler().Handle(Command(a.Id, b.Id), default);

        var appended = Assert.Single(_ctx.Cache.Appended);
        Assert.Equal(ChatSessionType.Private, appended.Type);

        // 双向必须命中同一个缓存键，否则两人看到的历史不一致
        var fromA = await _ctx.Cache.GetPrivateAsync(a.Id, b.Id);
        var fromB = await _ctx.Cache.GetPrivateAsync(b.Id, a.Id);
        Assert.Single(fromA);
        Assert.Single(fromB);
    }

    [Fact]
    public async Task 发出消息已发送事件_供机器人等订阅方使用()
    {
        var (a, b) = _ctx.GivenFriends();

        await Handler().Handle(Command(a.Id, b.Id), default);

        var e = _ctx.Events.SingleEvent<PrivateMessageSent>();
        Assert.Equal(a.Id, e.SenderId);
        Assert.Equal(b.Id, e.ReceiverId);
        Assert.NotEqual(0, e.MessageId);
    }

    [Fact]
    public async Task 被对方拉黑时拒绝发送_不落库不广播()
    {
        var (a, b) = _ctx.GivenFriends();
        await _ctx.Blacklist.AddAsync(BlacklistEntry.Create(b.Id, a.Id, _ctx.Now));

        var result = await Handler().Handle(Command(a.Id, b.Id), default);

        Assert.False(result.Delivered);
        Assert.Equal("对方已将你拉黑，消息未发送", result.RejectionReason);
        Assert.Empty(_ctx.PrivateMessages.All);
        Assert.Empty(_ctx.Notifier.OfKind(PushKind.PrivateMessage));
        Assert.Empty(_ctx.Events.Dispatched);
    }

    [Fact]
    public async Task 被拉黑时只回执发送者()
    {
        var (a, b) = _ctx.GivenFriends();
        await _ctx.Blacklist.AddAsync(BlacklistEntry.Create(b.Id, a.Id, _ctx.Now));

        await Handler().Handle(Command(a.Id, b.Id), default);

        var push = Assert.Single(_ctx.Notifier.OfKind(PushKind.MessageBlocked));
        Assert.Equal(a.Id, push.ToUserId);
    }

    [Fact]
    public async Task 自己拉黑对方时仍可发送()
    {
        // 拉黑是单向拦截：我拉黑你，是你不能发给我，我仍可发给你
        var (a, b) = _ctx.GivenFriends();
        await _ctx.Blacklist.AddAsync(BlacklistEntry.Create(a.Id, b.Id, _ctx.Now));

        var result = await Handler().Handle(Command(a.Id, b.Id), default);

        Assert.True(result.Delivered);
    }

    [Fact]
    public async Task 带引用回复时拆列存储()
    {
        var (a, b) = _ctx.GivenFriends();
        var command = Command(a.Id, b.Id);
        command.ReplyToMessageId = "origin-1";
        command.ReplyPreview = "原文";
        command.ReplySenderName = "李四";

        await Handler().Handle(command, default);

        var message = Assert.Single(_ctx.PrivateMessages.All);
        Assert.Equal("origin-1", message.ReplyMessageId);
        Assert.Equal("原文", message.ReplyContent);
        Assert.Equal("李四", message.ReplySenderName);
    }

    [Theory]
    [InlineData(MessageKind.Text)]
    [InlineData(MessageKind.Image)]
    [InlineData(MessageKind.File)]
    public async Task 支持全部消息类型(MessageKind kind)
    {
        var (a, b) = _ctx.GivenFriends();
        var command = Command(a.Id, b.Id);
        command.Kind = kind;

        await Handler().Handle(command, default);

        Assert.Equal(kind, Assert.Single(_ctx.PrivateMessages.All).Kind);
    }
}

public class SendGroupMessageTests
{
    private readonly ApplicationTestContext _ctx = new();

    private SendGroupMessageHandler Handler()
        => new(_ctx.GroupMessages, _ctx.GroupMembers, _ctx.Users, _ctx.Cache,
            _ctx.Notifier, _ctx.Events, _ctx.MuteFormatter, _ctx.Clock);

    [Fact]
    public async Task 群成员发送成功并广播给所有成员()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        var result = await Handler().Handle(new SendGroupMessageCommand
        {
            SenderId = owner.Id, GroupId = group.Id, Content = "大家好"
        }, default);

        Assert.True(result.Delivered);
        var pushes = _ctx.Notifier.OfKind(PushKind.GroupMessage).ToList();
        Assert.Equal(2, pushes.Count);
        Assert.Contains(pushes, p => p.ToUserId == owner.Id);
        Assert.Contains(pushes, p => p.ToUserId == member.Id);
    }

    [Fact]
    public async Task 非群成员静默丢弃_不落库()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var result = await Handler().Handle(new SendGroupMessageCommand
        {
            SenderId = 99999, GroupId = group.Id, Content = "偷偷发言"
        }, default);

        Assert.False(result.Delivered);
        Assert.Equal("你不是该群组成员", result.RejectionReason);
        Assert.Empty(_ctx.GroupMessages.All);
    }

    [Fact]
    public async Task 同一客户端消息ID重复提交只落一行_幂等()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        SendGroupMessageCommand Command() => new()
        {
            SenderId = owner.Id,
            GroupId = group.Id,
            Content = "重试的消息",
            ClientMessageId = "cmid-group-1"
        };

        var first = await Handler().Handle(Command(), default);
        var second = await Handler().Handle(Command(), default);

        Assert.True(first.Delivered);
        Assert.True(second.Delivered);
        Assert.Single(_ctx.GroupMessages.All);
    }

    [Fact]
    public async Task 提及列表被持久化并可解析回读()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        await Handler().Handle(new SendGroupMessageCommand
        {
            SenderId = owner.Id,
            GroupId = group.Id,
            Content = $"@成员 看一下",
            Mentions = new List<int> { member.Id }
        }, default);

        var message = Assert.Single(_ctx.GroupMessages.All);
        Assert.Equal(new[] { member.Id }, message.MentionedUsers.UserIds);
    }

    [Fact]
    public async Task 提及列表去重与剔除非法ID()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        await Handler().Handle(new SendGroupMessageCommand
        {
            SenderId = owner.Id,
            GroupId = group.Id,
            Content = "x",
            Mentions = new List<int> { 10002, 10002, 0, -1 }
        }, default);

        Assert.Equal(new[] { 10002 }, Assert.Single(_ctx.GroupMessages.All).MentionedUsers.UserIds);
    }

    [Fact]
    public async Task 禁言期间发送被拒并收到带截止时间的提示()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);
        var memberEntity = await _ctx.GroupMembers.FindAsync(group.Id, member.Id);
        var until = _ctx.Now.AddHours(2);
        memberEntity!.SetMute(until, _ctx.Now);
        await _ctx.GroupMembers.UpdateAsync(memberEntity);

        var result = await Handler().Handle(new SendGroupMessageCommand
        {
            SenderId = member.Id, GroupId = group.Id, Content = "我还能说话吗"
        }, default);

        Assert.False(result.Delivered);
        Assert.Equal(until, result.MutedUntil);
        Assert.Contains("禁言至", result.RejectionReason!, StringComparison.Ordinal);
        Assert.Empty(_ctx.GroupMessages.All);

        var push = Assert.Single(_ctx.Notifier.OfKind(PushKind.Muted));
        Assert.Equal(member.Id, push.ToUserId);
    }

    [Fact]
    public async Task 禁言到期后可正常发送()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);
        var memberEntity = await _ctx.GroupMembers.FindAsync(group.Id, member.Id);
        memberEntity!.SetMute(_ctx.Now.AddMinutes(30), _ctx.Now);
        await _ctx.GroupMembers.UpdateAsync(memberEntity);

        _ctx.Clock.Advance(TimeSpan.FromMinutes(31));

        var result = await Handler().Handle(new SendGroupMessageCommand
        {
            SenderId = member.Id, GroupId = group.Id, Content = "解禁了"
        }, default);

        Assert.True(result.Delivered);
    }

    [Fact]
    public async Task 群主不受禁言影响()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var result = await Handler().Handle(new SendGroupMessageCommand
        {
            SenderId = owner.Id, GroupId = group.Id, Content = "群主发言"
        }, default);

        Assert.True(result.Delivered);
    }

    [Fact]
    public async Task 写入群缓存并发出事件()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        await Handler().Handle(new SendGroupMessageCommand
        {
            SenderId = owner.Id, GroupId = group.Id, Content = "x"
        }, default);

        var appended = Assert.Single(_ctx.Cache.Appended);
        Assert.Equal(ChatSessionType.Group, appended.Type);
        Assert.Equal(group.Id, appended.Left);

        var e = _ctx.Events.SingleEvent<GroupMessageSent>();
        Assert.Equal(group.Id, e.GroupId);
    }
}

public class RecallMessageTests
{
    private readonly ApplicationTestContext _ctx = new();

    private RecallMessageHandler Handler()
        => new(_ctx.PrivateMessages, _ctx.GroupMessages, _ctx.Events, _ctx.Clock);

    [Fact]
    public async Task 本人在时间窗内可撤回私聊消息()
    {
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id, sentAt: _ctx.Now.AddMinutes(-1));

        var recalled = await Handler().Handle(new RecallMessageCommand
        {
            OperatorId = a.Id, TargetId = b.Id, MessageId = message.PublicMessageId
        }, default);

        Assert.True(recalled);
        Assert.True((await _ctx.PrivateMessages.FindByIdAsync(message.Id))!.IsDeleted);
    }

    [Fact]
    public async Task 撤回后发出事件并携带会话上下文()
    {
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id, sentAt: _ctx.Now.AddMinutes(-1));

        await Handler().Handle(new RecallMessageCommand
        {
            OperatorId = a.Id, TargetId = b.Id, MessageId = message.PublicMessageId
        }, default);

        var e = _ctx.Events.SingleEvent<MessageRecalled>();
        Assert.Equal(ChatSessionType.Private, e.SessionType);
        Assert.Equal(a.Id, e.SenderId);
        Assert.Equal(b.Id, e.PeerUserId);
        Assert.Null(e.GroupId);
        Assert.False(e.ByAdmin);
    }

    [Fact]
    public async Task 超过时间窗无法撤回()
    {
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id, sentAt: _ctx.Now.AddMinutes(-3));

        var recalled = await Handler().Handle(new RecallMessageCommand
        {
            OperatorId = a.Id, TargetId = b.Id, MessageId = message.PublicMessageId
        }, default);

        Assert.False(recalled);
        Assert.False((await _ctx.PrivateMessages.FindByIdAsync(message.Id))!.IsDeleted);
    }

    [Fact]
    public async Task 非本人无法撤回()
    {
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id, sentAt: _ctx.Now.AddMinutes(-1));

        var recalled = await Handler().Handle(new RecallMessageCommand
        {
            OperatorId = b.Id, TargetId = a.Id, MessageId = message.PublicMessageId
        }, default);

        Assert.False(recalled);
        Assert.False((await _ctx.PrivateMessages.FindByIdAsync(message.Id))!.IsDeleted);
    }

    [Fact]
    public async Task 群消息撤回_先按私聊找不到再按群找()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);
        var message = _ctx.GivenGroupMessage(group.Id, owner.Id, sentAt: _ctx.Now.AddMinutes(-1));

        var recalled = await Handler().Handle(new RecallMessageCommand
        {
            OperatorId = owner.Id, TargetId = group.Id, MessageId = message.PublicMessageId
        }, default);

        Assert.True(recalled);
        var e = _ctx.Events.SingleEvent<MessageRecalled>();
        Assert.Equal(ChatSessionType.Group, e.SessionType);
        Assert.Equal(group.Id, e.GroupId);
    }

    [Fact]
    public async Task 消息标识不存在时返回false()
    {
        var (a, b) = _ctx.GivenFriends();

        var recalled = await Handler().Handle(new RecallMessageCommand
        {
            OperatorId = a.Id, TargetId = b.Id, MessageId = "not-exist"
        }, default);

        Assert.False(recalled);
        Assert.Empty(_ctx.Events.Dispatched);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task 消息标识为空时直接返回false(string messageId)
    {
        var recalled = await Handler().Handle(new RecallMessageCommand
        {
            OperatorId = 10001, TargetId = 10002, MessageId = messageId
        }, default);

        Assert.False(recalled);
    }

    [Fact]
    public async Task 撤回订阅方清缓存并广播给双方()
    {
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id, sentAt: _ctx.Now.AddMinutes(-1));
        _ctx.Events.Subscribe(new HandleMessageRecalled(
            _ctx.Cache, _ctx.Notifier, _ctx.GroupMembers));

        await Handler().Handle(new RecallMessageCommand
        {
            OperatorId = a.Id, TargetId = b.Id, MessageId = message.PublicMessageId
        }, default);

        Assert.Single(_ctx.Cache.Removed);
        var pushes = _ctx.Notifier.OfKind(PushKind.MessageRecalled).ToList();
        Assert.Equal(2, pushes.Count);
        Assert.Contains(pushes, p => p.ToUserId == a.Id);
        Assert.Contains(pushes, p => p.ToUserId == b.Id);
    }

    [Fact]
    public async Task 群消息撤回广播给全部成员()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);
        var message = _ctx.GivenGroupMessage(group.Id, owner.Id, sentAt: _ctx.Now.AddMinutes(-1));
        _ctx.Events.Subscribe(new HandleMessageRecalled(
            _ctx.Cache, _ctx.Notifier, _ctx.GroupMembers));

        await Handler().Handle(new RecallMessageCommand
        {
            OperatorId = owner.Id, TargetId = group.Id, MessageId = message.PublicMessageId
        }, default);

        Assert.Equal(2, _ctx.Notifier.OfKind(PushKind.MessageRecalled).Count());
    }
}

public class PresenceAndBacklogTests
{
    private readonly ApplicationTestContext _ctx = new();

    [Fact]
    public async Task 上线广播给全部好友()
    {
        var me = _ctx.GivenUser("我", "me@test.local");
        var f1 = _ctx.GivenUser("甲", "a@test.local");
        var f2 = _ctx.GivenUser("乙", "b@test.local");
        await _ctx.Friendships.AddAsync(
            Domain.Friends.Friendship.EstablishDirectly(me.Id, f1.Id, _ctx.Now));
        await _ctx.Friendships.AddAsync(
            Domain.Friends.Friendship.EstablishDirectly(me.Id, f2.Id, _ctx.Now));

        await new BroadcastPresenceHandler(_ctx.Friendships, _ctx.Notifier).Handle(
            new BroadcastPresenceCommand { UserId = me.Id, Online = true }, default);

        var pushes = _ctx.Notifier.OfKind(PushKind.Presence).ToList();
        Assert.Equal(2, pushes.Count);
        Assert.All(pushes, p => Assert.Equal("online", p.Content));
    }

    [Fact]
    public async Task 下线广播内容为offline()
    {
        var (me, _) = _ctx.GivenFriends();

        await new BroadcastPresenceHandler(_ctx.Friendships, _ctx.Notifier).Handle(
            new BroadcastPresenceCommand { UserId = me.Id, Online = false }, default);

        Assert.Equal("offline", Assert.Single(_ctx.Notifier.OfKind(PushKind.Presence)).Content);
    }

    [Fact]
    public async Task 无好友时不推送()
    {
        var me = _ctx.GivenUser();

        await new BroadcastPresenceHandler(_ctx.Friendships, _ctx.Notifier).Handle(
            new BroadcastPresenceCommand { UserId = me.Id, Online = true }, default);

        Assert.Empty(_ctx.Notifier.Pushes);
    }

    [Fact]
    public async Task 补发游标之后的群消息()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        var read = _ctx.GivenGroupMessage(group.Id, owner.Id, "已读消息");
        var memberEntity = await _ctx.GroupMembers.FindAsync(group.Id, member.Id);
        memberEntity!.AdvanceReadCursor(read.Id);
        await _ctx.GroupMembers.UpdateAsync(memberEntity);

        _ctx.GivenGroupMessage(group.Id, owner.Id, "未读消息1");
        _ctx.GivenGroupMessage(group.Id, owner.Id, "未读消息2");

        await new SendGroupBacklogHandler(
                _ctx.GroupMessages, _ctx.Users, _ctx.Notifier)
            .Handle(new SendGroupBacklogCommand { UserId = member.Id }, default);

        var pushes = _ctx.Notifier.OfKind(PushKind.GroupBacklog).ToList();
        Assert.Equal(2, pushes.Count);
        Assert.All(pushes, p => Assert.Equal(member.Id, p.ToUserId));
        Assert.DoesNotContain(pushes, p => p.Content == "已读消息");
    }

    [Fact]
    public async Task 已撤回的消息不补发()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);
        var message = _ctx.GivenGroupMessage(group.Id, owner.Id, "已撤回");
        message.ForceDelete();
        await _ctx.GroupMessages.UpdateAsync(message);

        await new SendGroupBacklogHandler(
                _ctx.GroupMessages, _ctx.Users, _ctx.Notifier)
            .Handle(new SendGroupBacklogCommand { UserId = member.Id }, default);

        Assert.Empty(_ctx.Notifier.OfKind(PushKind.GroupBacklog));
    }

    [Fact]
    public async Task 每群补发上限为100条_防游标异常刷屏()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);
        for (var i = 0; i < 150; i++) _ctx.GivenGroupMessage(group.Id, owner.Id, $"消息{i}");

        await new SendGroupBacklogHandler(
                _ctx.GroupMessages, _ctx.Users, _ctx.Notifier)
            .Handle(new SendGroupBacklogCommand { UserId = member.Id }, default);

        Assert.Equal(
            SendGroupBacklogCommand.PerGroupLimit,
            _ctx.Notifier.OfKind(PushKind.GroupBacklog).Count());
    }

    [Fact]
    public async Task 未加入任何群时不补发()
    {
        var user = _ctx.GivenUser();

        await new SendGroupBacklogHandler(
                _ctx.GroupMessages, _ctx.Users, _ctx.Notifier)
            .Handle(new SendGroupBacklogCommand { UserId = user.Id }, default);

        Assert.Empty(_ctx.Notifier.Pushes);
    }
}
