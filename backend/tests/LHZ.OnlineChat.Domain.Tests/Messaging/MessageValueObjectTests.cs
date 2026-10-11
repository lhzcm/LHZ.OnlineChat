using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Domain.Tests.Messaging;

public class MentionListTests
{
    [Fact(DisplayName = "Empty_为空且序列化成null")]
    public void Empty_IsEmpty_AndSerializesToNull()
    {
        var empty = MentionList.Empty;

        Assert.True(empty.IsEmpty);
        Assert.Empty(empty.UserIds);
        Assert.Null(empty.ToStorage());
    }

    [Fact(DisplayName = "From_去重并剔除非法ID")]
    public void From_Deduplicates_AndRejectsInvalidIds()
    {
        var list = MentionList.From(new[] { 10001, 10002, 10001, 0, -5 });

        Assert.Equal(new[] { 10001, 10002 }, list.UserIds);
    }

    [Fact(DisplayName = "From_null或空集合返回Empty")]
    public void From_NullOrEmptyCollection_ReturnsEmpty()
    {
        Assert.True(MentionList.From(null).IsEmpty);
        Assert.True(MentionList.From(Array.Empty<int>()).IsEmpty);
        Assert.True(MentionList.From(new[] { 0, -1 }).IsEmpty);
    }

    [Theory(DisplayName = "Parse_解析库里的逗号分隔字符串")]
    [InlineData("10000,10002", new[] { 10000, 10002 })]
    [InlineData(" 10000 , 10002 ", new[] { 10000, 10002 })]   // 容忍空白
    [InlineData("10000,,10002", new[] { 10000, 10002 })]      // 容忍空段
    [InlineData("10000,abc,10002", new[] { 10000, 10002 })]   // 跳过非数字
    [InlineData("10000,10000", new[] { 10000 })]              // 去重
    public void Parse_CommaSeparatedStorageString_ParsesIds(string raw, int[] expected)
    {
        Assert.Equal(expected, MentionList.Parse(raw).UserIds);
    }

    [Theory(DisplayName = "Parse_无有效ID时返回Empty")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("0,-1")]
    public void Parse_NoValidIds_ReturnsEmpty(string? raw)
    {
        Assert.True(MentionList.Parse(raw).IsEmpty);
    }

    [Fact(DisplayName = "ToStorage与Parse互为逆运算")]
    public void ToStorage_ThenParse_RoundTrips()
    {
        // 改造前 ParseMentions 在 MessageService 和 WsMessageHandler 各写了一份，
        // 现在只有一份实现，往返一致性可以被测到
        var original = MentionList.From(new[] { 10000, 10002, 10005 });

        var roundTripped = MentionList.Parse(original.ToStorage());

        Assert.Equal(original.UserIds, roundTripped.UserIds);
        Assert.Equal(original, roundTripped);
    }

    [Theory(DisplayName = "Mentions_判断是否提及某人")]
    [InlineData(10001, true)]
    [InlineData(10003, false)]
    public void Mentions_GivenUser_ReturnsExpected(int userId, bool expected)
    {
        var list = MentionList.From(new[] { 10001, 10002 });

        Assert.Equal(expected, list.Mentions(userId));
    }

    [Fact(DisplayName = "值相等语义_顺序相同才相等")]
    public void ValueEquality_SameOrder_IsEqual()
    {
        Assert.Equal(MentionList.From(new[] { 1, 2 }), MentionList.From(new[] { 1, 2 }));
        Assert.NotEqual(MentionList.From(new[] { 1, 2 }), MentionList.From(new[] { 2, 1 }));
    }
}

public class MessageReplyTests
{
    [Fact(DisplayName = "Create_正常构造")]
    public void Create_ValidInput_BuildsReply()
    {
        var reply = MessageReply.Create("msg-1", "原文预览", "张三");

        Assert.NotNull(reply);
        Assert.Equal("msg-1", reply!.MessageId);
        Assert.Equal("原文预览", reply.Preview);
        Assert.Equal("张三", reply.SenderName);
    }

    [Theory(DisplayName = "Create_被引用ID为空时返回null表示不是引用回复")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankMessageId_ReturnsNull(string? messageId)
    {
        Assert.Null(MessageReply.Create(messageId, "预览", "张三"));
    }

    [Fact(DisplayName = "Create_去除首尾空白")]
    public void Create_SurroundingWhitespace_IsTrimmed()
    {
        var reply = MessageReply.Create("  msg-1  ", "  预览 ", "  张三 ");

        Assert.Equal("msg-1", reply!.MessageId);
        Assert.Equal("预览", reply.Preview);
        Assert.Equal("张三", reply.SenderName);
    }

    [Theory(DisplayName = "Create_预览与昵称空白时归为null")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankPreviewAndNickname_BecomeNull(string? blank)
    {
        var reply = MessageReply.Create("msg-1", blank, blank);

        Assert.Null(reply!.Preview);
        Assert.Null(reply.SenderName);
    }

    [Fact(DisplayName = "Create_预览超长时截断而非抛出")]
    public void Create_OverlongPreview_IsTruncated()
    {
        // 引用预览是展示用的附属信息，截断比让整条消息发不出去更合理
        var reply = MessageReply.Create(
            "msg-1", new string('长', MessageReply.MaxPreviewLength + 50), "张三");

        Assert.Equal(MessageReply.MaxPreviewLength, reply!.Preview!.Length);
    }

    [Fact(DisplayName = "值相等语义")]
    public void ValueEquality_DifferentFields_AreNotEqual()
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
    [Fact(DisplayName = "Window_为2分钟")]
    public void Window_Default_IsTwoMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), RecallPolicy.Window);
    }

    [Fact(DisplayName = "EarliestSentAt_为当前时刻往前推一个时间窗")]
    public void EarliestSentAt_IsOneWindowBeforeNow()
    {
        Assert.Equal(T.MinusMinutes(2), RecallPolicy.EarliestSentAt(T.Now));
    }

    [Theory(DisplayName = "IsWithinWindow_按发送时间判定")]
    [InlineData(0, true)]      // 刚发出
    [InlineData(-119, true)]   // 窗口内
    [InlineData(-120, true)]   // 边界：恰好 2 分钟仍可撤回
    [InlineData(-121, false)]  // 超窗
    [InlineData(-600, false)]
    public void IsWithinWindow_GivenSentAt_ReturnsExpected(int secondsAgo, bool expected)
    {
        var sentAt = T.Now.AddSeconds(secondsAgo);

        Assert.Equal(expected, RecallPolicy.IsWithinWindow(sentAt, T.Now));
    }

    [Fact(DisplayName = "IsWithinWindow_数据库读出的Unspecified时间也能正确判定")]
    public void IsWithinWindow_UnspecifiedKindFromDatabase_IsAccepted()
    {
        // 库里读出的 Kind 是 Unspecified，若不归一化会被当作本地时间，判定结果全错
        var fromDatabase = DateTime.SpecifyKind(T.MinusMinutes(1), DateTimeKind.Unspecified);

        Assert.True(RecallPolicy.IsWithinWindow(fromDatabase, T.Now));
    }
}

public class ChatSessionTypeTests
{
    [Theory(DisplayName = "ToStorage_映射为库里的字符串")]
    [InlineData(ChatSessionType.Private, "private")]
    [InlineData(ChatSessionType.Group, "group")]
    public void ToStorage_KnownType_MapsToString(ChatSessionType type, string expected)
    {
        Assert.Equal(expected, type.ToStorage());
    }

    [Theory(DisplayName = "Parse_解析合法值")]
    [InlineData("private", ChatSessionType.Private)]
    [InlineData("group", ChatSessionType.Group)]
    public void Parse_ValidValue_ReturnsType(string raw, ChatSessionType expected)
    {
        Assert.Equal(expected, ChatSessionTypeNames.Parse(raw));
    }

    [Theory(DisplayName = "Parse_非法值时抛出")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("Private")]   // 大小写敏感：协议值是小写
    public void Parse_InvalidValue_Throws(string? raw)
    {
        Assert.Equal("无效的会话类型",
            Assert.Throws<DomainException>(() => ChatSessionTypeNames.Parse(raw)).Message);
    }

    [Theory(DisplayName = "TryParse_非法值时返回null")]
    [InlineData(null)]
    [InlineData("bogus")]
    public void TryParse_InvalidValue_ReturnsNull(string? raw)
    {
        Assert.Null(ChatSessionTypeNames.TryParse(raw));
    }

    [Fact(DisplayName = "往返一致")]
    public void RoundTrip_StorageThenParse_IsConsistent()
    {
        foreach (var type in new[] { ChatSessionType.Private, ChatSessionType.Group })
        {
            Assert.Equal(type, ChatSessionTypeNames.Parse(type.ToStorage()));
        }
    }
}
