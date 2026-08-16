using System.Text.Json;
using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Models.Entities;
using LHZ.WebSocket.Interfaces;

namespace LHZ.OnlineChat.Server.Services;

/// <summary>
/// WebSocket 消息处理器
/// 根据消息类型进行路由分发
/// </summary>
public class WsMessageHandler
{
    private readonly WsConnectionManager _connectionManager;
    private readonly IFreeSql _fsql;
    private readonly RedisService _redis;

    public WsMessageHandler(WsConnectionManager connectionManager, IFreeSql fsql, RedisService redis)
    {
        _connectionManager = connectionManager;
        _fsql = fsql;
        _redis = redis;
    }

    /// <summary>
    /// 处理收到的 WebSocket 消息
    /// </summary>
    public async Task HandleMessageAsync(IWebSocketClient sender, long userId, string rawMessage)
    {
        WsMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<WsMessage>(rawMessage);
        }
        catch
        {
            Console.WriteLine($"[WS] 消息解析失败: {rawMessage}");
            return;
        }

        if (message == null) return;

        message.From = userId.ToString();
        message.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        switch (message.Type)
        {
            case WsMessageType.PrivateMessage:
                await HandlePrivateMessageAsync(sender, userId, message);
                break;
            case WsMessageType.GroupMessage:
                await HandleGroupMessageAsync(sender, userId, message);
                break;
            case WsMessageType.Heartbeat:
                await HandleHeartbeatAsync(sender);
                break;
            case WsMessageType.Typing:
                await HandleTypingAsync(userId, message);
                break;
            case WsMessageType.ReadReceipt:
                await HandleReadReceiptAsync(message);
                break;
            default:
                Console.WriteLine($"[WS] 未知消息类型: {message.Type}");
                break;
        }
    }

    /// <summary>
    /// 处理私聊消息
    /// </summary>
    private async Task HandlePrivateMessageAsync(IWebSocketClient sender, long userId, WsMessage message)
    {
        if (!long.TryParse(message.To, out var receiverId)) return;

        // 保存到数据库
        var privateMsg = new PrivateMessage
        {
            SenderId = userId,
            ReceiverId = receiverId,
            Content = message.Content,
            MessageType = message.MessageType,
            IsRead = false,
            SentAt = DateTime.UtcNow
        };
        var msgId = await _fsql.Insert(privateMsg).ExecuteIdentityAsync();
        // 客户端已生成 messageId（用于乐观发送去重）时保留，否则用数据库ID
        if (string.IsNullOrWhiteSpace(message.MessageId))
            message.MessageId = msgId.ToString();

        // 写入 Redis 缓存（单聊：key 为较小ID:较大ID）
        var cacheKey = GetPrivateChatCacheKey(userId, receiverId);
        var msgJson = JsonSerializer.Serialize(message);
        await _redis.ListLeftPushAsync(cacheKey, msgJson);
        await _redis.ListTrimAsync(cacheKey, 0, 49); // 保留最近 50 条

        // 查询发送者信息
        var senderUser = await _fsql.Select<User>().Where(u => u.Id == userId).FirstAsync();
        message.SenderName = senderUser?.Nickname ?? "未知";
        message.SenderAvatar = senderUser?.Avatar;

        var responseJson = JsonSerializer.Serialize(message);

        // 如果接收者在线，直接转发
        var receiverClient = _connectionManager.GetConnection(receiverId);
        if (receiverClient != null && receiverClient.Status == LHZ.WebSocket.Enums.ClientStatus.Opend)
        {
            receiverClient.SendMessage(responseJson);
            Console.WriteLine($"[WS] 私聊消息: {userId} → {receiverId} (已送达)");
        }
        else
        {
            Console.WriteLine($"[WS] 私聊消息: {userId} → {receiverId} (离线，已存库)");
        }

        // 也给发送者回显
        if (sender.Status == LHZ.WebSocket.Enums.ClientStatus.Opend)
        {
            sender.SendMessage(responseJson);
        }
    }

    /// <summary>
    /// 处理群聊消息
    /// </summary>
    private async Task HandleGroupMessageAsync(IWebSocketClient sender, long userId, WsMessage message)
    {
        if (!long.TryParse(message.To, out var groupId)) return;

        // 验证发送者是否在群中
        var isMember = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == userId)
            .AnyAsync();

        if (!isMember) return;

        // 保存到数据库
        var groupMsg = new GroupMessage
        {
            GroupId = groupId,
            SenderId = userId,
            Content = message.Content,
            MessageType = message.MessageType,
            SentAt = DateTime.UtcNow
        };
        var msgId = await _fsql.Insert(groupMsg).ExecuteIdentityAsync();
        // 客户端已生成 messageId（用于乐观发送去重）时保留，否则用数据库ID
        if (string.IsNullOrWhiteSpace(message.MessageId))
            message.MessageId = msgId.ToString();

        // 写入 Redis 缓存
        var cacheKey = $"chat:group:{groupId}";
        var msgJson = JsonSerializer.Serialize(message);
        await _redis.ListLeftPushAsync(cacheKey, msgJson);
        await _redis.ListTrimAsync(cacheKey, 0, 49); // 保留最近 50 条

        // 查询发送者信息
        var senderUser = await _fsql.Select<User>().Where(u => u.Id == userId).FirstAsync();
        message.SenderName = senderUser?.Nickname ?? "未知";
        message.SenderAvatar = senderUser?.Avatar;

        var responseJson = JsonSerializer.Serialize(message);

        // 获取群所有成员
        var members = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId)
            .ToListAsync();

        // 向所有在线成员广播（除了发送者）
        foreach (var member in members)
        {
            if (member.UserId == userId) continue;

            var client = _connectionManager.GetConnection(member.UserId);
            if (client != null && client.Status == LHZ.WebSocket.Enums.ClientStatus.Opend)
            {
                client.SendMessage(responseJson);
            }
        }

        // 给发送者回显
        if (sender.Status == LHZ.WebSocket.Enums.ClientStatus.Opend)
        {
            sender.SendMessage(responseJson);
        }

        Console.WriteLine($"[WS] 群聊消息: {userId} → 群 {groupId} ({members.Count} 人)");
    }

    /// <summary>
    /// 处理心跳
    /// </summary>
    private Task HandleHeartbeatAsync(IWebSocketClient sender)
    {
        // 简单回复 pong（不做额外处理，连接管理在 WsConnectionManager 中）
        try
        {
            if (sender.Status == LHZ.WebSocket.Enums.ClientStatus.Opend)
            {
                sender.SendMessage("{\"type\":\"pong\"}");
            }
        }
        catch { }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 处理"正在输入"状态
    /// </summary>
    private Task HandleTypingAsync(long userId, WsMessage message)
    {
        if (!long.TryParse(message.To, out var targetId)) return Task.CompletedTask;

        var targetClient = _connectionManager.GetConnection(targetId);
        if (targetClient != null && targetClient.Status == LHZ.WebSocket.Enums.ClientStatus.Opend)
        {
            message.From = userId.ToString();
            message.SenderName = "";
            targetClient.SendMessage(JsonSerializer.Serialize(message));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 处理已读回执
    /// </summary>
    private async Task HandleReadReceiptAsync(WsMessage message)
    {
        if (long.TryParse(message.MessageId, out var msgId))
        {
            await _fsql.Update<PrivateMessage>()
                .Set(p => p.IsRead, true)
                .Where(p => p.Id == msgId)
                .ExecuteAffrowsAsync();
        }
    }

    /// <summary>
    /// 获取私聊 Redis 缓存 Key（较小ID:较大ID）
    /// </summary>
    private static string GetPrivateChatCacheKey(long userId1, long userId2)
    {
        var minId = Math.Min(userId1, userId2);
        var maxId = Math.Max(userId1, userId2);
        return $"chat:private:{minId}:{maxId}";
    }

    /// <summary>
    /// 通知该用户的在线好友：ta 已上线/下线
    /// </summary>
    public async Task NotifyFriendsStatusAsync(long userId, bool online)
    {
        var friendships = await _fsql.Select<Friend>()
            .Where(f => (f.UserId == userId || f.FriendId == userId) && f.Status == 1)
            .ToListAsync();

        var statusMsg = JsonSerializer.Serialize(new WsMessage
        {
            Type = WsMessageType.OnlineStatus,
            From = userId.ToString(),
            Content = online ? "online" : "offline"
        });

        foreach (var f in friendships)
        {
            var friendId = f.UserId == userId ? f.FriendId : f.UserId;
            var client = _connectionManager.GetConnection(friendId);
            if (client != null && client.Status == LHZ.WebSocket.Enums.ClientStatus.Opend)
            {
                client.SendMessage(statusMsg);
            }
        }
    }

    /// <summary>
    /// 通知目标用户收到新的好友申请
    /// </summary>
    public void NotifyFriendRequestAsync(long toUserId, long fromUserId)
    {
        var client = _connectionManager.GetConnection(toUserId);
        if (client != null && client.Status == LHZ.WebSocket.Enums.ClientStatus.Opend)
        {
            client.SendMessage(JsonSerializer.Serialize(new WsMessage
            {
                Type = WsMessageType.FriendRequest,
                From = fromUserId.ToString(),
                Content = "new_friend_request"
            }));
        }
    }
}
