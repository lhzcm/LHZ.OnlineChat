using System.Globalization;
using LHZ.FastJson;
using LHZ.OnlineChat.Application.Messaging.Commands;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Infrastructure.Realtime;
using LHZ.OnlineChat.Infrastructure.Tests.TestDoubles;
using LHZ.WebSocket.Enums;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LHZ.OnlineChat.Infrastructure.Tests.Realtime;

/// <summary>
/// 入站分发器：心跳路径、内容被拒回执、正常报文派发。
/// 它是"客户端能不能知道自己消息被拒"的唯一出口 —— 这条链路断了，用户看到的就是消息凭空消失。
/// </summary>
public class WsInboundDispatcherTests
{
    private const int Me = 10001;
    private const int Peer = 10002;

    private static (WsInboundDispatcher Dispatcher, RecordingSender Sender, FakeScopeFactory Scopes, FakePresenceStore Presence) Create()
    {
        var sender = new RecordingSender();
        var scopes = new FakeScopeFactory(sender);
        var presence = new FakePresenceStore();
        var dispatcher = new WsInboundDispatcher(
            scopes, presence, NullLogger<WsInboundDispatcher>.Instance);
        return (dispatcher, sender, scopes, presence);
    }

    private static string Raw(
        string type, string content, int messageType = 0, string messageId = "cmid-1", string to = "10002")
        => JsonConvert.Serialize(new WsMessage
        {
            Type = type,
            From = Me.ToString(CultureInfo.InvariantCulture),
            To = to,
            Content = content,
            MessageId = messageId,
            MessageType = messageType
        });

    private static WsMessage Parse(string json) => JsonConvert.Deserialize<WsMessage>(json)!;

    [Fact(DisplayName = "Dispatch_心跳直接回pong且不开作用域")]
    public async Task Dispatch_Heartbeat_RepliesPongWithoutScope()
    {
        var (dispatcher, sender, scopes, _) = Create();
        var client = new FakeWebSocketClient();

        await dispatcher.DispatchAsync(client, Me, Raw(WsMessageType.Heartbeat, ""));

        Assert.Equal(WsMessageType.Pong, Parse(Assert.Single(client.Sent)).Type);
        Assert.Equal(0, scopes.Created);
        Assert.Empty(sender.Sent);
    }

    [Fact(DisplayName = "Dispatch_心跳会续期在线状态")]
    public async Task Dispatch_Heartbeat_RefreshesPresence()
    {
        var (dispatcher, _, _, presence) = Create();

        await dispatcher.DispatchAsync(new FakeWebSocketClient(), Me, Raw(WsMessageType.Heartbeat, ""));

        Assert.Equal(new[] { Me }, presence.RefreshCalls);
    }

    [Fact(DisplayName = "Dispatch_超长内容回message_failed且不进用例")]
    public async Task Dispatch_TooLongContent_RepliesMessageFailedWithoutDispatch()
    {
        var (dispatcher, sender, _, _) = Create();
        var client = new FakeWebSocketClient();
        var tooLong = new string('x', MessageContentRules.MaxLength + 1);

        await dispatcher.DispatchAsync(
            client, Me, Raw(WsMessageType.PrivateMessage, tooLong, messageId: "cmid-9"));

        var frame = Parse(Assert.Single(client.Sent));
        Assert.Equal(WsMessageType.MessageFailed, frame.Type);
        Assert.Equal("cmid-9", frame.MessageId);                       // 客户端靠它定位气泡
        Assert.Contains(MessageContentRules.MaxLength.ToString(CultureInfo.InvariantCulture), frame.Content, StringComparison.Ordinal);
        Assert.Empty(sender.Sent);                                      // 没有落库/推送
    }

    [Fact(DisplayName = "Dispatch_群消息超长同样回message_failed")]
    public async Task Dispatch_GroupMessageTooLong_RepliesMessageFailed()
    {
        var (dispatcher, sender, _, _) = Create();
        var client = new FakeWebSocketClient();

        await dispatcher.DispatchAsync(
            client, Me,
            Raw(WsMessageType.GroupMessage, new string('x', MessageContentRules.MaxLength + 1),
                messageId: "cmid-g", to: "5"));

        Assert.Equal(WsMessageType.MessageFailed, Parse(Assert.Single(client.Sent)).Type);
        Assert.Empty(sender.Sent);
    }

    [Fact(DisplayName = "Dispatch_未定义的消息类型回message_failed")]
    public async Task Dispatch_UndefinedMessageKind_RepliesMessageFailed()
    {
        var (dispatcher, sender, _, _) = Create();
        var client = new FakeWebSocketClient();

        await dispatcher.DispatchAsync(
            client, Me, Raw(WsMessageType.PrivateMessage, "内容", messageType: 99));

        var frame = Parse(Assert.Single(client.Sent));
        Assert.Equal(WsMessageType.MessageFailed, frame.Type);
        Assert.Equal("不支持的消息类型", frame.Content);
        Assert.Empty(sender.Sent);
    }

    [Fact(DisplayName = "Dispatch_连接已关闭时不回执但也不抛")]
    public async Task Dispatch_ClosedClient_DoesNotSendFailureFrame()
    {
        var (dispatcher, sender, _, _) = Create();
        var client = new FakeWebSocketClient { Status = ClientStatus.Close };

        await dispatcher.DispatchAsync(
            client, Me, Raw(WsMessageType.PrivateMessage, new string('x', MessageContentRules.MaxLength + 1)));

        Assert.Empty(client.Sent);
        Assert.Empty(sender.Sent);
    }

    [Fact(DisplayName = "Dispatch_合法私聊消息派发成用例并带上类型与客户端消息号")]
    public async Task Dispatch_ValidPrivateMessage_DispatchesCommand()
    {
        var (dispatcher, sender, _, _) = Create();
        var client = new FakeWebSocketClient();

        await dispatcher.DispatchAsync(
            client, Me,
            Raw(WsMessageType.PrivateMessage, "# 标题", (int)MessageKind.Markdown, "cmid-3"));

        var command = Assert.IsType<SendPrivateMessageCommand>(Assert.Single(sender.Sent));
        Assert.Equal(Me, command.SenderId);                 // 操作者来自链路，不信报文
        Assert.Equal(Peer, command.ReceiverId);
        Assert.Equal(MessageKind.Markdown, command.Kind);
        Assert.Equal("cmid-3", command.ClientMessageId);
        Assert.Empty(client.Sent);
    }

    [Fact(DisplayName = "Dispatch_合法群消息派发成用例并带上提及列表")]
    public async Task Dispatch_ValidGroupMessage_DispatchesCommand()
    {
        var (dispatcher, sender, _, _) = Create();
        var frame = new WsMessage
        {
            Type = WsMessageType.GroupMessage,
            From = Me.ToString(CultureInfo.InvariantCulture),
            To = "5",
            Content = "@小明 在吗",
            MessageId = "cmid-4",
            Mentions = new List<int> { 10003 }
        };

        await dispatcher.DispatchAsync(new FakeWebSocketClient(), Me, JsonConvert.Serialize(frame));

        var command = Assert.IsType<SendGroupMessageCommand>(Assert.Single(sender.Sent));
        Assert.Equal(5, command.GroupId);
        Assert.Equal(new[] { 10003 }, command.Mentions);
    }

    [Theory(DisplayName = "Dispatch_非法报文不崩连接也不回帧")]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    public async Task Dispatch_MalformedPayload_DoesNotThrowOrReply(string raw)
    {
        var (dispatcher, sender, _, _) = Create();
        var client = new FakeWebSocketClient();

        await dispatcher.DispatchAsync(client, Me, raw);

        Assert.Empty(client.Sent);
        Assert.Empty(sender.Sent);
    }

    [Fact(DisplayName = "Dispatch_未知类型静默忽略")]
    public async Task Dispatch_UnknownType_IsIgnored()
    {
        var (dispatcher, sender, _, _) = Create();
        var client = new FakeWebSocketClient();

        await dispatcher.DispatchAsync(client, Me, Raw("some_unknown_type", "x"));

        Assert.Empty(client.Sent);
        Assert.Empty(sender.Sent);
    }

    [Fact(DisplayName = "Dispatch_接收者ID非法时不派发用例")]
    public async Task Dispatch_InvalidReceiver_DoesNotDispatch()
    {
        var (dispatcher, sender, _, _) = Create();

        await dispatcher.DispatchAsync(
            new FakeWebSocketClient(), Me, Raw(WsMessageType.PrivateMessage, "hi", to: "abc"));

        Assert.Empty(sender.Sent);
    }
}
