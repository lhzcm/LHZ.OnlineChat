using System.Globalization;
using LHZ.FastJson;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Messaging.Commands;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.WebSocket.Enums;
using LHZ.WebSocket.Interfaces;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LHZ.OnlineChat.Infrastructure.Realtime;

/// <summary>
/// WebSocket 入站报文分发器：解析协议 → 派发用例。
///
/// 改造前的 WsMessageHandler 是个 596 行的大杂烩 —— 解析协议、写数据库、查黑名单、
/// 拼缓存键、广播、触发机器人全在里面。现在这里只剩「解析 + 路由」，
/// 每种消息类型对应一个 MediatR 命令，业务逻辑一行都不在这。
///
/// 每条入站消息开独立 DI 作用域：WS 连接是长生命周期的，
/// 不能复用握手时的请求作用域（那个作用域早已结束）。
/// </summary>
internal sealed class WsInboundDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPresenceStore _presence;
    private readonly ILogger<WsInboundDispatcher> _logger;

    public WsInboundDispatcher(
        IServiceScopeFactory scopeFactory,
        IPresenceStore presence,
        ILogger<WsInboundDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _presence = presence;
        _logger = logger;
    }

    public async Task DispatchAsync(IWebSocketClient sender, int userId, string rawMessage)
    {
        WsMessage? message;
        try
        {
            message = JsonConvert.Deserialize<WsMessage>(rawMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WS 报文解析失败");
            return;
        }

        if (message is null) return;

        // 心跳不需要开作用域（IPresenceStore 是单例，可直接用）
        if (message.Type == WsMessageType.Heartbeat)
        {
            Pong(sender);
            await RefreshPresenceAsync(userId).ConfigureAwait(false);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

            switch (message.Type)
            {
                case WsMessageType.PrivateMessage:
                    await HandlePrivateMessageAsync(mediator, userId, message).ConfigureAwait(false);
                    break;

                case WsMessageType.GroupMessage:
                    await HandleGroupMessageAsync(mediator, userId, message).ConfigureAwait(false);
                    break;

                case WsMessageType.Typing:
                    await HandleTypingAsync(mediator, userId, message).ConfigureAwait(false);
                    break;

                case WsMessageType.ReadReceipt:
                    await HandleReadReceiptAsync(mediator, userId, message).ConfigureAwait(false);
                    break;

                case WsMessageType.MessageRecalled:
                    await HandleRecallAsync(mediator, userId, message).ConfigureAwait(false);
                    break;

                default:
                    _logger.LogDebug("未知 WS 消息类型：{Type}", message.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            // 入站处理失败不能让连接崩掉
            _logger.LogError(ex, "处理 WS 报文失败（用户 {UserId}，类型 {Type}）", userId, message.Type);
        }
    }

    private static async Task HandlePrivateMessageAsync(ISender mediator, int userId, WsMessage message)
    {
        if (!TryParseInt(message.To, out var receiverId)) return;

        await mediator.Send(new SendPrivateMessageCommand
        {
            SenderId = userId,
            ReceiverId = receiverId,
            Content = message.Content,
            Kind = (MessageKind)message.MessageType,
            ClientMessageId = NullIfBlank(message.MessageId),
            ReplyToMessageId = NullIfBlank(message.ReplyTo),
            ReplyPreview = NullIfBlank(message.ReplyContent),
            ReplySenderName = NullIfBlank(message.ReplySender)
        }).ConfigureAwait(false);
    }

    private static async Task HandleGroupMessageAsync(ISender mediator, int userId, WsMessage message)
    {
        if (!TryParseLong(message.To, out var groupId)) return;

        await mediator.Send(new SendGroupMessageCommand
        {
            SenderId = userId,
            GroupId = groupId,
            Content = message.Content,
            Kind = (MessageKind)message.MessageType,
            ClientMessageId = NullIfBlank(message.MessageId),
            Mentions = message.Mentions ?? new List<int>(),
            ReplyToMessageId = NullIfBlank(message.ReplyTo),
            ReplyPreview = NullIfBlank(message.ReplyContent),
            ReplySenderName = NullIfBlank(message.ReplySender)
        }).ConfigureAwait(false);
    }

    private static async Task HandleTypingAsync(ISender mediator, int userId, WsMessage message)
    {
        if (!TryParseInt(message.To, out var targetId)) return;

        await mediator.Send(new SendTypingCommand { FromUserId = userId, ToUserId = targetId })
            .ConfigureAwait(false);
    }

    private static async Task HandleReadReceiptAsync(ISender mediator, int readerId, WsMessage message)
    {
        // to 可能缺失（旧客户端只标记已读、不要求转发回执），此时 targetId=0，用例会跳过转发
        _ = TryParseInt(message.To, out var targetId);

        await mediator.Send(new SendReadReceiptCommand
        {
            ReaderId = readerId,
            TargetUserId = targetId,
            MessageId = NullIfBlank(message.MessageId)
        }).ConfigureAwait(false);
    }

    /// <summary>撤回：content 承载待撤回的 messageId，to 是会话对端（用户或群）</summary>
    private static async Task HandleRecallAsync(ISender mediator, int userId, WsMessage message)
    {
        var targetMessageId = message.Content?.Trim();
        if (string.IsNullOrWhiteSpace(targetMessageId)) return;
        if (!TryParseLong(message.To, out var targetId)) return;

        await mediator.Send(new RecallMessageCommand
        {
            OperatorId = userId,
            TargetId = targetId,
            MessageId = targetMessageId
        }).ConfigureAwait(false);
    }

    private void Pong(IWebSocketClient sender)
    {
        try
        {
            if (sender.Status != ClientStatus.Opened) return;
            sender.SendMessage(JsonConvert.Serialize(new WsMessage { Type = WsMessageType.Pong }));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "心跳回复失败");
        }
    }

    /// <summary>
    /// 用心跳续期在线状态标记。
    ///
    /// 在线键带 TTL 是为了兜住「进程被 kill、来不及跑下线逻辑」的情况 ——
    /// 否则那个用户会永远显示在线。续期失败不影响心跳回复本身，
    /// 所以这里只记 Debug：Redis 抖动不该让客户端反复重连。
    /// </summary>
    private async Task RefreshPresenceAsync(int userId)
    {
        try
        {
            await _presence.RefreshAsync(userId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "在线状态续期失败（用户 {UserId}）", userId);
        }
    }

    private static bool TryParseInt(string? raw, out int value)
        => int.TryParse(raw, CultureInfo.InvariantCulture, out value);

    private static bool TryParseLong(string? raw, out long value)
        => long.TryParse(raw, CultureInfo.InvariantCulture, out value);

    private static string? NullIfBlank(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw;
}
