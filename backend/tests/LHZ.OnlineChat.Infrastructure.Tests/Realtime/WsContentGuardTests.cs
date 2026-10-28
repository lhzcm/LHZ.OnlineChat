using LHZ.FastJson;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Infrastructure.Realtime;

namespace LHZ.OnlineChat.Infrastructure.Tests.Realtime;

/// <summary>
/// 入站内容预校验：它决定「这次失败能不能直接回执给发送者」，
/// 所以必须与领域规则完全同口径（不一致就会出现"前端与服务端各说一套"）。
/// </summary>
public class WsContentGuardTests
{
    [Theory(DisplayName = "DescribeRejection_合法内容不返回原因")]
    [InlineData("你好", MessageKind.Text)]
    [InlineData("# 标题\n\n- 列表", MessageKind.Markdown)]
    [InlineData("/uploads/images/a.png", MessageKind.Image)]
    [InlineData("/uploads/voices/a.webm", MessageKind.Voice)]
    public void DescribeRejection_ValidContent_ReturnsNull(string content, MessageKind kind)
    {
        Assert.Null(WsContentGuard.DescribeRejection(content, (int)kind));
    }

    [Theory(DisplayName = "DescribeRejection_图片语音的外链被拒")]
    [InlineData("https://example.com/a.png", MessageKind.Image)]
    [InlineData("https://example.com/a.webm", MessageKind.Voice)]
    public void DescribeRejection_ExternalMediaUrl_ReturnsReason(string content, MessageKind kind)
    {
        // 原用例把外链图片当作合法：那是 WS 入口尚无站内地址约束时的口径，现已收紧
        Assert.Equal("图片、语音或文件地址不合法", WsContentGuard.DescribeRejection(content, (int)kind));
    }

    [Fact(DisplayName = "DescribeRejection_超过上限时返回原因并带上限数字")]
    public void DescribeRejection_TooLong_ReturnsReasonWithLimit()
    {
        var reason = WsContentGuard.DescribeRejection(
            new string('x', MessageContentRules.MaxLength + 1), (int)MessageKind.Text);

        Assert.NotNull(reason);
        Assert.Contains(
            MessageContentRules.MaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            reason,
            StringComparison.Ordinal);
    }

    [Fact(DisplayName = "DescribeRejection_恰好达到上限时通过")]
    public void DescribeRejection_AtLimit_ReturnsNull()
    {
        Assert.Null(WsContentGuard.DescribeRejection(
            new string('x', MessageContentRules.MaxLength), (int)MessageKind.Markdown));
    }

    [Theory(DisplayName = "DescribeRejection_空内容被拒")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DescribeRejection_EmptyContent_ReturnsReason(string? content)
    {
        Assert.Equal("消息内容不能为空", WsContentGuard.DescribeRejection(content, (int)MessageKind.Text));
    }

    [Theory(DisplayName = "DescribeRejection_未定义的消息类型被拒")]
    [InlineData(99)]
    [InlineData(-1)]
    public void DescribeRejection_UndefinedKind_ReturnsReason(int messageType)
    {
        Assert.Equal("不支持的消息类型", WsContentGuard.DescribeRejection("内容", messageType));
    }

    [Fact(DisplayName = "BuildFailureFrame_回执带上客户端消息ID与原因")]
    public void BuildFailureFrame_CarriesClientMessageIdAndReason()
    {
        var inbound = new WsMessage
        {
            Type = WsMessageType.PrivateMessage,
            From = "10001",
            To = "10002",
            MessageId = "cmid-1",
            MessageType = 3,
            Content = new string('x', MessageContentRules.MaxLength + 1)
        };

        var frame = WsContentGuard.BuildFailureFrame(inbound, "消息内容不能超过 20000 字");

        Assert.Equal(WsMessageType.MessageFailed, frame.Type);
        Assert.Equal("cmid-1", frame.MessageId);      // 客户端靠它把气泡标成「发送失败」
        Assert.Equal("10001", frame.From);
        Assert.Equal("10002", frame.To);
        Assert.Equal(3, frame.MessageType);
        Assert.Contains("20000", frame.Content, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BuildFailureFrame_序列化后仍是camelCase协议字段")]
    public void BuildFailureFrame_SerializesToCamelCaseProtocol()
    {
        var inbound = new WsMessage
        {
            Type = WsMessageType.GroupMessage, From = "10001", To = "5", MessageId = "cmid-2"
        };

        var json = JsonConvert.Serialize(WsContentGuard.BuildFailureFrame(inbound, "内容为空"));
        var parsed = JsonConvert.Deserialize<WsMessage>(json);

        Assert.NotNull(parsed);
        Assert.Equal("message_failed", parsed!.Type);
        Assert.Equal("cmid-2", parsed.MessageId);
        Assert.Equal("内容为空", parsed.Content);
    }
}
