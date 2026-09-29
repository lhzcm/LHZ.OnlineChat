using LHZ.OnlineChat.Application.Messaging.Commands;
using LHZ.OnlineChat.Application.Messaging.Queries;
using LHZ.OnlineChat.Application.Tests.TestDoubles;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Application.Tests.Messaging;

public class PrivateHistoryTests
{
    private readonly ApplicationTestContext _ctx = new();

    private GetPrivateHistoryHandler Handler()
        => new(_ctx.PrivateMessages, _ctx.Friendships, _ctx.Users, _ctx.Cache);

    [Fact]
    public async Task 历史按时间正序返回()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(a.Id, b.Id, "第一条", _ctx.Now.AddMinutes(-2));
        _ctx.GivenPrivateMessage(b.Id, a.Id, "第二条", _ctx.Now.AddMinutes(-1));

        var result = await Handler().Handle(
            new GetPrivateHistoryQuery { UserId = a.Id, FriendId = b.Id }, default);

        Assert.Equal(2, result.Data!.Items.Count);
        Assert.Equal("第一条", result.Data.Items[0].Content);
        Assert.Equal("第二条", result.Data.Items[1].Content);
    }

    [Fact]
    public async Task 历史携带发送者信息()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(a.Id, b.Id, "你好");

        var result = await Handler().Handle(
            new GetPrivateHistoryQuery { UserId = b.Id, FriendId = a.Id }, default);

        Assert.Equal("张三", Assert.Single(result.Data!.Items).SenderName);
    }

    [Fact]
    public async Task 非好友读历史被拒()
    {
        var a = _ctx.GivenUser("张三", "a@test.local");
        var b = _ctx.GivenUser("李四", "b@test.local");

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new GetPrivateHistoryQuery { UserId = a.Id, FriendId = b.Id }, default));

        Assert.Equal("不是好友关系", ex.Message);
    }

    [Fact]
    public async Task 分页返回正确总数()
    {
        var (a, b) = _ctx.GivenFriends();
        for (var i = 0; i < 5; i++)
            _ctx.GivenPrivateMessage(a.Id, b.Id, $"消息{i}", _ctx.Now.AddMinutes(-i));

        var result = await Handler().Handle(new GetPrivateHistoryQuery
        {
            UserId = a.Id, FriendId = b.Id, Page = 1, PageSize = 2
        }, default);

        Assert.Equal(2, result.Data!.Items.Count);
        Assert.Equal(5, result.Data.Total);
        Assert.Equal(1, result.Data.Page);
        Assert.Equal(2, result.Data.PageSize);
    }

    [Fact]
    public async Task 只返回双方之间的消息()
    {
        var (a, b) = _ctx.GivenFriends();
        var c = _ctx.GivenUser("王五", "c@test.local");
        _ctx.GivenPrivateMessage(a.Id, b.Id, "给乙");
        _ctx.GivenPrivateMessage(a.Id, c.Id, "给丙");

        var result = await Handler().Handle(
            new GetPrivateHistoryQuery { UserId = a.Id, FriendId = b.Id }, default);

        Assert.Equal("给乙", Assert.Single(result.Data!.Items).Content);
    }

    [Fact]
    public async Task 页长上限被夹紧()
    {
        var (a, b) = _ctx.GivenFriends();

        var result = await Handler().Handle(new GetPrivateHistoryQuery
        {
            UserId = a.Id, FriendId = b.Id, PageSize = 9999
        }, default);

        Assert.Equal(100, result.Data!.PageSize);
    }
}

public class GroupHistoryTests
{
    private readonly ApplicationTestContext _ctx = new();

    private GetGroupHistoryHandler Handler()
        => new(_ctx.GroupMessages, _ctx.GroupMembers, _ctx.Users);

    [Fact]
    public async Task 群成员可读历史()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);
        _ctx.GivenGroupMessage(group.Id, owner.Id, "群消息");

        var result = await Handler().Handle(
            new GetGroupHistoryQuery { GroupId = group.Id, UserId = owner.Id }, default);

        Assert.Equal("群消息", Assert.Single(result.Data!.Items).Content);
    }

    [Fact]
    public async Task 非群成员被拒()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new GetGroupHistoryQuery { GroupId = group.Id, UserId = 99999 }, default));

        Assert.Equal("你不是该群成员", ex.Message);
    }

    [Fact]
    public async Task 历史携带提及列表()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        await new SendGroupMessageHandler(
                _ctx.GroupMessages, _ctx.GroupMembers, _ctx.Users, _ctx.Cache,
                _ctx.Notifier, _ctx.Events, _ctx.MuteFormatter, _ctx.Clock)
            .Handle(new SendGroupMessageCommand
            {
                SenderId = owner.Id,
                GroupId = group.Id,
                Content = "@成员",
                Mentions = new List<int> { member.Id }
            }, default);

        var result = await Handler().Handle(
            new GetGroupHistoryQuery { GroupId = group.Id, UserId = member.Id }, default);

        Assert.Equal(new[] { member.Id }, Assert.Single(result.Data!.Items).Mentions);
    }
}

public class UnreadAndOfflineTests
{
    private readonly ApplicationTestContext _ctx = new();

    [Fact]
    public async Task 未读数只统计发给我的未读()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(a.Id, b.Id, "给乙1");
        _ctx.GivenPrivateMessage(a.Id, b.Id, "给乙2");
        _ctx.GivenPrivateMessage(b.Id, a.Id, "给甲");

        var result = await new GetUnreadCountHandler(_ctx.PrivateMessages).Handle(
            new GetUnreadCountQuery { UserId = b.Id }, default);

        Assert.Equal(2, result.Data!.PrivateUnread);
    }

    [Fact]
    public async Task 离线消息按时间正序()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(a.Id, b.Id, "早", _ctx.Now.AddMinutes(-5));
        _ctx.GivenPrivateMessage(a.Id, b.Id, "晚", _ctx.Now.AddMinutes(-1));

        var result = await new GetOfflineMessagesHandler(_ctx.PrivateMessages, _ctx.Users).Handle(
            new GetOfflineMessagesQuery { UserId = b.Id }, default);

        Assert.Equal(2, result.Data!.Count);
        Assert.Equal("早", result.Data[0].Content);
    }

    [Fact]
    public async Task 已读消息不算离线消息()
    {
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id, "已读");
        message.MarkAsRead(b.Id);
        await _ctx.PrivateMessages.UpdateAsync(message);

        var result = await new GetOfflineMessagesHandler(_ctx.PrivateMessages, _ctx.Users).Handle(
            new GetOfflineMessagesQuery { UserId = b.Id }, default);

        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task 标记单条已读()
    {
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id);

        var result = await new MarkMessageReadHandler(_ctx.PrivateMessages).Handle(
            new MarkMessageReadCommand { MessageId = message.Id, UserId = b.Id }, default);

        Assert.Equal("已标记已读", result.Message);
        Assert.True((await _ctx.PrivateMessages.FindByIdAsync(message.Id))!.IsRead);
    }

    [Fact]
    public async Task 非接收方不能标记已读()
    {
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new MarkMessageReadHandler(_ctx.PrivateMessages).Handle(
                new MarkMessageReadCommand { MessageId = message.Id, UserId = a.Id }, default));

        Assert.Equal("消息不存在或无权操作", ex.Message);
    }

    [Fact]
    public async Task 消息不存在时抛出()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => new MarkMessageReadHandler(_ctx.PrivateMessages).Handle(
                new MarkMessageReadCommand { MessageId = 99999, UserId = 10001 }, default));
    }

    [Fact]
    public async Task 批量标记某人发来的消息已读()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(a.Id, b.Id, "1");
        _ctx.GivenPrivateMessage(a.Id, b.Id, "2");

        var result = await new MarkConversationReadHandler(_ctx.PrivateMessages).Handle(
            new MarkConversationReadCommand { SenderId = a.Id, UserId = b.Id }, default);

        Assert.Equal("已全部标记已读", result.Message);
        Assert.Equal(0, await _ctx.PrivateMessages.CountUnreadForAsync(b.Id));
    }

    [Fact]
    public async Task 标记群已读推进游标到最新()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);
        _ctx.GivenGroupMessage(group.Id, owner.Id, "1");
        var latest = _ctx.GivenGroupMessage(group.Id, owner.Id, "2");

        var result = await new MarkGroupReadHandler(_ctx.GroupMembers, _ctx.GroupMessages).Handle(
            new MarkGroupReadCommand { GroupId = group.Id, UserId = member.Id }, default);

        Assert.Equal("已标记群消息已读", result.Message);
        Assert.Equal(latest.Id, (await _ctx.GroupMembers.FindAsync(group.Id, member.Id))!.LastReadMessageId);
    }

    [Fact]
    public async Task 非群成员不能标记群已读()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => new MarkGroupReadHandler(_ctx.GroupMembers, _ctx.GroupMessages).Handle(
                new MarkGroupReadCommand { GroupId = group.Id, UserId = 99999 }, default));
    }

    [Fact]
    public async Task 已读回执标记已读并转发给被读方()
    {
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id);

        await new SendReadReceiptHandler(_ctx.PrivateMessages, _ctx.Notifier).Handle(
            new SendReadReceiptCommand
            {
                ReaderId = b.Id,
                TargetUserId = a.Id,
                MessageId = message.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }, default);

        Assert.True((await _ctx.PrivateMessages.FindByIdAsync(message.Id))!.IsRead);
        var push = Assert.Single(_ctx.Notifier.OfKind(PushKind.ReadReceipt));
        Assert.Equal(a.Id, push.ToUserId);
    }

    [Fact]
    public async Task 已读回执_消息ID无法解析时只转发不落库()
    {
        var (a, b) = _ctx.GivenFriends();

        await new SendReadReceiptHandler(_ctx.PrivateMessages, _ctx.Notifier).Handle(
            new SendReadReceiptCommand
            {
                ReaderId = b.Id, TargetUserId = a.Id, MessageId = "not-a-number"
            }, default);

        Assert.Single(_ctx.Notifier.OfKind(PushKind.ReadReceipt));
    }

    [Fact]
    public async Task 已读回执_未指定被读方时不转发()
    {
        await new SendReadReceiptHandler(_ctx.PrivateMessages, _ctx.Notifier).Handle(
            new SendReadReceiptCommand { ReaderId = 10002, TargetUserId = 0, MessageId = null },
            default);

        Assert.Empty(_ctx.Notifier.Pushes);
    }

    [Fact]
    public async Task 已读回执_不是接收方时不改动已读状态()
    {
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id);

        // 发送方自己发已读回执，不应把自己的消息标记为已读
        await new SendReadReceiptHandler(_ctx.PrivateMessages, _ctx.Notifier).Handle(
            new SendReadReceiptCommand
            {
                ReaderId = a.Id,
                TargetUserId = b.Id,
                MessageId = message.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }, default);

        Assert.False((await _ctx.PrivateMessages.FindByIdAsync(message.Id))!.IsRead);
    }

    [Fact]
    public async Task 正在输入转发给对方()
    {
        await new SendTypingHandler(_ctx.Notifier).Handle(
            new SendTypingCommand { FromUserId = 10001, ToUserId = 10002 }, default);

        var push = Assert.Single(_ctx.Notifier.OfKind(PushKind.Typing));
        Assert.Equal(10002, push.ToUserId);
        Assert.Equal(10001, push.ContextId);
    }

    [Fact]
    public async Task 正在输入_目标无效时不转发()
    {
        await new SendTypingHandler(_ctx.Notifier).Handle(
            new SendTypingCommand { FromUserId = 10001, ToUserId = 0 }, default);

        Assert.Empty(_ctx.Notifier.Pushes);
    }
}

public class SearchMessagesTests
{
    private readonly ApplicationTestContext _ctx = new();

    private SearchMessagesHandler Handler()
        => new(_ctx.PrivateMessages, _ctx.GroupMessages, _ctx.Friendships,
            _ctx.GroupMembers, _ctx.Groups, _ctx.Users);

    [Fact]
    public async Task 空关键词被拒()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SearchMessagesQuery { UserId = 10001, Keyword = "   " }, default));

        Assert.Equal("请输入搜索关键词", ex.Message);
    }

    [Fact]
    public async Task 全局搜索合并私聊与群聊结果_按时间倒序()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var friend = _ctx.GivenUser("好友", "f@test.local");
        await _ctx.Friendships.AddAsync(
            Domain.Friends.Friendship.EstablishDirectly(owner.Id, friend.Id, _ctx.Now));
        var group = _ctx.GivenGroup(owner.Id);

        _ctx.GivenPrivateMessage(owner.Id, friend.Id, "关键词私聊", _ctx.Now.AddMinutes(-5));
        _ctx.GivenGroupMessage(group.Id, owner.Id, "关键词群聊", _ctx.Now.AddMinutes(-1));

        var result = await Handler().Handle(
            new SearchMessagesQuery { UserId = owner.Id, Keyword = "关键词" }, default);

        Assert.Equal(2, result.Data!.Items.Count);
        Assert.Equal("关键词群聊", result.Data.Items[0].Content);   // 最新在前
        Assert.Equal("关键词私聊", result.Data.Items[1].Content);
    }

    [Fact]
    public async Task 已撤回的消息不出现在搜索结果()
    {
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id, "关键词内容");
        message.ForceDelete();
        await _ctx.PrivateMessages.UpdateAsync(message);

        var result = await Handler().Handle(
            new SearchMessagesQuery { UserId = a.Id, Keyword = "关键词" }, default);

        Assert.Empty(result.Data!.Items);
    }

    [Fact]
    public async Task 会话内搜索_私聊需好友关系()
    {
        var a = _ctx.GivenUser("张三", "a@test.local");
        var b = _ctx.GivenUser("李四", "b@test.local");

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SearchMessagesQuery
            {
                UserId = a.Id, Keyword = "x",
                ScopeType = ChatSessionTypeNames.Private, ScopeId = b.Id
            }, default));

        Assert.Equal("不是好友关系", ex.Message);
    }

    [Fact]
    public async Task 会话内搜索_私聊只返回该会话消息()
    {
        var (a, b) = _ctx.GivenFriends();
        var c = _ctx.GivenUser("王五", "c@test.local");
        await _ctx.Friendships.AddAsync(
            Domain.Friends.Friendship.EstablishDirectly(a.Id, c.Id, _ctx.Now));
        _ctx.GivenPrivateMessage(a.Id, b.Id, "关键词给乙");
        _ctx.GivenPrivateMessage(a.Id, c.Id, "关键词给丙");

        var result = await Handler().Handle(new SearchMessagesQuery
        {
            UserId = a.Id, Keyword = "关键词",
            ScopeType = ChatSessionTypeNames.Private, ScopeId = b.Id
        }, default);

        Assert.Equal("关键词给乙", Assert.Single(result.Data!.Items).Content);
    }

    [Fact]
    public async Task 会话内搜索_群聊需群成员身份()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SearchMessagesQuery
            {
                UserId = 99999, Keyword = "x",
                ScopeType = ChatSessionTypeNames.Group, ScopeId = group.Id
            }, default));

        Assert.Equal("你不是该群成员", ex.Message);
    }

    [Theory]
    [InlineData(ChatSessionTypeNames.Private)]
    [InlineData(ChatSessionTypeNames.Group)]
    public async Task 会话ID非法时被拒(string scopeType)
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SearchMessagesQuery
            {
                UserId = 10001, Keyword = "x", ScopeType = scopeType, ScopeId = 0
            }, default));

        Assert.Equal("无效的会话", ex.Message);
    }

    [Fact]
    public async Task 无效scopeType退化为全局搜索()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(a.Id, b.Id, "关键词");

        var result = await Handler().Handle(new SearchMessagesQuery
        {
            UserId = a.Id, Keyword = "关键词", ScopeType = "bogus", ScopeId = 1
        }, default);

        Assert.Single(result.Data!.Items);
    }

    [Fact]
    public async Task 超长关键词被截断而非报错()
    {
        var (a, b) = _ctx.GivenFriends();

        var result = await Handler().Handle(new SearchMessagesQuery
        {
            UserId = a.Id, Keyword = new string('x', 100)
        }, default);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task 搜索结果里自己发的消息标注为我()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(a.Id, b.Id, "关键词");

        var result = await Handler().Handle(
            new SearchMessagesQuery { UserId = a.Id, Keyword = "关键词" }, default);

        Assert.Equal("我", Assert.Single(result.Data!.Items).SenderName);
    }
}

public class ChatSessionsTests
{
    private readonly ApplicationTestContext _ctx = new();

    private GetChatSessionsHandler Handler()
        => new(_ctx.PrivateMessages, _ctx.GroupMessages, _ctx.GroupMembers,
            _ctx.Groups, _ctx.Users, _ctx.FriendSettings, _ctx.SessionSettings);

    [Fact]
    public async Task 会话列表聚合私聊与群聊()
    {
        var (a, b) = _ctx.GivenFriends();
        var group = _ctx.GivenGroup(a.Id);
        _ctx.GivenPrivateMessage(b.Id, a.Id, "私聊消息");
        _ctx.GivenGroupMessage(group.Id, a.Id, "群消息");

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = a.Id }, default);

        Assert.Equal(2, result.Data!.Count);
        Assert.Contains(result.Data, s => s.Type == "private" && s.Id == b.Id);
        Assert.Contains(result.Data, s => s.Type == "group" && s.Id == group.Id);
    }

    [Fact]
    public async Task 私聊会话名优先显示我设的备注()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(b.Id, a.Id, "消息");
        var setting = Domain.Friends.FriendSetting.CreateFor(a.Id, b.Id, _ctx.Now);
        setting.SetRemark("老王", _ctx.Now);
        await _ctx.FriendSettings.AddAsync(setting);

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = a.Id }, default);

        Assert.Equal("老王", Assert.Single(result.Data!).Name);
    }

    [Fact]
    public async Task 无备注时显示对方昵称()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(b.Id, a.Id, "消息");

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = a.Id }, default);

        Assert.Equal("李四", Assert.Single(result.Data!).Name);
    }

    [Fact]
    public async Task 会话带未读数与最后消息()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(b.Id, a.Id, "旧消息", _ctx.Now.AddMinutes(-5));
        _ctx.GivenPrivateMessage(b.Id, a.Id, "最新消息", _ctx.Now.AddMinutes(-1));

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = a.Id }, default);

        var session = Assert.Single(result.Data!);
        Assert.Equal("最新消息", session.LastMessage);
        Assert.Equal(2, session.UnreadCount);
    }

    [Fact]
    public async Task 置顶会话排在前面()
    {
        var (a, b) = _ctx.GivenFriends();
        var group = _ctx.GivenGroup(a.Id);
        // 私聊消息更新，群消息更旧；但群被置顶，应排在前
        _ctx.GivenPrivateMessage(b.Id, a.Id, "私聊", _ctx.Now.AddMinutes(-1));
        _ctx.GivenGroupMessage(group.Id, a.Id, "群聊", _ctx.Now.AddMinutes(-10));
        await _ctx.SessionSettings.AddAsync(SessionSetting.Create(
            a.Id, ChatSessionType.Group, group.Id, isPinned: true, muted: false, _ctx.Now));

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = a.Id }, default);

        Assert.Equal("group", result.Data![0].Type);
        Assert.True(result.Data[0].IsPinned);
    }

    [Fact]
    public async Task 免打扰标记被带出()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(b.Id, a.Id, "消息");
        await _ctx.SessionSettings.AddAsync(SessionSetting.Create(
            a.Id, ChatSessionType.Private, b.Id, isPinned: false, muted: true, _ctx.Now));

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = a.Id }, default);

        Assert.True(Assert.Single(result.Data!).Muted);
    }

    [Fact]
    public async Task 机器人会话被标记IsBot()
    {
        var owner = _ctx.GivenUser();
        var (_, botUser) = _ctx.GivenRobot(owner.Id);
        _ctx.GivenPrivateMessage(botUser.Id, owner.Id, "机器人消息");

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = owner.Id }, default);

        Assert.True(Assert.Single(result.Data!).IsBot);
    }

    [Fact]
    public async Task 群会话未读按已读游标计算()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);
        var read = _ctx.GivenGroupMessage(group.Id, owner.Id, "已读");
        _ctx.GivenGroupMessage(group.Id, owner.Id, "未读1");
        _ctx.GivenGroupMessage(group.Id, owner.Id, "未读2");

        var memberEntity = await _ctx.GroupMembers.FindAsync(group.Id, member.Id);
        memberEntity!.AdvanceReadCursor(read.Id);
        await _ctx.GroupMembers.UpdateAsync(memberEntity);

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = member.Id }, default);

        Assert.Equal(2, Assert.Single(result.Data!).UnreadCount);
    }

    [Fact]
    public async Task 无消息的群仍出现在会话列表()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = owner.Id }, default);

        var session = Assert.Single(result.Data!);
        Assert.Equal(group.Id, session.Id);
        Assert.Equal(string.Empty, session.LastMessage);
    }

    [Fact]
    public async Task 无任何会话时返回空列表()
    {
        var user = _ctx.GivenUser();

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = user.Id }, default);

        Assert.Empty(result.Data!);
    }
}

public class SessionSettingUseCaseTests
{
    private readonly ApplicationTestContext _ctx = new();

    private UpdateSessionSettingHandler Handler()
        => new(_ctx.SessionSettings, _ctx.Clock);

    [Fact]
    public async Task 首次设置时创建记录()
    {
        var result = await Handler().Handle(new UpdateSessionSettingCommand
        {
            UserId = 10001, Type = "group", Id = 5, IsPinned = true
        }, default);

        Assert.Equal("设置已保存", result.Message);
        var setting = Assert.Single(_ctx.SessionSettings.All);
        Assert.True(setting.IsPinned);
        Assert.False(setting.Muted);
    }

    [Fact]
    public async Task 再次设置时更新同一条记录且只改传入项()
    {
        await Handler().Handle(new UpdateSessionSettingCommand
        {
            UserId = 10001, Type = "group", Id = 5, IsPinned = true, Muted = true
        }, default);

        await Handler().Handle(new UpdateSessionSettingCommand
        {
            UserId = 10001, Type = "group", Id = 5, IsPinned = false
        }, default);

        var setting = Assert.Single(_ctx.SessionSettings.All);
        Assert.False(setting.IsPinned);
        Assert.True(setting.Muted);        // 未传的项保持原值
    }

    [Fact]
    public async Task 无效会话类型被拒()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new UpdateSessionSettingCommand
            {
                UserId = 10001, Type = "bogus", Id = 5, Muted = true
            }, default));

        Assert.Equal("无效的会话类型", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task 无效会话ID被拒(long sessionId)
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new UpdateSessionSettingCommand
            {
                UserId = 10001, Type = "group", Id = sessionId, Muted = true
            }, default));

        Assert.Equal("无效的会话 ID", ex.Message);
    }

    [Fact]
    public async Task 两项都不传时被拒()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new UpdateSessionSettingCommand { UserId = 10001, Type = "group", Id = 5 }, default));

        Assert.Equal("没有需要更新的设置", ex.Message);
    }

    [Fact]
    public async Task 不同用户对同一会话的设置互相独立()
    {
        await Handler().Handle(new UpdateSessionSettingCommand
        {
            UserId = 10001, Type = "group", Id = 5, IsPinned = true
        }, default);
        await Handler().Handle(new UpdateSessionSettingCommand
        {
            UserId = 10002, Type = "group", Id = 5, IsPinned = false
        }, default);

        Assert.Equal(2, _ctx.SessionSettings.All.Count);
    }
}
