using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Blacklists;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Messaging.Commands;

/// <summary>
/// 发送私聊消息（WebSocket 入站路径）。
/// 返回的是内部结果而非 ApiResponse —— 这条链路不走 HTTP，
/// 被拉黑时要回一个 blocked 协议帧而不是 HTTP 400。
/// </summary>
public sealed class SendPrivateMessageCommand : ICommand<SendMessageResult>
{
    public int SenderId { get; set; }

    public int ReceiverId { get; set; }

    public string Content { get; set; } = string.Empty;

    public MessageKind Kind { get; set; } = MessageKind.Text;

    /// <summary>客户端生成的消息 ID（乐观发送去重）</summary>
    public string? ClientMessageId { get; set; }

    public string? ReplyToMessageId { get; set; }

    public string? ReplyPreview { get; set; }

    public string? ReplySenderName { get; set; }
}

/// <summary>消息发送结果</summary>
public sealed class SendMessageResult
{
    public bool Delivered { get; init; }

    /// <summary>被拒原因（拉黑/禁言）；成功时为 null</summary>
    public string? RejectionReason { get; init; }

    /// <summary>被禁言时的截止时间</summary>
    public DateTime? MutedUntil { get; init; }

    public static SendMessageResult Ok() => new() { Delivered = true };

    public static SendMessageResult Rejected(string reason) => new() { Delivered = false, RejectionReason = reason };

    public static SendMessageResult Muted(string reason, DateTime until)
        => new() { Delivered = false, RejectionReason = reason, MutedUntil = until };
}

internal sealed class SendPrivateMessageHandler : IRequestHandler<SendPrivateMessageCommand, SendMessageResult>
{
    private readonly IPrivateMessageRepository _messages;
    private readonly IBlacklistRepository _blacklist;
    private readonly IUserRepository _users;
    private readonly IRecentMessageCache _cache;
    private readonly IRealtimeNotifier _notifier;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public SendPrivateMessageHandler(
        IPrivateMessageRepository messages,
        IBlacklistRepository blacklist,
        IUserRepository users,
        IRecentMessageCache cache,
        IRealtimeNotifier notifier,
        IDomainEventDispatcher events,
        IClock clock)
    {
        _messages = messages;
        _blacklist = blacklist;
        _users = users;
        _cache = cache;
        _notifier = notifier;
        _events = events;
        _clock = clock;
    }

    public async Task<SendMessageResult> Handle(SendPrivateMessageCommand command, CancellationToken ct)
    {
        // 幂等：客户端重试同一条消息（网络抖动、乐观发送超时重发）时，
        // ClientMessageId 相同就直接当成功返回，不再插一条。
        // 之前只在前端按 messageId 去重，服务端照样插入重复行 —— 刷新一次就现形。
        if (!string.IsNullOrWhiteSpace(command.ClientMessageId))
        {
            var existing = await _messages
                .FindByClientMessageIdAsync(command.SenderId, command.ClientMessageId, ct)
                .ConfigureAwait(false);

            if (existing is not null) return SendMessageResult.Ok();
        }

        // 黑名单拦截：接收者拉黑了发送者
        var blocked = await _blacklist
            .IsBlockedByAsync(command.ReceiverId, command.SenderId, ct)
            .ConfigureAwait(false);
        if (blocked)
        {
            const string reason = "对方已将你拉黑，消息未发送";
            await _notifier
                .NotifyMessageBlockedAsync(command.SenderId, command.ReceiverId, reason, command.ClientMessageId, ct)
                .ConfigureAwait(false);
            return SendMessageResult.Rejected(reason);
        }

        var now = _clock.UtcNow;
        var reply = MessageReply.Create(command.ReplyToMessageId, command.ReplyPreview, command.ReplySenderName);

        var message = PrivateMessage.Send(
            command.SenderId, command.ReceiverId, command.Content, command.Kind,
            command.ClientMessageId, reply, now);

        await _messages.AddAsync(message, ct).ConfigureAwait(false);

        var sender = await _users.FindByIdAsync(command.SenderId, ct).ConfigureAwait(false);
        var realtime = new RealtimeMessage
        {
            SessionType = ChatSessionType.Private,
            SenderId = command.SenderId,
            TargetId = command.ReceiverId,
            Content = message.Content,
            Kind = message.Kind,
            MessageId = message.PublicMessageId,
            SenderName = sender?.Nickname ?? "未知",
            SenderAvatar = sender?.Avatar,
            SentAt = message.SentAtUtc,
            Reply = reply
        };

        await _cache
            .AppendAsync(ChatSessionType.Private, command.SenderId, command.ReceiverId, realtime, ct)
            .ConfigureAwait(false);

        // 推给双方全部在线设备（多端同步）
        await _notifier.PushPrivateMessageAsync(realtime, ct).ConfigureAwait(false);

        // 机器人触发挂在这个事件上，不再由消息处理器直接调 BotService
        await _events
            .DispatchAsync(new PrivateMessageSent(message.Id, command.SenderId, command.ReceiverId, now), ct)
            .ConfigureAwait(false);

        return SendMessageResult.Ok();
    }
}
