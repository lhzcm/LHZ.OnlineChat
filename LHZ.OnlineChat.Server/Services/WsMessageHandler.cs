using LHZ.FastJson;
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
    private readonly BlacklistService _blacklistService;
    private readonly BotService _botService;

    public WsMessageHandler(WsConnectionManager connectionManager, IFreeSql fsql, RedisService redis,
        BlacklistService blacklistService, BotService botService)
    {
        _connectionManager = connectionManager;
        _fsql = fsql;
        _redis = redis;
        _blacklistService = blacklistService;
        _botService = botService;
    }

    /// <summary>
    /// 处理收到的 WebSocket 消息
    /// </summary>
    public async Task HandleMessageAsync(IWebSocketClient sender, int userId, string rawMessage)
    {
        WsMessage? message;
        try
        {
            message = JsonConvert.Deserialize<WsMessage>(rawMessage);
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
                await HandleReadReceiptAsync(userId, message);
                break;
            case WsMessageType.MessageRecalled:
                await HandleMessageRecalledAsync(userId, message);
                break;
            default:
                Console.WriteLine($"[WS] 未知消息类型: {message.Type}");
                break;
        }
    }

    /// <summary>
    /// 处理私聊消息
    /// </summary>
    private async Task HandlePrivateMessageAsync(IWebSocketClient sender, int userId, WsMessage message)
    {
        if (!int.TryParse(message.To, out var receiverId)) return;

        // 黑名单拦截：接收者拉黑了发送者时，拒绝并回执发送者
        if (await _blacklistService.IsBlockedByAsync(receiverId, userId))
        {
            sender.SendMessage(JsonConvert.Serialize(new WsMessage
            {
                Type = WsMessageType.Blocked,
                From = receiverId.ToString(),
                To = userId.ToString(),
                Content = "对方已将你拉黑，消息未发送",
                MessageId = message.MessageId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageType = 0,
                SenderName = string.Empty,
                SenderAvatar = null
            }));
            Console.WriteLine($"[WS] 私聊消息被拦截: {userId} → {receiverId} (对方已拉黑)");
            return;
        }

        // 保存到数据库
        var privateMsg = new PrivateMessage
        {
            SenderId = userId,
            ReceiverId = receiverId,
            Content = message.Content,
            MessageType = message.MessageType,
            IsRead = false,
            ClientMessageId = string.IsNullOrWhiteSpace(message.MessageId) ? null : message.MessageId,
            ReplyMessageId = string.IsNullOrWhiteSpace(message.ReplyTo) ? null : message.ReplyTo,
            ReplyContent = string.IsNullOrWhiteSpace(message.ReplyContent) ? null : message.ReplyContent,
            ReplySenderName = string.IsNullOrWhiteSpace(message.ReplySender) ? null : message.ReplySender,
            SentAt = DateTime.UtcNow
        };
        var msgId = await _fsql.Insert(privateMsg).ExecuteIdentityAsync();
        // 客户端已生成 messageId（用于乐观发送去重）时保留，否则用数据库ID
        if (string.IsNullOrWhiteSpace(message.MessageId))
            message.MessageId = msgId.ToString();

        // 写入 Redis 缓存（单聊：key 为较小ID:较大ID）
        var cacheKey = GetPrivateChatCacheKey(userId, receiverId);
        var msgJson = JsonConvert.Serialize(message);
        await _redis.ListLeftPushAsync(cacheKey, msgJson);
        await _redis.ListTrimAsync(cacheKey, 0, 49); // 保留最近 50 条

        // 查询发送者信息
        var senderUser = await _fsql.Select<User>().Where(u => u.Id == userId).FirstAsync();
        message.SenderName = senderUser?.Nickname ?? "未知";
        message.SenderAvatar = senderUser?.Avatar;

        var responseJson = JsonConvert.Serialize(message);

        // 如果接收者在线，转发到其所有在线设备（多端同步）
        foreach (var receiverClient in _connectionManager.GetConnections(receiverId))
        {
            receiverClient.SendMessage(responseJson);
        }
        if (_connectionManager.IsOnline(receiverId))
        {
            Console.WriteLine($"[WS] 私聊消息: {userId} → {receiverId} (已送达)");
        }
        else
        {
            Console.WriteLine($"[WS] 私聊消息: {userId} → {receiverId} (离线，已存库)");
        }

        // 回显给发送者的所有在线设备（多端同步：自己发的消息其他设备实时可见）
        foreach (var client in _connectionManager.GetConnections(userId))
        {
            client.SendMessage(responseJson);
        }

        // 机器人触发：接收者是启用中的机器人时，异步调度 Webhook（不阻塞消息处理）
        await _botService.TryDispatchPrivateAsync(userId, receiverId, message);
    }

    /// <summary>
    /// 处理群聊消息
    /// </summary>
    private async Task HandleGroupMessageAsync(IWebSocketClient sender, int userId, WsMessage message)
    {
        if (!int.TryParse(message.To, out var groupId)) return;

        // 验证发送者是否在群中（顺带取成员记录用于禁言校验）
        var senderMember = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == userId)
            .FirstAsync();

        if (senderMember == null) return;

        // 禁言校验：禁言截止时间未到则拒绝发送
        if (senderMember.MutedUntil.HasValue && senderMember.MutedUntil.Value > DateTime.UtcNow)
        {
            var untilLocal = TimeZoneInfo.ConvertTimeFromUtc(senderMember.MutedUntil.Value,
                TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"));
            var msg = new WsMessage
            {
                Type = WsMessageType.Muted,
                From = userId.ToString(),
                To = groupId.ToString(),
                Content = $"你已被禁言至 {untilLocal:MM-dd HH:mm}，期间无法在群里发言",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = message.MessageId ?? string.Empty,
                MessageType = 0,
                SenderName = string.Empty,
                SenderAvatar = null
            };
            sender.SendMessage(JsonConvert.Serialize(msg));
            Console.WriteLine($"[WS] 群消息被拒绝: {userId} → 群 {groupId}（禁言中）");
            return;
        }

        // 保存到数据库
        var groupMsg = new GroupMessage
        {
            GroupId = groupId,
            SenderId = userId,
            Content = message.Content,
            MessageType = message.MessageType,
            ClientMessageId = string.IsNullOrWhiteSpace(message.MessageId) ? null : message.MessageId,
            Mentions = message.Mentions?.Count > 0 ? string.Join(',', message.Mentions) : null,
            ReplyMessageId = string.IsNullOrWhiteSpace(message.ReplyTo) ? null : message.ReplyTo,
            ReplyContent = string.IsNullOrWhiteSpace(message.ReplyContent) ? null : message.ReplyContent,
            ReplySenderName = string.IsNullOrWhiteSpace(message.ReplySender) ? null : message.ReplySender,
            SentAt = DateTime.UtcNow
        };
        var msgId = await _fsql.Insert(groupMsg).ExecuteIdentityAsync();
        // 客户端已生成 messageId（用于乐观发送去重）时保留，否则用数据库ID
        if (string.IsNullOrWhiteSpace(message.MessageId))
            message.MessageId = msgId.ToString();

        // 写入 Redis 缓存
        var cacheKey = $"chat:group:{groupId}";
        var msgJson = JsonConvert.Serialize(message);
        await _redis.ListLeftPushAsync(cacheKey, msgJson);
        await _redis.ListTrimAsync(cacheKey, 0, 49); // 保留最近 50 条

        // 查询发送者信息
        var senderUser = await _fsql.Select<User>().Where(u => u.Id == userId).FirstAsync();
        message.SenderName = senderUser?.Nickname ?? "未知";
        message.SenderAvatar = senderUser?.Avatar;

        var responseJson = JsonConvert.Serialize(message);

        // 获取群所有成员
        var members = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId)
            .ToListAsync();

        // 向所有在线成员的所有设备广播（除了发送者）
        foreach (var member in members)
        {
            if (member.UserId == userId) continue;

            foreach (var client in _connectionManager.GetConnections(member.UserId))
            {
                client.SendMessage(responseJson);
            }
        }

        // 回显给发送者的所有在线设备（多端同步）
        foreach (var client in _connectionManager.GetConnections(userId))
        {
            client.SendMessage(responseJson);
        }

        Console.WriteLine($"[WS] 群聊消息: {userId} → 群 {groupId} ({members.Count} 人)");

        // 机器人触发：消息 @ 了群内启用中的机器人时，异步调度 Webhook
        await _botService.TryDispatchGroupAsync(userId, groupId, message);
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
    /// 处理"正在输入"状态（转发到对方所有在线设备）
    /// </summary>
    private Task HandleTypingAsync(int userId, WsMessage message)
    {
        if (!int.TryParse(message.To, out var targetId)) return Task.CompletedTask;

        message.From = userId.ToString();
        message.SenderName = "";
        var json = JsonConvert.Serialize(message);
        foreach (var client in _connectionManager.GetConnections(targetId))
        {
            client.SendMessage(json);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 处理已读回执：标记消息已读，并转发给被读方所有在线设备（通知对方该会话已读）
    /// </summary>
    private async Task HandleReadReceiptAsync(int readerId, WsMessage message)
    {
        // 单条消息标记已读（兼容旧协议）
        if (long.TryParse(message.MessageId, out var msgId))
        {
            await _fsql.Update<PrivateMessage>()
                .Set(p => p.IsRead, true)
                .Where(p => p.Id == msgId)
                .ExecuteAffrowsAsync();
        }

        // 转发回执给被读方（在线时），from 为已读用户
        if (int.TryParse(message.To, out var targetId))
        {
            var json = JsonConvert.Serialize(message);
            foreach (var client in _connectionManager.GetConnections(targetId))
            {
                client.SendMessage(json);
            }
        }
    }

    /// <summary>
    /// 撤回消息（限 2 分钟内、仅本人）：标记 IsDeleted，从缓存移除，并广播撤回通知
    /// </summary>
    private async Task HandleMessageRecalledAsync(int userId, WsMessage message)
    {
        var targetId = message.Content?.Trim();
        if (string.IsNullOrWhiteSpace(targetId) || !long.TryParse(message.To, out var toId))
            return;

        var withinTime = DateTime.UtcNow.AddMinutes(-2);
        long groupId = 0;
        int sessionPeer = 0;

        // 私聊撤回（to = 对方用户 ID）
        var pm = await _fsql.Select<PrivateMessage>()
            .Where(m => m.SenderId == userId && m.ReceiverId == toId && !m.IsDeleted &&
                m.SentAt >= withinTime &&
                (m.Id.ToString() == targetId || m.ClientMessageId == targetId))
            .FirstAsync();

        if (pm != null)
        {
            await _fsql.Update<PrivateMessage>()
                .Set(m => m.IsDeleted, true)
                .Where(m => m.Id == pm.Id)
                .ExecuteAffrowsAsync();

            // 从 Redis 缓存移除该消息
            await RemoveFromCacheAsync($"chat:private:{Math.Min(userId, (int)toId)}:{Math.Max(userId, (int)toId)}", targetId, pm.Id);
            sessionPeer = (int)toId;
        }
        else
        {
            // 群聊撤回（to = 群 ID）
            var gm = await _fsql.Select<GroupMessage>()
                .Where(m => m.GroupId == toId && m.SenderId == userId && !m.IsDeleted &&
                    m.SentAt >= withinTime &&
                    (m.Id.ToString() == targetId || m.ClientMessageId == targetId))
                .FirstAsync();

            if (gm != null)
            {
                await _fsql.Update<GroupMessage>()
                    .Set(m => m.IsDeleted, true)
                    .Where(m => m.Id == gm.Id)
                    .ExecuteAffrowsAsync();

                await RemoveFromCacheAsync($"chat:group:{toId}", targetId, gm.Id);
                groupId = toId;
            }
        }

        if (groupId == 0 && sessionPeer == 0)
            return; // 未找到可撤回的消息

        // 广播撤回通知
        var notify = new WsMessage
        {
            Type = WsMessageType.MessageRecalled,
            From = userId.ToString(),
            To = message.To,
            Content = targetId,
            MessageId = targetId
        };
        var json = JsonConvert.Serialize(notify);

        if (groupId > 0)
        {
            var members = await _fsql.Select<GroupMember>()
                .Where(m => m.GroupId == groupId)
                .ToListAsync();
            foreach (var member in members)
            {
                foreach (var client in _connectionManager.GetConnections(member.UserId))
                {
                    client.SendMessage(json);
                }
            }
        }
        else
        {
            // 发送者回显 + 接收者（所有在线设备）
            var targets = new[] { userId, sessionPeer };
            foreach (var uid in targets)
            {
                foreach (var client in _connectionManager.GetConnections(uid))
                {
                    client.SendMessage(json);
                }
            }
        }
    }

    /// <summary>
    /// 从 Redis 消息缓存中移除指定消息（保留顺序重建）
    /// </summary>
    private async Task RemoveFromCacheAsync(string cacheKey, string targetId, long dbId)
    {
        var items = await _redis.ListRangeAsync(cacheKey);
        if (items.Length == 0) return;

        var keep = items
            .Where(j => !j.Contains($"\"messageId\":\"{targetId}\"") && !j.Contains($"\"messageId\":\"{dbId}\""))
            .ToList();

        if (keep.Count == items.Length) return;

        await _redis.DeleteKeyAsync(cacheKey);
        // 原列表为新消息在前；逆序 LeftPush 恢复原顺序
        for (var i = keep.Count - 1; i >= 0; i--)
        {
            await _redis.ListLeftPushAsync(cacheKey, keep[i]);
        }
    }

    /// <summary>
    /// 获取私聊 Redis 缓存 Key（较小ID:较大ID）
    /// </summary>
    private static string GetPrivateChatCacheKey(int userId1, int userId2)
    {
        var minId = Math.Min(userId1, userId2);
        var maxId = Math.Max(userId1, userId2);
        return $"chat:private:{minId}:{maxId}";
    }

    /// <summary>
    /// 通知该用户的在线好友：ta 已上线/下线
    /// </summary>
    public async Task NotifyFriendsStatusAsync(int userId, bool online)
    {
        var friendships = await _fsql.Select<Friend>()
            .Where(f => (f.UserId == userId || f.FriendId == userId) && f.Status == 1)
            .ToListAsync();

        var statusMsg = JsonConvert.Serialize(new WsMessage
        {
            Type = WsMessageType.OnlineStatus,
            From = userId.ToString(),
            Content = online ? "online" : "offline"
        });

        foreach (var f in friendships)
        {
            var friendId = f.UserId == userId ? f.FriendId : f.UserId;
            foreach (var client in _connectionManager.GetConnections(friendId))
            {
                client.SendMessage(statusMsg);
            }
        }
    }

    /// <summary>
    /// 通知目标用户收到新的好友申请
    /// </summary>
    public void NotifyFriendRequestAsync(int toUserId, int fromUserId)
    {
        SendToUser(toUserId, WsMessageType.FriendRequest, fromUserId.ToString());
    }

    /// <summary>
    /// 好友申请被接受：双向通知（双方刷新好友列表）
    /// </summary>
    public void NotifyFriendAcceptedAsync(int requesterId, int accepterId)
    {
        SendToUser(requesterId, WsMessageType.FriendAccepted, accepterId.ToString());
        SendToUser(accepterId, WsMessageType.FriendAccepted, requesterId.ToString());
    }

    /// <summary>
    /// 好友申请被拒绝：通知申请人
    /// </summary>
    public void NotifyFriendRejectedAsync(int requesterId, int accepterId)
    {
        SendToUser(requesterId, WsMessageType.FriendRejected, accepterId.ToString());
    }

    /// <summary>
    /// 被邀请加入群组：通知被邀请者（from 携带群组 ID）
    /// </summary>
    public void NotifyGroupInvitedAsync(int toUserId, long groupId)
    {
        SendToUser(toUserId, WsMessageType.GroupInvited, groupId.ToString());
    }

    /// <summary>
    /// 通知用户被拉黑（含内容说明）
    /// </summary>
    public void NotifyBlockedAsync(int toUserId, int fromUserId, string content)
    {
        var json = JsonConvert.Serialize(new WsMessage
        {
            Type = WsMessageType.Blocked,
            From = fromUserId.ToString(),
            To = toUserId.ToString(),
            Content = content,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageType = 0,
            SenderName = string.Empty,
            SenderAvatar = null
        });
        foreach (var client in _connectionManager.GetConnections(toUserId))
        {
            client.SendMessage(json);
        }
    }

    /// <summary>
    /// 向指定用户推送一条 WS 消息（推送到其所有在线设备）
    /// </summary>
    private void SendToUser(int userId, string type, string fromUserId)
    {
        var json = JsonConvert.Serialize(new WsMessage
        {
            Type = type,
            From = fromUserId,
            Content = type
        });
        foreach (var client in _connectionManager.GetConnections(userId))
        {
            client.SendMessage(json);
        }
    }

    /// <summary>
    /// 用户上线后，补发各群已读游标之后的群消息（离线群消息）
    /// </summary>
    public async Task SendGroupOfflineMessagesAsync(int userId)
    {
        var members = await _fsql.Select<GroupMember>()
            .Where(m => m.UserId == userId)
            .ToListAsync();
        if (members.Count == 0) return;

        var client = _connectionManager.GetConnection(userId);
        if (client == null || client.Status != LHZ.WebSocket.Enums.ClientStatus.Opend) return;

        foreach (var member in members)
        {
            var msgs = await _fsql.Select<GroupMessage>()
                .Where(gm => gm.GroupId == member.GroupId && gm.Id > member.LastReadMessageId && !gm.IsDeleted)
                .OrderBy(gm => gm.SentAt)
                .Take(100) // 防止游标异常时一次性补发过多
                .ToListAsync();
            if (msgs.Count == 0) continue;

            var senderIds = msgs.Select(m => m.SenderId).Distinct().ToList();
            var users = await _fsql.Select<User>().Where(u => senderIds.Contains(u.Id)).ToListAsync();
            var userDict = users.ToDictionary(u => u.Id);

            foreach (var m in msgs)
            {
                var wsMsg = new WsMessage
                {
                    Type = WsMessageType.GroupMessage,
                    From = m.SenderId.ToString(),
                    To = member.GroupId.ToString(),
                    Content = m.Content,
                    MessageId = string.IsNullOrWhiteSpace(m.ClientMessageId) ? m.Id.ToString() : m.ClientMessageId,
                    MessageType = m.MessageType,
                    Mentions = ParseMentions(m.Mentions),
                    Timestamp = new DateTimeOffset(m.SentAt, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                    SenderName = userDict.TryGetValue(m.SenderId, out var u) ? u.Nickname : "未知",
                    SenderAvatar = userDict.TryGetValue(m.SenderId, out var u2) ? u2.Avatar : null
                };
                client.SendMessage(JsonConvert.Serialize(wsMsg));
            }
        }
    }

    /// <summary>
    /// 解析逗号分隔的提及 ID 字符串为列表
    /// </summary>
    private static List<int> ParseMentions(string? mentions)
    {
        if (string.IsNullOrWhiteSpace(mentions)) return new List<int>();
        return mentions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }
}
