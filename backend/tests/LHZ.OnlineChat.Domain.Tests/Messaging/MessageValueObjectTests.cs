using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Domain.Tests.Messaging;

public class MentionListTests
{
    [Fact]
    public void Empty_为空且序列化成null()
    {
        var empty = MentionList.Empty;

        Assert.True(empty.IsEmpty);
        Assert.Empty(empty.UserIds);
        Assert.Null(empty.ToStorage());
    }

    [Fact]
    public void From_去重并剔除非法ID()
    {
        var list = MentionList.From(new[] { 10001, 10002, 10001, 0, -5 });

        Assert.Equal(new[] { 10001, 10002 }, list.UserIds);
    }

    [Fact]
    public void From_null或空集合返回Empty()
    {
        Assert.True(MentionList.From(null).IsEmpty);
        Assert.True(MentionList.From(Array.Empty<int>()).IsEmpty);
        Assert.True(MentionList.From(new[] { 0, -1 }).IsEmpty);
    }

    [Theory]
    [InlineData("10000,10002", new[] { 10000, 10002 })]
    [InlineData(" 10000 , 10002 ", new[] { 10000, 10002 })]   // 容忍空白
    [InlineData("10000,,10002", new[] { 10000, 10002 })]      // 容忍空段
    [InlineData("10000,abc,10002", new[] { 10000, 10002 })]   // 跳过非数字
    [InlineData("10000,10000", new[] { 10000 })]              // 去重
    public void Parse_解析库里的逗号分隔字符串(string raw, int[] expected)
    {
        Assert.Equal(expected, MentionList.Parse(raw).UserIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("0,-1")]
    public void Parse_无有效ID时返回Empty(string? raw)
    {
        Assert.True(MentionList.Parse(raw).IsEmpty);
    }

    [Fact]
    public void ToStorage与Parse互为逆运算()
    {
        // 改造前 ParseMentions 在 MessageService 和 WsMessageHandler 各写了一份，
        // 现在只有一份实现，往返一致性可以被测到
        var original = MentionList.From(new[] { 10000, 10002, 10005 });

        var roundTripped = MentionList.Parse(original.ToStorage());

        Assert.Equal(original.UserIds, roundTripped.UserIds);
        Assert.Equal(original, roundTripped);
    }

    [Theory]
    [InlineData(10001, true)]
    [InlineData(10003, false)]
    public void Mentions_判断是否提及某人(int userId, bool expected)
    {
        var list = MentionList.From(new[] { 10001, 10002 });

        Assert.Equal(expected, list.Mentions(userId));
    }

    [Fact]
    public void 值相等语义_顺序相同才相等()
    {
        Assert.Equal(MentionList.From(new[] { 1, 2 }), MentionList.From(new[] { 1, 2 }));
        Assert.NotEqual(MentionList.From(new[] { 1, 2 }), MentionList.From(new[] { 2, 1 }));
    }
}

public class MessageReplyTests
{
    [Fact]
    public void Create_正常构造()
    {
        var reply = MessageReply.Create("msg-1", "原文预览", "张三");

        Assert.NotNull(reply);
        Assert.Equal("msg-1", reply!.MessageId);
        Assert.Equal("原文预览", reply.Preview);
        Assert.Equal("张三", reply.SenderName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_被引用ID为空时返回null表示不是引用回复(string? messageId)
    {
        Assert.Null(MessageReply.Create(messageId, "预览", "张三"));
    }

    [Fact]
    public void Create_去除首尾空白()
    {
        var reply = MessageReply.Create("  msg-1  ", "  预览 ", "  张三 ");

        Assert.Equal("msg-1", reply!.MessageId);
        Assert.Equal("预览", reply.Preview);
        Assert.Equal("张三", reply.SenderName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_预览与昵称空白时归为null(string? blank)
    {
        var reply = MessageReply.Create("msg-1", blank, blank);

        Assert.Null(reply!.Preview);
        Assert.Null(reply.SenderName);
    }

    [Fact]
    public void Create_预览超长时截断而非抛出()
    {
        // 引用预览是展示用的附属信息，截断比让整条消息发不出去更合理
        var reply = MessageReply.Create(
            "msg-1", new string('长', MessageReply.MaxPreviewLength + 50), "张三");

        Assert.Equal(MessageReply.MaxPreviewLength, reply!.Preview!.Length);
    }

    [Fact]
    public void 值相等语义()
    {
        Assert.Equal(
            MessageReply.Create("m", "p", "s"),
            MessageReply.Create("m", "p", "s"));
        Assert.NotEqual(
            MessageReply.Create("m", "p", "s"),
            MessageReply.Create("m", "p", "other"));
    }
}

public class RecallPolicyTests
{
    [Fact]
    public void Window_为2分钟()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), RecallPolicy.Window);
    }

    [Fact]
    public void EarliestSentAt_为当前时刻往前推一个时间窗()
    {
        Assert.Equal(T.MinusMinutes(2), RecallPolicy.EarliestSentAt(T.Now));
    }

    [Theory]
    [InlineData(0, true)]      // 刚发出
    [InlineData(-119, true)]   // 窗口内
    [InlineData(-120, true)]   // 边界：恰好 2 分钟仍可撤回
    [InlineData(-121, false)]  // 超窗
    [InlineData(-600, false)]
    public void IsWithinWindow_按发送时间判定(int secondsAgo, bool expected)
    {
        var sentAt = T.Now.AddSeconds(secondsAgo);

        Assert.Equal(expected, RecallPolicy.IsWithinWindow(sentAt, T.Now));
    }

    [Fact]
    public void IsWithinWindow_数据库读出的Unspecified时间也能正确判定()
    {
        // 库里读出的 Kind 是 Unspecified，若不归一化会被当作本地时间，判定结果全错
        var fromDatabase = DateTime.SpecifyKind(T.MinusMinutes(1), DateTimeKind.Unspecified);

        Assert.True(RecallPolicy.IsWithinWindow(fromDatabase, T.Now));
    }
}

public class ChatSessionTypeTests
{
    [Theory]
    [InlineData(ChatSessionType.Private, "private")]
    [InlineData(ChatSessionType.Group, "group")]
    public void ToStorage_映射为库里的字符串(ChatSessionType type, string expected)
    {
        Assert.Equal(expected, type.ToStorage());
    }

    [Theory]
    [InlineData("private", ChatSessionType.Private)]
    [InlineData("group", ChatSessionType.Group)]
    public void Parse_解析合法值(string raw, ChatSessionType expected)
    {
        Assert.Equal(expected, ChatSessionTypeNames.Parse(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("Private")]   // 大小写敏感：协议值是小写
    public void Parse_非法值时抛出(string? raw)
    {
        Assert.Equal("无效的会话类型",
            Assert.Throws<DomainException>(() => ChatSessionTypeNames.Parse(raw)).Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bogus")]
    public void TryParse_非法值时返回null(string? raw)
    {
        Assert.Null(ChatSessionTypeNames.TryParse(raw));
    }

    [Fact]
    public void 往返一致()
    {
        foreach (var type in new[] { ChatSessionType.Private, ChatSessionType.Group })
        {
            Assert.Equal(type, ChatSessionTypeNames.Parse(type.ToStorage()));
        }
    }
}
