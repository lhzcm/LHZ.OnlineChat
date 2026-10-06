using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Domain.Tests.Messaging;

public class PrivateMessageTests
{
    [Fact]
    public void Send_记录收发双方与内容()
    {
        var message = PrivateMessage.Send(
            senderId: 10001, receiverId: 10002, "你好", MessageKind.Text,
            clientMessageId: "cmid-1", reply: null, T.Now);

        Assert.Equal(10001, message.SenderId);
        Assert.Equal(10002, message.ReceiverId);
        Assert.Equal("你好", message.Content);
        Assert.Equal(MessageKind.Text, message.Kind);
        Assert.Equal("cmid-1", message.ClientMessageId);
        Assert.False(message.IsRead);
        Assert.False(message.IsDeleted);
        Assert.Equal(T.Now, message.SentAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Send_客户端消息ID为空白时归为null(string? clientMessageId)
    {
        var message = PrivateMessage.Send(
            10001, 10002, "x", MessageKind.Text, clientMessageId, null, T.Now);

        Assert.Null(message.ClientMessageId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Send_内容为空时被拒(string? content)
    {
        // 空内容曾经被静默归为空串入库,前端会渲染出没有内容的空气泡
        var ex = Assert.Throws<DomainException>(() => PrivateMessage.Send(
            10001, 10002, content!, MessageKind.Text, null, null, T.Now));

        Assert.Equal("消息内容不能为空", ex.Message);
    }

    [Fact]
    public void Send_内容超长时被拒()
    {
        var tooLong = new string('x', MessageContentRules.MaxLength + 1);

        var ex = Assert.Throws<DomainException>(() => GroupMessage.Send(
            1, 10001, tooLong, MessageKind.Text, null, MentionList.Empty, null, T.Now));

        Assert.Contains("不能超过", ex.Message);
    }

    [Fact]
    public void Send_恰好达到长度上限时通过()
    {
        var atLimit = new string('x', MessageContentRules.MaxLength);

        var message = PrivateMessage.Send(
            10001, 10002, atLimit, MessageKind.Text, null, null, T.Now);

        Assert.Equal(MessageContentRules.MaxLength, message.Content.Length);
    }

    [Fact]
    public void Send_未定义的消息类型被拒()
    {
        // WS 入口是 (MessageKind)整数 的未检查转换,枚举校验是最后一道闸
        var undefined = (MessageKind)99;

        var ex = Assert.Throws<DomainException>(() => PrivateMessage.Send(
            10001, 10002, "内容", undefined, null, null, T.Now));

        Assert.Equal("不支持的消息类型", ex.Message);
    }

    [Fact]
    public void Send_带引用回复时拆成三列并可聚合回读()
    {
        var reply = MessageReply.Create("origin-1", "原文", "李四");

        var message = PrivateMessage.Send(
            10001, 10002, "回复你", MessageKind.Text, null, reply, T.Now);

        Assert.Equal("origin-1", message.ReplyMessageId);
        Assert.Equal("原文", message.ReplyContent);
        Assert.Equal("李四", message.ReplySenderName);
        Assert.Equal(reply, message.Reply);
    }

    [Fact]
    public void Reply_无引用时为null()
    {
        var message = PrivateMessage.Send(10001, 10002, "x", MessageKind.Text, null, null, T.Now);

        Assert.Null(message.Reply);
    }

    [Fact]
    public void PublicMessageId_优先用客户端ID()
    {
        var message = TestMessages.Private(id: 77, clientMessageId: "cmid-1");

        Assert.Equal("cmid-1", message.PublicMessageId);
    }

    [Fact]
    public void PublicMessageId_无客户端ID时回落数据库ID()
    {
        var message = TestMessages.Private(id: 77, clientMessageId: null);

        Assert.Equal("77", message.PublicMessageId);
    }

    [Theory]
    [InlineData("cmid-1", true)]
    [InlineData("77", true)]        // 数据库 ID 也算匹配
    [InlineData("other", false)]
    public void HasPublicId_两种标识都能匹配(string probe, bool expected)
    {
        var message = TestMessages.Private(id: 77, clientMessageId: "cmid-1");

        Assert.Equal(expected, message.HasPublicId(probe));
    }

    [Fact]
    public void SentAtUtc_补齐UTC标识()
    {
        var message = PrivateMessage.Send(
            10001, 10002, "x", MessageKind.Text, null, null,
            DateTime.SpecifyKind(T.Now, DateTimeKind.Unspecified));

        Assert.Equal(DateTimeKind.Utc, message.SentAtUtc.Kind);
    }

    [Fact]
    public void MarkAsRead_接收方可标记()
    {
        var message = TestMessages.Private(senderId: 10001, receiverId: 10002);

        message.MarkAsRead(10002);

        Assert.True(message.IsRead);
    }

    [Fact]
    public void MarkAsRead_非接收方被拒且提示语不泄露消息是否存在()
    {
        var message = TestMessages.Private(senderId: 10001, receiverId: 10002);

        var ex = Assert.Throws<DomainException>(() => message.MarkAsRead(10001));

        Assert.Equal("消息不存在或无权操作", ex.Message);
        Assert.False(message.IsRead);
    }
}

public class PrivateMessageRecallTests
{
    [Fact]
    public void Recall_本人在时间窗内可撤回()
    {
        var message = TestMessages.Private(senderId: 10001, sentAt: T.MinusMinutes(1));

        message.Recall(10001, T.Now);

        Assert.True(message.IsDeleted);
    }

    [Fact]
    public void Recall_非本人被拒()
    {
        var message = TestMessages.Private(senderId: 10001, receiverId: 10002);

        var ex = Assert.Throws<DomainException>(() => message.Recall(10002, T.Now));

        Assert.Equal("只能撤回自己发送的消息", ex.Message);
        Assert.False(message.IsDeleted);
    }

    [Fact]
    public void Recall_超过时间窗被拒()
    {
        var message = TestMessages.Private(senderId: 10001, sentAt: T.MinusMinutes(3));

        var ex = Assert.Throws<DomainException>(() => message.Recall(10001, T.Now));

        Assert.Equal("超过可撤回时间", ex.Message);
        Assert.False(message.IsDeleted);
    }

    [Fact]
    public void Recall_已撤回不能重复撤回()
    {
        var message = TestMessages.Private(senderId: 10001, sentAt: T.MinusMinutes(1));
        message.Recall(10001, T.Now);

        var ex = Assert.Throws<DomainException>(() => message.Recall(10001, T.Now));

        Assert.Equal("该消息已撤回", ex.Message);
    }

    [Fact]
    public void ForceDelete_管理后台不受本人与时间窗限制()
    {
        var message = TestMessages.Private(senderId: 10001, sentAt: T.MinusMinutes(600));

        message.ForceDelete();

        Assert.True(message.IsDeleted);
    }

    [Fact]
    public void ForceDelete_幂等()
    {
        var message = TestMessages.Private();

        message.ForceDelete();
        message.ForceDelete();

        Assert.True(message.IsDeleted);
    }
}

public class GroupMessageTests
{
    [Fact]
    public void Send_记录群与发送者()
    {
        var mentions = MentionList.From(new[] { 10002, 10003 });

        var message = GroupMessage.Send(
            groupId: 5, senderId: 10001, "大家好", MessageKind.Text,
            clientMessageId: "gmid-1", mentions, reply: null, T.Now);

        Assert.Equal(5, message.GroupId);
        Assert.Equal(10001, message.SenderId);
        Assert.Equal("大家好", message.Content);
        Assert.Equal("10002,10003", message.Mentions);
        Assert.Equal(mentions, message.MentionedUsers);
    }

    [Fact]
    public void Send_无提及时Mentions列存null()
    {
        var message = GroupMessage.Send(
            5, 10001, "x", MessageKind.Text, null, MentionList.Empty, null, T.Now);

        Assert.Null(message.Mentions);
        Assert.True(message.MentionedUsers.IsEmpty);
    }

    [Theory]
    [InlineData(MessageKind.Text)]
    [InlineData(MessageKind.Image)]
    [InlineData(MessageKind.File)]
    public void Send_支持全部消息类型(MessageKind kind)
    {
        var message = GroupMessage.Send(5, 10001, "x", kind, null, MentionList.Empty, null, T.Now);

        Assert.Equal(kind, message.Kind);
    }

    [Fact]
    public void Recall_规则与私聊一致()
    {
        var message = TestMessages.Group(senderId: 10001, sentAt: T.MinusMinutes(1));

        message.Recall(10001, T.Now);
        Assert.True(message.IsDeleted);

        var late = TestMessages.Group(senderId: 10001, sentAt: T.MinusMinutes(3));
        Assert.Equal("超过可撤回时间",
            Assert.Throws<DomainException>(() => late.Recall(10001, T.Now)).Message);

        var other = TestMessages.Group(senderId: 10001, sentAt: T.MinusMinutes(1));
        Assert.Equal("只能撤回自己发送的消息",
            Assert.Throws<DomainException>(() => other.Recall(10002, T.Now)).Message);
    }

    [Fact]
    public void PublicMessageId_规则与私聊一致()
    {
        Assert.Equal("gmid-1", TestMessages.Group(id: 88, clientMessageId: "gmid-1").PublicMessageId);
        Assert.Equal("88", TestMessages.Group(id: 88, clientMessageId: null).PublicMessageId);
    }
}

public class SessionSettingTests
{
    [Fact]
    public void Create_按用户与会话维度记录()
    {
        var setting = SessionSetting.Create(
            userId: 10001, ChatSessionType.Group, sessionId: 5,
            isPinned: true, muted: false, T.Now);

        Assert.Equal(10001, setting.UserId);
        Assert.Equal("group", setting.SessionType);
        Assert.Equal(ChatSessionType.Group, setting.Type);
        Assert.Equal(5, setting.SessionId);
        Assert.True(setting.IsPinned);
        Assert.False(setting.Muted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_会话ID非法时抛出(long sessionId)
    {
        var ex = Assert.Throws<DomainException>(() => SessionSetting.Create(
            10001, ChatSessionType.Private, sessionId, false, false, T.Now));

        Assert.Equal("无效的会话 ID", ex.Message);
    }

    [Fact]
    public void Update_只改传入的项()
    {
        var setting = SessionSetting.Create(
            10001, ChatSessionType.Group, 5, isPinned: true, muted: true, T.Now);

        setting.Update(isPinned: false, muted: null, T.PlusMinutes(1));

        Assert.False(setting.IsPinned);
        Assert.True(setting.Muted);                       // 未传的项保持原值
        Assert.Equal(T.PlusMinutes(1), setting.UpdatedAt);
    }

    [Fact]
    public void Update_两项都不传时抛出()
    {
        var setting = SessionSetting.Create(10001, ChatSessionType.Group, 5, false, false, T.Now);

        var ex = Assert.Throws<DomainException>(() => setting.Update(null, null, T.Now));

        Assert.Equal("没有需要更新的设置", ex.Message);
    }
}

internal static class TestMessages
{
    internal static PrivateMessage Private(
        long id = 1,
        int senderId = 10001,
        int receiverId = 10002,
        string? clientMessageId = null,
        DateTime? sentAt = null)
    {
        var message = PrivateMessage.Send(
            senderId, receiverId, "内容", MessageKind.Text,
            clientMessageId, null, sentAt ?? T.Now);
        message.AssignPersistedId(id);
        return message;
    }

    internal static GroupMessage Group(
        long id = 1,
        long groupId = 5,
        int senderId = 10001,
        string? clientMessageId = null,
        DateTime? sentAt = null)
    {
        var message = GroupMessage.Send(
            groupId, senderId, "内容", MessageKind.Text,
            clientMessageId, MentionList.Empty, null, sentAt ?? T.Now);
        message.AssignPersistedId(id);
        return message;
    }
}
