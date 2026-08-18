using LHZ.FastJson;
using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Models.Entities;

namespace LHZ.OnlineChat.Server.Services;

/// <summary>
/// 消息历史与离线消息服务
/// </summary>
public class MessageService
{
    private readonly IFreeSql _fsql;
    private readonly RedisService _redis;

    public MessageService(IFreeSql fsql, RedisService redis)
    {
        _fsql = fsql;
        _redis = redis;
    }

    /// <summary>
    /// 获取私聊历史消息（分页）
    /// </summary>
    public async Task<ApiResponse<PagedResult<MessageDto>>> GetPrivateHistoryAsync(
        int userId, int friendId, int page = 1, int pageSize = 50)
    {
        // 验证好友关系
        var isFriend = await _fsql.Select<Friend>()
            .Where(f =>
                ((f.UserId == userId && f.FriendId == friendId) ||
                 (f.UserId == friendId && f.FriendId == userId)) &&
                f.Status == 1)
            .AnyAsync();

        if (!isFriend)
            return ApiResponse<PagedResult<MessageDto>>.Fail("不是好友关系");

        // 先从 Redis 缓存读取
        var cacheKey = GetPrivateChatCacheKey(userId, friendId);
        var cachedMessages = await _redis.ListRangeAsync(cacheKey);

        if (cachedMessages.Length > 0 && page == 1)
        {
            // 缓存内容为 WsMessage 快照（camelCase），解析后转换为 MessageDto
            var cachedList = cachedMessages
                .Select(m => TryParseCachedMessage(m))
                .Where(m => m != null)
                .Select(m => m!)
                .OrderBy(m => m.SentAt)
                .ToList();

            // 如果缓存足够，直接返回
            if (cachedList.Count >= pageSize)
            {
                return ApiResponse<PagedResult<MessageDto>>.Ok(new PagedResult<MessageDto>
                {
                    Items = cachedList.Take(pageSize).ToList(),
                    Total = cachedList.Count,
                    Page = page,
                    PageSize = pageSize
                });
            }
        }

        // 从数据库分页查询
        var total = await _fsql.Select<PrivateMessage>()
            .Where(m =>
                (m.SenderId == userId && m.ReceiverId == friendId) ||
                (m.SenderId == friendId && m.ReceiverId == userId))
            .CountAsync();

        var messages = await _fsql.Select<PrivateMessage>()
            .Where(m =>
                (m.SenderId == userId && m.ReceiverId == friendId) ||
                (m.SenderId == friendId && m.ReceiverId == userId))
            .OrderByDescending(m => m.SentAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // 查询发送者信息
        var senderIds = messages.Select(m => m.SenderId).Distinct().ToList();
        var users = await _fsql.Select<User>().Where(u => senderIds.Contains(u.Id)).ToListAsync();
        var userDict = users.ToDictionary(u => u.Id);

        var result = messages
            .OrderBy(m => m.SentAt)
            .Select(m => new MessageDto
            {
                Id = m.Id,
                SenderId = m.SenderId,
                SenderName = userDict.TryGetValue(m.SenderId, out var u) ? u.Nickname : "未知",
                SenderAvatar = userDict.TryGetValue(m.SenderId, out var u2) ? u2.Avatar : null,
                Content = m.Content,
                MessageType = m.MessageType,
                IsRead = m.IsRead,
                MessageId = m.ClientMessageId,
                SentAt = m.SentAt
            })
            .ToList();

        return ApiResponse<PagedResult<MessageDto>>.Ok(new PagedResult<MessageDto>
        {
            Items = result,
            Total = (int)total,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取群聊历史消息（分页）
    /// </summary>
    public async Task<ApiResponse<PagedResult<GroupMessageDto>>> GetGroupHistoryAsync(
        long groupId, int userId, int page = 1, int pageSize = 50)
    {
        // 验证是否群成员
        var isMember = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == userId)
            .AnyAsync();

        if (!isMember)
            return ApiResponse<PagedResult<GroupMessageDto>>.Fail("你不是该群成员");

        // 从数据库分页查询
        var total = await _fsql.Select<GroupMessage>()
            .Where(m => m.GroupId == groupId)
            .CountAsync();

        var messages = await _fsql.Select<GroupMessage>()
            .Where(m => m.GroupId == groupId)
            .OrderByDescending(m => m.SentAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // 查询发送者信息
        var senderIds = messages.Select(m => m.SenderId).Distinct().ToList();
        var users = await _fsql.Select<User>().Where(u => senderIds.Contains(u.Id)).ToListAsync();
        var userDict = users.ToDictionary(u => u.Id);

        var result = messages
            .OrderBy(m => m.SentAt)
            .Select(m => new GroupMessageDto
            {
                Id = m.Id,
                GroupId = m.GroupId,
                SenderId = m.SenderId,
                SenderName = userDict.TryGetValue(m.SenderId, out var u) ? u.Nickname : "未知",
                SenderAvatar = userDict.TryGetValue(m.SenderId, out var u2) ? u2.Avatar : null,
                Content = m.Content,
                MessageType = m.MessageType,
                MessageId = m.ClientMessageId,
                Mentions = ParseMentions(m.Mentions),
                SentAt = m.SentAt
            })
            .ToList();

        return ApiResponse<PagedResult<GroupMessageDto>>.Ok(new PagedResult<GroupMessageDto>
        {
            Items = result,
            Total = (int)total,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 标记消息已读
    /// </summary>
    public async Task<ApiResponse> MarkAsReadAsync(long messageId, int currentUserId)
    {
        var msg = await _fsql.Select<PrivateMessage>()
            .Where(m => m.Id == messageId && m.ReceiverId == currentUserId)
            .FirstAsync();

        if (msg == null)
            return ApiResponse.Fail("消息不存在或无权操作");

        await _fsql.Update<PrivateMessage>()
            .Set(p => p.IsRead, true)
            .Where(p => p.Id == messageId)
            .ExecuteAffrowsAsync();

        return ApiResponse.Ok("已标记已读");
    }

    /// <summary>
    /// 批量标记消息已读
    /// </summary>
    public async Task<ApiResponse> MarkAllAsReadAsync(int senderId, int currentUserId)
    {
        await _fsql.Update<PrivateMessage>()
            .Set(p => p.IsRead, true)
            .Where(p => p.SenderId == senderId && p.ReceiverId == currentUserId && !p.IsRead)
            .ExecuteAffrowsAsync();

        return ApiResponse.Ok("已全部标记已读");
    }

    /// <summary>
    /// 获取未读消息数
    /// </summary>
    public async Task<ApiResponse<object>> GetUnreadCountAsync(int userId)
    {
        var privateUnread = await _fsql.Select<PrivateMessage>()
            .Where(m => m.ReceiverId == userId && !m.IsRead)
            .CountAsync();

        return ApiResponse<object>.Ok(new
        {
            PrivateUnread = privateUnread
        });
    }

    /// <summary>
    /// 获取离线消息（用户上线时调用）
    /// </summary>
    public async Task<ApiResponse<List<MessageDto>>> GetOfflineMessagesAsync(int userId)
    {
        // 获取未读私聊消息
        var messages = await _fsql.Select<PrivateMessage>()
            .Where(m => m.ReceiverId == userId && !m.IsRead)
            .OrderBy(m => m.SentAt)
            .ToListAsync();

        // 查询发送者信息
        var senderIds = messages.Select(m => m.SenderId).Distinct().ToList();
        var users = await _fsql.Select<User>().Where(u => senderIds.Contains(u.Id)).ToListAsync();
        var userDict = users.ToDictionary(u => u.Id);

        var result = messages
            .Select(m => new MessageDto
            {
                Id = m.Id,
                SenderId = m.SenderId,
                SenderName = userDict.TryGetValue(m.SenderId, out var u) ? u.Nickname : "未知",
                SenderAvatar = userDict.TryGetValue(m.SenderId, out var u2) ? u2.Avatar : null,
                Content = m.Content,
                MessageType = m.MessageType,
                IsRead = m.IsRead,
                MessageId = m.ClientMessageId,
                SentAt = m.SentAt
            })
            .ToList();

        return ApiResponse<List<MessageDto>>.Ok(result);
    }

    /// <summary>
    /// 获取私聊 Redis 缓存 Key
    /// </summary>
    private static string GetPrivateChatCacheKey(int userId1, int userId2)
    {
        var minId = Math.Min(userId1, userId2);
        var maxId = Math.Max(userId1, userId2);
        return $"chat:private:{minId}:{maxId}";
    }

    /// <summary>
    /// 标记群消息已读：把该成员的已读游标推进到群内最新消息ID
    /// </summary>
    public async Task<ApiResponse> MarkGroupAsReadAsync(long groupId, int userId)
    {
        var member = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == userId)
            .FirstAsync();

        if (member == null)
            return ApiResponse.Fail("你不是该群组成员");

        var maxId = await _fsql.Select<GroupMessage>()
            .Where(gm => gm.GroupId == groupId)
            .MaxAsync(gm => gm.Id);

        await _fsql.Update<GroupMember>()
            .Set(m => m.LastReadMessageId, maxId)
            .Where(m => m.Id == member.Id)
            .ExecuteAffrowsAsync();

        return ApiResponse.Ok("已标记群消息已读");
    }

    /// <summary>
    /// 获取会话列表（私聊 + 群聊聚合，按最后消息时间倒序）
    /// </summary>
    public async Task<ApiResponse<List<SessionDto>>> GetSessionsAsync(int userId)
    {
        var sessions = new List<SessionDto>();

        // 我的会话设置（置顶/免打扰）
        var settings = await _fsql.Select<SessionSetting>()
            .Where(s => s.UserId == userId)
            .ToListAsync();
        var settingDict = settings.ToDictionary(s => $"{s.SessionType}_{s.SessionId}");

        // ===== 私聊会话 =====
        var recentPrivate = await _fsql.Select<PrivateMessage>()
            .Where(m => m.SenderId == userId || m.ReceiverId == userId)
            .OrderByDescending(m => m.SentAt)
            .Take(500)
            .ToListAsync();

        var peerLast = new Dictionary<int, PrivateMessage>();
        foreach (var m in recentPrivate)
        {
            var peer = m.SenderId == userId ? m.ReceiverId : m.SenderId;
            if (!peerLast.ContainsKey(peer)) peerLast[peer] = m;
        }

        var unreadPrivate = new Dictionary<int, int>();
        var unreadList = await _fsql.Select<PrivateMessage>()
            .Where(m => m.ReceiverId == userId && !m.IsRead)
            .ToListAsync();
        foreach (var m in unreadList)
            unreadPrivate[m.SenderId] = unreadPrivate.GetValueOrDefault(m.SenderId) + 1;

        if (peerLast.Count > 0)
        {
            var peerIds = peerLast.Keys.ToList();
            var users = await _fsql.Select<User>().Where(u => peerIds.Contains(u.Id)).ToListAsync();
            var userDict = users.ToDictionary(u => u.Id);

            // 我的好友备注（会话名优先显示备注）
            var tags = await _fsql.Select<FriendTag>()
                .Where(t => t.UserId == userId && peerIds.Contains(t.FriendId))
                .ToListAsync();
            var tagDict = tags.ToDictionary(t => t.FriendId);

            foreach (var (peerId, last) in peerLast)
            {
                var nickname = userDict.GetValueOrDefault(peerId)?.Nickname ?? $"用户{peerId}";
                var remark = tagDict.GetValueOrDefault(peerId)?.Remark;
                var setting = settingDict.GetValueOrDefault($"private_{peerId}");
                sessions.Add(new SessionDto
                {
                    Type = "private",
                    Id = peerId,
                    Name = string.IsNullOrWhiteSpace(remark) ? nickname : remark,
                    Avatar = userDict.GetValueOrDefault(peerId)?.Avatar,
                    LastMessage = last.Content,
                    LastTime = last.SentAt,
                    UnreadCount = unreadPrivate.GetValueOrDefault(peerId),
                    IsPinned = setting?.IsPinned ?? false,
                    Muted = setting?.Muted ?? false
                });
            }
        }

        // ===== 群聊会话 =====
        var members = await _fsql.Select<GroupMember>()
            .Where(m => m.UserId == userId)
            .ToListAsync();

        if (members.Count > 0)
        {
            var groupIds = members.Select(m => m.GroupId).ToList();
            var groups = await _fsql.Select<Group_>().Where(g => groupIds.Contains(g.Id)).ToListAsync();
            var groupDict = groups.ToDictionary(g => g.Id);
            var memberDict = members.ToDictionary(m => m.GroupId);

            var recentGroup = await _fsql.Select<GroupMessage>()
                .Where(gm => groupIds.Contains(gm.GroupId))
                .OrderByDescending(gm => gm.SentAt)
                .Take(500)
                .ToListAsync();
            var groupLast = new Dictionary<long, GroupMessage>();
            foreach (var m in recentGroup)
                if (!groupLast.ContainsKey(m.GroupId)) groupLast[m.GroupId] = m;

            foreach (var gid in groupIds)
            {
                var last = groupLast.GetValueOrDefault(gid);
                var member = memberDict.GetValueOrDefault(gid);
                var unread = 0;
                if (member != null)
                {
                    unread = (int)await _fsql.Select<GroupMessage>()
                        .Where(gm => gm.GroupId == gid && gm.Id > member.LastReadMessageId)
                        .CountAsync();
                }

                var setting = settingDict.GetValueOrDefault($"group_{gid}");
                sessions.Add(new SessionDto
                {
                    Type = "group",
                    Id = gid,
                    Name = groupDict.GetValueOrDefault(gid)?.Name ?? $"群{gid}",
                    Avatar = groupDict.GetValueOrDefault(gid)?.Avatar,
                    LastMessage = last?.Content ?? string.Empty,
                    LastTime = last?.SentAt ?? DateTime.MinValue,
                    UnreadCount = unread,
                    IsPinned = setting?.IsPinned ?? false,
                    Muted = setting?.Muted ?? false
                });
            }
        }

        // 置顶优先，再按最后消息时间倒序
        sessions = sessions.OrderByDescending(s => s.IsPinned).ThenByDescending(s => s.LastTime).ToList();
        return ApiResponse<List<SessionDto>>.Ok(sessions);
    }

    /// <summary>
    /// 更新会话设置（置顶 / 免打扰）
    /// </summary>
    public async Task<ApiResponse> UpdateSessionSettingAsync(int userId, UpdateSessionSettingRequest request)
    {
        if (request.Type != "private" && request.Type != "group")
            return ApiResponse.Fail("无效的会话类型");
        if (request.Id <= 0)
            return ApiResponse.Fail("无效的会话 ID");
        if (!request.IsPinned.HasValue && !request.Muted.HasValue)
            return ApiResponse.Fail("没有需要更新的设置");

        var setting = await _fsql.Select<SessionSetting>()
            .Where(s => s.UserId == userId && s.SessionType == request.Type && s.SessionId == request.Id)
            .FirstAsync();

        if (setting == null)
        {
            setting = new SessionSetting
            {
                UserId = userId,
                SessionType = request.Type,
                SessionId = request.Id,
                IsPinned = request.IsPinned ?? false,
                Muted = request.Muted ?? false,
                UpdatedAt = DateTime.UtcNow
            };
            await _fsql.Insert(setting).ExecuteAffrowsAsync();
        }
        else
        {
            if (request.IsPinned.HasValue) setting.IsPinned = request.IsPinned.Value;
            if (request.Muted.HasValue) setting.Muted = request.Muted.Value;
            setting.UpdatedAt = DateTime.UtcNow;
            await _fsql.Update<SessionSetting>().SetSource(setting).ExecuteAffrowsAsync();
        }

        return ApiResponse.Ok("设置已保存");
    }

    /// <summary>
    /// 解析 Redis 中缓存的 WsMessage 快照（camelCase JSON）为 MessageDto
    /// </summary>
    private static MessageDto? TryParseCachedMessage(string json)
    {
        try
        {
            var ws = JsonConvert.Deserialize<WsMessage>(json);
            if (ws == null) return null;

            return new MessageDto
            {
                SenderId = int.TryParse(ws.From, out var senderId) ? senderId : 0,
                SenderName = ws.SenderName,
                SenderAvatar = ws.SenderAvatar,
                Content = ws.Content,
                MessageType = ws.MessageType,
                IsRead = false,
                MessageId = ws.MessageId,
                SentAt = ws.Timestamp > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ws.Timestamp).UtcDateTime
                    : DateTime.MinValue
            };
        }
        catch
        {
            // 单条缓存解析失败不影响整体
            return null;
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
