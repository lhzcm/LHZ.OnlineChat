using System.Globalization;
using LHZ.FastJson;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;
using Microsoft.Extensions.Logging;

namespace LHZ.OnlineChat.Infrastructure.Realtime;

/// <summary>
/// 实时推送的唯一落地点：把应用层的业务意图翻译成 WS 协议报文并投递到各端。
///
/// 改造前这些逻辑散落在 WsMessageHandler（既处理入站又负责出站通知）、
/// BotService（自己又写了一遍落库+缓存+广播）、AdminService（自己拼 WsMessage 广播撤回）。
/// 现在协议封包只有这一份，多端广播规则也只有这一份。
/// </summary>
internal sealed class RealtimeNotifier : IRealtimeNotifier
{
    private readonly WsConnectionManager _connections;
    private readonly ILogger<RealtimeNotifier> _logger;

    public RealtimeNotifier(WsConnectionManager connections, ILogger<RealtimeNotifier> logger)
    {
        _connections = connections;
        _logger = logger;
    }

    // ==================== 好友 ====================

    public Task NotifyFriendRequestAsync(int toUserId, int fromUserId, CancellationToken ct = default)
    {
        SendSignal(toUserId, WsMessageType.FriendRequest, fromUserId);
        return Task.CompletedTask;
    }

    public Task NotifyFriendAcceptedAsync(int requesterId, int accepterId, CancellationToken ct = default)
    {
        // 双向通知：两边都要刷新好友列表
        SendSignal(requesterId, WsMessageType.FriendAccepted, accepterId);
        SendSignal(accepterId, WsMessageType.FriendAccepted, requesterId);
        return Task.CompletedTask;
    }

    public Task NotifyFriendRejectedAsync(int requesterId, int rejecterId, CancellationToken ct = default)
    {
        SendSignal(requesterId, WsMessageType.FriendRejected, rejecterId);
        return Task.CompletedTask;
    }

    public Task NotifyPresenceAsync(
        int userId, IReadOnlyList<int> friendIds, bool online, CancellationToken ct = default)
    {
        var payload = JsonConvert.Serialize(new WsMessage
        {
            Type = WsMessageType.OnlineStatus,
            From = Text(userId),
            Content = online ? "online" : "offline"
        });

        foreach (var friendId in friendIds)
        {
            _connections.Broadcast(friendId, payload);
        }

        return Task.CompletedTask;
    }

    public Task NotifyBlockedAsync(
        int toUserId, int fromUserId, string reason, CancellationToken ct = default)
    {
        var payload = JsonConvert.Serialize(new WsMessage
        {
            Type = WsMessageType.Blocked,
            From = Text(fromUserId),
            To = Text(toUserId),
            Content = reason,
            Timestamp = NowUnixMilliseconds()
        });

        _connections.Broadcast(toUserId, payload);
        return Task.CompletedTask;
    }

    // ==================== 群组 ====================

    public Task NotifyGroupInvitedAsync(int toUserId, long groupId, CancellationToken ct = default)
    {
        // from 承载群 ID：与改造前的协议保持一致
        var payload = JsonConvert.Serialize(new WsMessage
        {
            Type = WsMessageType.GroupInvited,
            From = Text(groupId),
            Content = WsMessageType.GroupInvited
        });

        _connections.Broadcast(toUserId, payload);
        return Task.CompletedTask;
    }

    public Task NotifyGroupDissolvedAsync(
        IReadOnlyList<int> memberIds, long groupId, string groupName, CancellationToken ct = default)
    {
        var payload = JsonConvert.Serialize(new WsMessage
        {
            Type = WsMessageType.GroupDissolved,
            From = Text(groupId),
            To = Text(groupId),
            Content = $"群「{groupName}」已被解散",
            Timestamp = NowUnixMilliseconds()
        });

        foreach (var memberId in memberIds)
        {
            _connections.Broadcast(memberId, payload);
        }

        return Task.CompletedTask;
    }

    // ==================== 消息 ====================

    public Task PushPrivateMessageAsync(RealtimeMessage message, CancellationToken ct = default)
    {
        var payload = JsonConvert.Serialize(ToWsMessage(message));
        var receiverId = (int)message.TargetId;

        var delivered = _connections.Broadcast(receiverId, payload);
        // 回显给发送者的其他设备（多端同步：自己发的消息其他端也要看到）
        _connections.Broadcast(message.SenderId, payload);

        _logger.LogInformation(
            "私聊消息 {SenderId} → {ReceiverId}（{State}）",
            message.SenderId, receiverId, delivered > 0 ? $"已送达 {delivered} 端" : "离线，已存库");

        return Task.CompletedTask;
    }

    public Task PushGroupMessageAsync(
        RealtimeMessage message, IReadOnlyList<int> memberIds, CancellationToken ct = default)
    {
        var payload = JsonConvert.Serialize(ToWsMessage(message));

        foreach (var memberId in memberIds)
        {
            _connections.Broadcast(memberId, payload);
        }

        // 发送者可能已不在成员列表里（如机器人被移出后的补发），确保回显
        if (!memberIds.Contains(message.SenderId))
            _connections.Broadcast(message.SenderId, payload);

        _logger.LogInformation(
            "群消息 {SenderId} → 群 {GroupId}（{Count} 人）",
            message.SenderId, message.TargetId, memberIds.Count);

        return Task.CompletedTask;
    }

    public Task PushGroupBacklogAsync(
        int toUserId, IReadOnlyList<RealtimeMessage> messages, CancellationToken ct = default)
    {
        // 补发只推一份到任一连接（与改造前一致，避免多端重复刷历史）
        var client = _connections.GetAnyConnection(toUserId);
        if (client is null) return Task.CompletedTask;

        foreach (var message in messages)
        {
            try
            {
                client.SendMessage(JsonConvert.Serialize(ToWsMessage(message)));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "补发群消息失败，用户 {UserId}", toUserId);
                break;
            }
        }

        return Task.CompletedTask;
    }

    public Task NotifyMessageRecalledAsync(
        MessageRecalled recalled, IReadOnlyList<int> audienceUserIds, CancellationToken ct = default)
    {
        var to = recalled.SessionType == ChatSessionType.Group
            ? Text(recalled.GroupId ?? 0)
            : Text(recalled.PeerUserId ?? 0);

        var payload = JsonConvert.Serialize(new WsMessage
        {
            Type = WsMessageType.MessageRecalled,
            From = Text(recalled.SenderId),
            To = to,
            Content = recalled.PublicMessageId,
            MessageId = recalled.PublicMessageId,
            Timestamp = UtcTime.ToUnixMilliseconds(recalled.OccurredAt)
        });

        foreach (var userId in audienceUserIds)
        {
            _connections.Broadcast(userId, payload);
        }

        return Task.CompletedTask;
    }

    public Task ForwardTypingAsync(int fromUserId, int toUserId, CancellationToken ct = default)
    {
        var payload = JsonConvert.Serialize(new WsMessage
        {
            Type = WsMessageType.Typing,
            From = Text(fromUserId),
            To = Text(toUserId),
            Timestamp = NowUnixMilliseconds()
        });

        _connections.Broadcast(toUserId, payload);
        return Task.CompletedTask;
    }

    public Task ForwardReadReceiptAsync(
        int readerId, int toUserId, string? messageId, CancellationToken ct = default)
    {
        var payload = JsonConvert.Serialize(new WsMessage
        {
            Type = WsMessageType.ReadReceipt,
            From = Text(readerId),
            To = Text(toUserId),
            MessageId = messageId ?? string.Empty,
            Timestamp = NowUnixMilliseconds()
        });

        _connections.Broadcast(toUserId, payload);
        return Task.CompletedTask;
    }

    public Task NotifyMutedAsync(
        int userId, long groupId, string reason, string? messageId, CancellationToken ct = default)
    {
        var payload = JsonConvert.Serialize(new WsMessage
        {
            Type = WsMessageType.Muted,
            From = Text(userId),
            To = Text(groupId),
            Content = reason,
            MessageId = messageId ?? string.Empty,
            Timestamp = NowUnixMilliseconds()
        });

        _connections.Broadcast(userId, payload);
        _logger.LogInformation("群消息被拒：用户 {UserId} 在群 {GroupId} 处于禁言中", userId, groupId);
        return Task.CompletedTask;
    }

    public Task NotifyMessageBlockedAsync(
        int senderId, int receiverId, string reason, string? messageId, CancellationToken ct = default)
    {
        var payload = JsonConvert.Serialize(new WsMessage
        {
            Type = WsMessageType.Blocked,
            From = Text(receiverId),
            To = Text(senderId),
            Content = reason,
            MessageId = messageId ?? string.Empty,
            Timestamp = NowUnixMilliseconds()
        });

        _connections.Broadcast(senderId, payload);
        _logger.LogInformation("私聊被拦截：{SenderId} → {ReceiverId}（对方已拉黑）", senderId, receiverId);
        return Task.CompletedTask;
    }

    // ==================== 内部 ====================

    /// <summary>只携带类型与上下文 ID 的轻量信号（客户端收到后自行拉取数据）</summary>
    private void SendSignal(int toUserId, string type, int contextId)
    {
        var payload = JsonConvert.Serialize(new WsMessage
        {
            Type = type,
            From = Text(contextId),
            Content = type
        });

        _connections.Broadcast(toUserId, payload);
    }

    private static WsMessage ToWsMessage(RealtimeMessage message) => new()
    {
        Type = message.SessionType == ChatSessionType.Private
            ? WsMessageType.PrivateMessage
            : WsMessageType.GroupMessage,
        From = Text(message.SenderId),
        To = Text(message.TargetId),
        Content = message.Content,
        MessageType = (int)message.Kind,
        MessageId = message.MessageId,
        SenderName = message.SenderName,
        SenderAvatar = message.SenderAvatar,
        Timestamp = UtcTime.ToUnixMilliseconds(message.SentAt),
        Mentions = message.Mentions.UserIds.ToList(),
        ReplyTo = message.Reply?.MessageId ?? string.Empty,
        ReplyContent = message.Reply?.Preview ?? string.Empty,
        ReplySender = message.Reply?.SenderName ?? string.Empty
    };

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static long NowUnixMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
