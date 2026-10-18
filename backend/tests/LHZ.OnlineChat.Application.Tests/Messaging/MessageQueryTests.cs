using LHZ.OnlineChat.Application.Abstractions;
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

    [Fact(DisplayName = "历史按时间正序返回")]
    public async Task History_ReturnsInChronologicalOrder()
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

    [Fact(DisplayName = "没有客户端消息ID的历史消息也返回可用的MessageId")]
    public async Task History_MessageWithoutClientId_StillHasMessageId()
    {
        var (a, b) = _ctx.GivenFriends();
        // GivenPrivateMessage 不带客户端 ID —— 与机器人推送的消息同一形态
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id, "你好");

        var result = await Handler().Handle(
            new GetPrivateHistoryQuery { UserId = a.Id, FriendId = b.Id }, default);

        var item = Assert.Single(result.Data!.Items);

        // 历史接口必须与 WS 推送用同一个 messageId 口径（PublicMessageId），
        // 否则前端只能自己重算一遍回落规则，两边一旦不一致就会出现重复上屏
        Assert.Equal(
            message.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            item.MessageId);
    }

    [Fact(DisplayName = "历史携带发送者信息")]
    public async Task History_IncludesSenderInfo()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(a.Id, b.Id, "你好");

        var result = await Handler().Handle(
            new GetPrivateHistoryQuery { UserId = b.Id, FriendId = a.Id }, default);

        Assert.Equal("张三", Assert.Single(result.Data!.Items).SenderName);
    }

    [Fact(DisplayName = "非好友读历史被拒")]
    public async Task History_NonFriend_IsRejected()
    {
        var a = _ctx.GivenUser("张三", "a@test.local");
        var b = _ctx.GivenUser("李四", "b@test.local");

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new GetPrivateHistoryQuery { UserId = a.Id, FriendId = b.Id }, default));

        Assert.Equal("不是好友关系", ex.Message);
    }

    [Fact(DisplayName = "分页返回正确总数")]
    public async Task History_Pagination_ReturnsCorrectTotal()
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

    [Fact(DisplayName = "只返回双方之间的消息")]
    public async Task History_ReturnsOnlyMessagesBetweenBothParties()
    {
        var (a, b) = _ctx.GivenFriends();
        var c = _ctx.GivenUser("王五", "c@test.local");
        _ctx.GivenPrivateMessage(a.Id, b.Id, "给乙");
        _ctx.GivenPrivateMessage(a.Id, c.Id, "给丙");

        var result = await Handler().Handle(
            new GetPrivateHistoryQuery { UserId = a.Id, FriendId = b.Id }, default);

        Assert.Equal("给乙", Assert.Single(result.Data!.Items).Content);
    }

    [Fact(DisplayName = "页长上限被夹紧")]
    public async Task History_PageSizeAboveLimit_IsClamped()
    {
        var (a, b) = _ctx.GivenFriends();

        var result = await Handler().Handle(new GetPrivateHistoryQuery
        {
            UserId = a.Id, FriendId = b.Id, PageSize = 9999
        }, default);

        Assert.Equal(100, result.Data!.PageSize);
    }

    /// <summary>
    /// 造 total 条消息（消息1 最旧、消息{total} 最新），并只把最近 cachedCount 条灌进最近消息缓存。
    /// 缓存按时间正序 AppendAsync —— 替身与生产一样是头部插入（LPUSH），所以缓存头部是最新一条。
    /// </summary>
    private (int A, int B) GivenConversation(int total, int? cachedCount = null)
    {
        var (a, b) = _ctx.GivenFriends();
        var now = _ctx.Now;
        var cacheFrom = cachedCount is > 0 ? total - cachedCount.Value + 1 : total + 1;

        for (var i = 1; i <= total; i++)
        {
            var sentAt = now.AddSeconds(-(total - i));
            _ctx.GivenPrivateMessage(a.Id, b.Id, $"消息{i}", sentAt);

            if (i < cacheFrom) continue;
            _ctx.Cache.AppendAsync(
                ChatSessionType.Private, a.Id, b.Id,
                new RealtimeMessage
                {
                    SessionType = ChatSessionType.Private,
                    SenderId = a.Id,
                    TargetId = b.Id,
                    Content = $"消息{i}",
                    Kind = MessageKind.Text,
                    MessageId = $"cmid-{i}",
                    SenderName = "张三",
                    SentAt = sentAt
                }).GetAwaiter().GetResult();
        }

        return (a.Id, b.Id);
    }

    [Fact(DisplayName = "缓存命中且页长小于缓存长度时返回最新的一页")]
    public async Task History_CacheHitSmallerPageSize_ReturnsNewest()
    {
        var (a, b) = GivenConversation(50, cachedCount: 50);

        var result = await Handler().Handle(new GetPrivateHistoryQuery
        {
            UserId = a, FriendId = b, Page = 1, PageSize = 20
        }, default);

        var items = result.Data!.Items;

        Assert.Equal(20, items.Count);
        // 缓存是「最新在前」的滑动窗口，第 1 页必须取窗口里**最新**的 20 条（消息31..消息50）。
        // 取成最旧的 20 条（消息1..消息20）时，最新的消息就永远翻不到 ——
        // 只有页长恰好等于缓存长度（默认 50 == 50）时才会掩盖。
        Assert.Equal("消息31", items[0].Content);
        Assert.Equal("消息50", items[^1].Content);
    }

    [Fact(DisplayName = "缓存命中且页长等于缓存长度时仍返回整窗")]
    public async Task History_CacheHitDefaultPageSize_ReturnsWholeWindow()
    {
        var (a, b) = GivenConversation(50, cachedCount: 50);

        var result = await Handler().Handle(new GetPrivateHistoryQuery
        {
            UserId = a, FriendId = b, Page = 1, PageSize = 50
        }, default);

        var items = result.Data!.Items;

        Assert.Equal(50, items.Count);
        Assert.Equal("消息1", items[0].Content);
        Assert.Equal("消息50", items[^1].Content);
    }

    [Fact(DisplayName = "缓存不足一页时回落到数据库")]
    public async Task History_CacheInsufficient_FallsBackToDatabase()
    {
        var (a, b) = GivenConversation(50, cachedCount: 10);

        var result = await Handler().Handle(new GetPrivateHistoryQuery
        {
            UserId = a, FriendId = b, Page = 1, PageSize = 20
        }, default);

        var items = result.Data!.Items;

        Assert.Equal(20, items.Count);
        // 缓存只有 10 条 < 页长 20 → 走数据库；数据库第 1 页本来就是最新的 20 条
        Assert.Equal("消息31", items[0].Content);
        Assert.Equal("消息50", items[^1].Content);
    }

    [Fact(DisplayName = "非首页不读缓存")]
    public async Task History_PageTwo_IgnoresCache()
    {
        var (a, b) = GivenConversation(50, cachedCount: 50);

        var result = await Handler().Handle(new GetPrivateHistoryQuery
        {
            UserId = a, FriendId = b, Page = 2, PageSize = 20
        }, default);

        var items = result.Data!.Items;

        Assert.Equal(20, items.Count);
        Assert.Equal("消息11", items[0].Content);
        Assert.Equal("消息30", items[^1].Content);
    }
}

public class GroupHistoryTests
{
    private readonly ApplicationTestContext _ctx = new();

    private GetGroupHistoryHandler Handler()
        => new(_ctx.GroupMessages, _ctx.GroupMembers, _ctx.Users);

    [Fact(DisplayName = "群成员可读历史")]
    public async Task GroupHistory_Member_CanRead()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);
        _ctx.GivenGroupMessage(group.Id, owner.Id, "群消息");

        var result = await Handler().Handle(
            new GetGroupHistoryQuery { GroupId = group.Id, UserId = owner.Id }, default);

        Assert.Equal("群消息", Assert.Single(result.Data!.Items).Content);
    }

    [Fact(DisplayName = "非群成员被拒")]
    public async Task GroupHistory_NonMember_IsRejected()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new GetGroupHistoryQuery { GroupId = group.Id, UserId = 99999 }, default));

        Assert.Equal("你不是该群成员", ex.Message);
    }

    [Fact(DisplayName = "历史携带提及列表")]
    public async Task GroupHistory_IncludesMentions()
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

    [Fact(DisplayName = "未读数只统计发给我的未读")]
    public async Task UnreadCount_CountsOnlyMessagesSentToMe()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(a.Id, b.Id, "给乙1");
        _ctx.GivenPrivateMessage(a.Id, b.Id, "给乙2");
        _ctx.GivenPrivateMessage(b.Id, a.Id, "给甲");

        var result = await new GetUnreadCountHandler(_ctx.PrivateMessages).Handle(
            new GetUnreadCountQuery { UserId = b.Id }, default);

        Assert.Equal(2, result.Data!.PrivateUnread);
    }

    [Fact(DisplayName = "离线消息按时间正序")]
    public async Task OfflineMessages_AreInChronologicalOrder()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(a.Id, b.Id, "早", _ctx.Now.AddMinutes(-5));
        _ctx.GivenPrivateMessage(a.Id, b.Id, "晚", _ctx.Now.AddMinutes(-1));

        var result = await new GetOfflineMessagesHandler(_ctx.PrivateMessages, _ctx.Users).Handle(
            new GetOfflineMessagesQuery { UserId = b.Id }, default);

        Assert.Equal(2, result.Data!.Count);
        Assert.Equal("早", result.Data[0].Content);
    }

    [Fact(DisplayName = "已读消息不算离线消息")]
    public async Task OfflineMessages_ExcludeAlreadyRead()
    {
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id, "已读");
        message.MarkAsRead(b.Id);
        await _ctx.PrivateMessages.UpdateAsync(message);

        var result = await new GetOfflineMessagesHandler(_ctx.PrivateMessages, _ctx.Users).Handle(
            new GetOfflineMessagesQuery { UserId = b.Id }, default);

        Assert.Empty(result.Data!);
    }

    [Fact(DisplayName = "标记单条已读")]
    public async Task MarkMessageRead_MarksSingleMessage()
    {
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id);

        var result = await new MarkMessageReadHandler(_ctx.PrivateMessages).Handle(
            new MarkMessageReadCommand { MessageId = message.Id, UserId = b.Id }, default);

        Assert.Equal("已标记已读", result.Message);
        Assert.True((await _ctx.PrivateMessages.FindByIdAsync(message.Id))!.IsRead);
    }

    [Fact(DisplayName = "非接收方不能标记已读")]
    public async Task MarkMessageRead_NonRecipient_IsRejected()
    {
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new MarkMessageReadHandler(_ctx.PrivateMessages).Handle(
                new MarkMessageReadCommand { MessageId = message.Id, UserId = a.Id }, default));

        Assert.Equal("消息不存在或无权操作", ex.Message);
    }

    [Fact(DisplayName = "消息不存在时抛出")]
    public async Task MarkMessageRead_MessageNotFound_Throws()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => new MarkMessageReadHandler(_ctx.PrivateMessages).Handle(
                new MarkMessageReadCommand { MessageId = 99999, UserId = 10001 }, default));
    }

    [Fact(DisplayName = "批量标记某人发来的消息已读")]
    public async Task MarkConversationRead_MarksAllFromSender()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(a.Id, b.Id, "1");
        _ctx.GivenPrivateMessage(a.Id, b.Id, "2");

        var result = await new MarkConversationReadHandler(_ctx.PrivateMessages).Handle(
            new MarkConversationReadCommand { SenderId = a.Id, UserId = b.Id }, default);

        Assert.Equal("已全部标记已读", result.Message);
        Assert.Equal(0, await _ctx.PrivateMessages.CountUnreadForAsync(b.Id));
    }

    [Fact(DisplayName = "标记群已读推进游标到最新")]
    public async Task MarkGroupRead_AdvancesCursorToLatest()
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

    [Fact(DisplayName = "非群成员不能标记群已读")]
    public async Task MarkGroupRead_NonMember_Throws()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => new MarkGroupReadHandler(_ctx.GroupMembers, _ctx.GroupMessages).Handle(
                new MarkGroupReadCommand { GroupId = group.Id, UserId = 99999 }, default));
    }

    [Fact(DisplayName = "已读回执标记已读并转发给被读方")]
    public async Task SendReadReceipt_MarksReadAndForwardsToSender()
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

    [Fact(DisplayName = "已读回执_消息ID无法解析时只转发不落库")]
    public async Task SendReadReceipt_UnparsableMessageId_ForwardsWithoutSaving()
    {
        var (a, b) = _ctx.GivenFriends();

        await new SendReadReceiptHandler(_ctx.PrivateMessages, _ctx.Notifier).Handle(
            new SendReadReceiptCommand
            {
                ReaderId = b.Id, TargetUserId = a.Id, MessageId = "not-a-number"
            }, default);

        Assert.Single(_ctx.Notifier.OfKind(PushKind.ReadReceipt));
    }

    [Fact(DisplayName = "已读回执_未指定被读方时不转发")]
    public async Task SendReadReceipt_NoTargetUser_DoesNotForward()
    {
        await new SendReadReceiptHandler(_ctx.PrivateMessages, _ctx.Notifier).Handle(
            new SendReadReceiptCommand { ReaderId = 10002, TargetUserId = 0, MessageId = null },
            default);

        Assert.Empty(_ctx.Notifier.Pushes);
    }

    [Fact(DisplayName = "已读回执_不是接收方时不改动已读状态")]
    public async Task SendReadReceipt_NotRecipient_DoesNotMarkRead()
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

    [Fact(DisplayName = "正在输入转发给对方")]
    public async Task SendTyping_ForwardsToTarget()
    {
        await new SendTypingHandler(_ctx.Notifier).Handle(
            new SendTypingCommand { FromUserId = 10001, ToUserId = 10002 }, default);

        var push = Assert.Single(_ctx.Notifier.OfKind(PushKind.Typing));
        Assert.Equal(10002, push.ToUserId);
        Assert.Equal(10001, push.ContextId);
    }

    [Fact(DisplayName = "正在输入_目标无效时不转发")]
    public async Task SendTyping_InvalidTarget_DoesNotForward()
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

    [Fact(DisplayName = "空关键词被拒")]
    public async Task SearchMessages_EmptyKeyword_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SearchMessagesQuery { UserId = 10001, Keyword = "   " }, default));

        Assert.Equal("请输入搜索关键词", ex.Message);
    }

    [Fact(DisplayName = "全局搜索合并私聊与群聊结果_按时间倒序")]
    public async Task SearchMessages_Global_MergesPrivateAndGroupNewestFirst()
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

    [Fact(DisplayName = "没有客户端消息ID的搜索结果也带可用MessageId")]
    public async Task SearchMessages_ResultWithoutClientId_StillHasMessageId()
    {
        var (a, b) = _ctx.GivenFriends();
        // 机器人推送的消息 ClientMessageId 为 null（RobotTriggerHandlers 显式传 null），
        // 而 MessageSearchResultDto 没有 Id 字段，前端拿不到任何兜底值
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id, "关键词内容");

        var result = await Handler().Handle(
            new SearchMessagesQuery { UserId = a.Id, Keyword = "关键词" }, default);

        var item = Assert.Single(result.Data!.Items);

        // 回归：此前这里返回 null，前端 `if (r.messageId)` 为假，
        // 点击搜索结果既不定位也不高亮（静默无反应）
        Assert.NotNull(item.MessageId);
        Assert.Equal(
            message.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            item.MessageId);
    }

    [Fact(DisplayName = "已撤回的消息不出现在搜索结果")]
    public async Task SearchMessages_RecalledMessages_AreExcluded()
    {
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id, "关键词内容");
        message.ForceDelete();
        await _ctx.PrivateMessages.UpdateAsync(message);

        var result = await Handler().Handle(
            new SearchMessagesQuery { UserId = a.Id, Keyword = "关键词" }, default);

        Assert.Empty(result.Data!.Items);
    }

    [Fact(DisplayName = "会话内搜索_私聊需好友关系")]
    public async Task SearchMessages_ScopedPrivate_RequiresFriendship()
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

    [Fact(DisplayName = "会话内搜索_私聊只返回该会话消息")]
    public async Task SearchMessages_ScopedPrivate_ReturnsOnlyThatConversation()
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

    [Fact(DisplayName = "会话内搜索_群聊需群成员身份")]
    public async Task SearchMessages_ScopedGroup_RequiresMembership()
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

    [Theory(DisplayName = "会话ID非法时被拒")]
    [InlineData(ChatSessionTypeNames.Private)]
    [InlineData(ChatSessionTypeNames.Group)]
    public async Task SearchMessages_InvalidScopeId_IsRejected(string scopeType)
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SearchMessagesQuery
            {
                UserId = 10001, Keyword = "x", ScopeType = scopeType, ScopeId = 0
            }, default));

        Assert.Equal("无效的会话", ex.Message);
    }

    [Fact(DisplayName = "无效scopeType退化为全局搜索")]
    public async Task SearchMessages_UnknownScopeType_FallsBackToGlobal()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(a.Id, b.Id, "关键词");

        var result = await Handler().Handle(new SearchMessagesQuery
        {
            UserId = a.Id, Keyword = "关键词", ScopeType = "bogus", ScopeId = 1
        }, default);

        Assert.Single(result.Data!.Items);
    }

    [Fact(DisplayName = "超长关键词被截断而非报错")]
    public async Task SearchMessages_OverlongKeyword_IsTruncatedNotRejected()
    {
        var (a, b) = _ctx.GivenFriends();

        var result = await Handler().Handle(new SearchMessagesQuery
        {
            UserId = a.Id, Keyword = new string('x', 100)
        }, default);

        Assert.True(result.Success);
    }

    [Fact(DisplayName = "搜索结果里自己发的消息标注为我")]
    public async Task SearchMessages_OwnMessages_AreLabeledAsMe()
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

    [Fact(DisplayName = "会话列表聚合私聊与群聊")]
    public async Task ChatSessions_AggregatePrivateAndGroup()
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

    [Fact(DisplayName = "私聊会话名优先显示我设的备注")]
    public async Task ChatSessions_PrivateName_PrefersMyRemark()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(b.Id, a.Id, "消息");
        var setting = Domain.Friends.FriendSetting.CreateFor(a.Id, b.Id, _ctx.Now);
        setting.SetRemark("老王", _ctx.Now);
        await _ctx.FriendSettings.AddAsync(setting);

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = a.Id }, default);

        Assert.Equal("老王", Assert.Single(result.Data!).Name);
    }

    [Fact(DisplayName = "无备注时显示对方昵称")]
    public async Task ChatSessions_NoRemark_FallsBackToNickname()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(b.Id, a.Id, "消息");

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = a.Id }, default);

        Assert.Equal("李四", Assert.Single(result.Data!).Name);
    }

    [Fact(DisplayName = "会话带未读数与最后消息")]
    public async Task ChatSessions_IncludeUnreadCountAndLastMessage()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(b.Id, a.Id, "旧消息", _ctx.Now.AddMinutes(-5));
        _ctx.GivenPrivateMessage(b.Id, a.Id, "最新消息", _ctx.Now.AddMinutes(-1));

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = a.Id }, default);

        var session = Assert.Single(result.Data!);
        Assert.Equal("最新消息", session.LastMessage);
        Assert.Equal(2, session.UnreadCount);
    }

    [Fact(DisplayName = "会话预览带最后一条消息的类型（转发卡片别把 JSON 糊到列表里）")]
    public async Task ChatSessions_IncludeLastMessageType()
    {
        var (a, b) = _ctx.GivenFriends();
        var group = _ctx.GivenGroup(a.Id);
        _ctx.GivenPrivateMessage(b.Id, a.Id, "{\"v\":1,\"items\":[]}", kind: Domain.Messaging.MessageKind.Forward);
        _ctx.GivenGroupMessage(group.Id, a.Id, "# 标题", kind: Domain.Messaging.MessageKind.Markdown);

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = a.Id }, default);

        var privateSession = result.Data!.Single(s => s.Type == "private");
        Assert.Equal((int)Domain.Messaging.MessageKind.Forward, privateSession.LastMessageType);
        var groupSession = result.Data!.Single(s => s.Type == "group");
        Assert.Equal((int)Domain.Messaging.MessageKind.Markdown, groupSession.LastMessageType);
    }

    [Fact(DisplayName = "置顶会话排在前面")]
    public async Task ChatSessions_Pinned_ComesFirst()
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

    [Fact(DisplayName = "免打扰标记被带出")]
    public async Task ChatSessions_MutedFlag_IsReturned()
    {
        var (a, b) = _ctx.GivenFriends();
        _ctx.GivenPrivateMessage(b.Id, a.Id, "消息");
        await _ctx.SessionSettings.AddAsync(SessionSetting.Create(
            a.Id, ChatSessionType.Private, b.Id, isPinned: false, muted: true, _ctx.Now));

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = a.Id }, default);

        Assert.True(Assert.Single(result.Data!).Muted);
    }

    [Fact(DisplayName = "机器人会话被标记IsBot")]
    public async Task ChatSessions_BotSession_IsFlaggedAsBot()
    {
        var owner = _ctx.GivenUser();
        var (_, botUser) = _ctx.GivenRobot(owner.Id);
        _ctx.GivenPrivateMessage(botUser.Id, owner.Id, "机器人消息");

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = owner.Id }, default);

        Assert.True(Assert.Single(result.Data!).IsBot);
    }

    [Fact(DisplayName = "群会话未读按已读游标计算")]
    public async Task ChatSessions_GroupUnread_CountedByReadCursor()
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

    [Fact(DisplayName = "无消息的群仍出现在会话列表")]
    public async Task ChatSessions_GroupWithoutMessages_StillListed()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var result = await Handler().Handle(new GetChatSessionsQuery { UserId = owner.Id }, default);

        var session = Assert.Single(result.Data!);
        Assert.Equal(group.Id, session.Id);
        Assert.Equal(string.Empty, session.LastMessage);
    }

    [Fact(DisplayName = "无任何会话时返回空列表")]
    public async Task ChatSessions_NoSessions_ReturnsEmpty()
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

    [Fact(DisplayName = "首次设置时创建记录")]
    public async Task UpdateSessionSetting_FirstTime_CreatesRecord()
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

    [Fact(DisplayName = "再次设置时更新同一条记录且只改传入项")]
    public async Task UpdateSessionSetting_Again_UpdatesSameRecordAndGivenFields()
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

    [Fact(DisplayName = "无效会话类型被拒")]
    public async Task UpdateSessionSetting_InvalidType_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new UpdateSessionSettingCommand
            {
                UserId = 10001, Type = "bogus", Id = 5, Muted = true
            }, default));

        Assert.Equal("无效的会话类型", ex.Message);
    }

    [Theory(DisplayName = "无效会话ID被拒")]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task UpdateSessionSetting_InvalidSessionId_IsRejected(long sessionId)
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new UpdateSessionSettingCommand
            {
                UserId = 10001, Type = "group", Id = sessionId, Muted = true
            }, default));

        Assert.Equal("无效的会话 ID", ex.Message);
    }

    [Fact(DisplayName = "两项都不传时被拒")]
    public async Task UpdateSessionSetting_NoFieldsProvided_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new UpdateSessionSettingCommand { UserId = 10001, Type = "group", Id = 5 }, default));

        Assert.Equal("没有需要更新的设置", ex.Message);
    }

    [Fact(DisplayName = "不同用户对同一会话的设置互相独立")]
    public async Task UpdateSessionSetting_DifferentUsers_AreIndependent()
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