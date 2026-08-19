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
    /// 归一化数据库读出的时间：Npgsql/FreeSql 返回的 DateTime Kind 可能为 Unspecified，
    /// 序列化时丢失 UTC 标识（无 Z 后缀），前端按本地时间解析会偏移 8 小时。
    /// </summary>
    private static DateTime AsUtc(DateTime dt)
        => dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);

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

        // 数据库总数（缓存路径与分页路径共用，保证 hasMore 判断正确）
        var total = await _fsql.Select<PrivateMessage>()
            .Where(m =>
                (m.SenderId == userId && m.ReceiverId == friendId) ||
                (m.SenderId == friendId && m.ReceiverId == userId))
            .CountAsync();

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
                    Total = (int)total,
                    Page = page,
                    PageSize = pageSize
                });
            }
        }

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
                IsDeleted = m.IsDeleted,
                MessageId = m.ClientMessageId,
                ReplyTo = m.ReplyMessageId,
                ReplyContent = m.ReplyContent,
                ReplySender = m.ReplySenderName,
                SentAt = AsUtc(m.SentAt)
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
                ReplyTo = m.ReplyMessageId,
                ReplyContent = m.ReplyContent,
                ReplySender = m.ReplySenderName,
                IsDeleted = m.IsDeleted,
                Mentions = ParseMentions(m.Mentions),
                SentAt = AsUtc(m.SentAt)
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
    /// 全局搜索消息（私聊 + 群聊，分页，按时间倒序）。
    /// scopeType/scopeId 可选：限定在单个会话内搜索（private=好友ID / group=群ID），需校验访问权限。
    /// </summary>
    public async Task<ApiResponse<PagedResult<MessageSearchResultDto>>> SearchMessagesAsync(
        int userId, string keyword, int page = 1, int pageSize = 30,
        string? scopeType = null, long? scopeId = null)
    {
        keyword = (keyword ?? string.Empty).Trim();
        if (keyword.Length == 0)
            return ApiResponse<PagedResult<MessageSearchResultDto>>.Fail("请输入搜索关键词");
        if (keyword.Length > 50)
            keyword = keyword[..50];

        var take = page * pageSize;

        // ===== 会话内搜索：校验访问权限后只查该会话 =====
        if (scopeType == "private")
        {
            var peerId = scopeId ?? 0;
            if (peerId <= 0)
                return ApiResponse<PagedResult<MessageSearchResultDto>>.Fail("无效的会话");
            var isFriend = await _fsql.Select<Friend>()
                .Where(f =>
                    ((f.UserId == userId && f.FriendId == peerId) ||
                     (f.UserId == peerId && f.FriendId == userId)) &&
                    f.Status == 1)
                .AnyAsync();
            if (!isFriend)
                return ApiResponse<PagedResult<MessageSearchResultDto>>.Fail("不是好友关系");

            var msgs = await _fsql.Select<PrivateMessage>()
                .Where(m =>
                    ((m.SenderId == userId && m.ReceiverId == peerId) ||
                     (m.SenderId == peerId && m.ReceiverId == userId)) &&
                    !m.IsDeleted && m.Content.Contains(keyword))
                .OrderByDescending(m => m.SentAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            var total = (int)await _fsql.Select<PrivateMessage>()
                .Where(m =>
                    ((m.SenderId == userId && m.ReceiverId == peerId) ||
                     (m.SenderId == peerId && m.ReceiverId == userId)) &&
                    !m.IsDeleted && m.Content.Contains(keyword))
                .CountAsync();

            var peer = await _fsql.Select<User>().Where(u => u.Id == peerId).FirstAsync();
            var scopeItems = msgs.Select(m => new MessageSearchResultDto
            {
                Type = "private",
                SessionId = peerId,
                SessionName = peer?.Nickname ?? $"用户{peerId}",
                SenderName = m.SenderId == userId ? "我" : (peer?.Nickname ?? "未知"),
                SenderAvatar = m.SenderId == userId ? null : peer?.Avatar,
                Content = m.Content,
                MessageType = m.MessageType,
                MessageId = m.ClientMessageId,
                SentAt = AsUtc(m.SentAt)
            }).ToList();

            return ApiResponse<PagedResult<MessageSearchResultDto>>.Ok(new PagedResult<MessageSearchResultDto>
            {
                Items = scopeItems,
                Total = total,
                Page = page,
                PageSize = pageSize
            });
        }

        if (scopeType == "group")
        {
            var groupId = scopeId ?? 0;
            if (groupId <= 0)
                return ApiResponse<PagedResult<MessageSearchResultDto>>.Fail("无效的会话");
            var isMember = await _fsql.Select<GroupMember>()
                .Where(m => m.GroupId == groupId && m.UserId == userId)
                .AnyAsync();
            if (!isMember)
                return ApiResponse<PagedResult<MessageSearchResultDto>>.Fail("你不是该群成员");

            var msgs = await _fsql.Select<GroupMessage>()
                .Where(m => m.GroupId == groupId && !m.IsDeleted && m.Content.Contains(keyword))
                .OrderByDescending(m => m.SentAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            var total = (int)await _fsql.Select<GroupMessage>()
                .Where(m => m.GroupId == groupId && !m.IsDeleted && m.Content.Contains(keyword))
                .CountAsync();

            var group = await _fsql.Select<Group_>().Where(g => g.Id == groupId).FirstAsync();
            var scopeSenderIds = msgs.Select(m => m.SenderId).Distinct().ToList();
            var scopeUsers = scopeSenderIds.Count > 0
                ? await _fsql.Select<User>().Where(u => scopeSenderIds.Contains(u.Id)).ToListAsync()
                : new List<User>();
            var scopeUserDict = scopeUsers.ToDictionary(u => u.Id);

            var scopeItems = msgs.Select(m => new MessageSearchResultDto
            {
                Type = "group",
                SessionId = groupId,
                SessionName = group?.Name ?? $"群{groupId}",
                SenderName = m.SenderId == userId ? "我" : (scopeUserDict.TryGetValue(m.SenderId, out var su) ? su.Nickname : "未知"),
                SenderAvatar = scopeUserDict.TryGetValue(m.SenderId, out var sa) ? sa.Avatar : null,
                Content = m.Content,
                MessageType = m.MessageType,
                MessageId = m.ClientMessageId,
                SentAt = AsUtc(m.SentAt)
            }).ToList();

            return ApiResponse<PagedResult<MessageSearchResultDto>>.Ok(new PagedResult<MessageSearchResultDto>
            {
                Items = scopeItems,
                Total = total,
                Page = page,
                PageSize = pageSize
            });
        }

        // ===== 全局搜索（默认） =====
        var privateMsgs = await _fsql.Select<PrivateMessage>()
            .Where(m => (m.SenderId == userId || m.ReceiverId == userId) && !m.IsDeleted && m.Content.Contains(keyword))
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .ToListAsync();
        var privateTotal = (int)await _fsql.Select<PrivateMessage>()
            .Where(m => (m.SenderId == userId || m.ReceiverId == userId) && !m.IsDeleted && m.Content.Contains(keyword))
            .CountAsync();

        // 群聊：我所在的所有群
        var groupIds = (await _fsql.Select<GroupMember>().Where(m => m.UserId == userId).ToListAsync())
            .Select(m => m.GroupId).ToList();
        var groupMsgs = new List<GroupMessage>();
        var groupTotal = 0;
        if (groupIds.Count > 0)
        {
            groupMsgs = await _fsql.Select<GroupMessage>()
                .Where(m => groupIds.Contains(m.GroupId) && !m.IsDeleted && m.Content.Contains(keyword))
                .OrderByDescending(m => m.SentAt)
                .Take(take)
                .ToListAsync();
            groupTotal = (int)await _fsql.Select<GroupMessage>()
                .Where(m => groupIds.Contains(m.GroupId) && !m.IsDeleted && m.Content.Contains(keyword))
                .CountAsync();
        }

        // 用户信息（含自己，用于头像）
        var userIds = privateMsgs
            .Select(m => m.SenderId == userId ? m.ReceiverId : m.SenderId)
            .Concat(groupMsgs.Select(m => m.SenderId))
            .Append(userId)
            .Distinct()
            .ToList();
        var users = userIds.Count > 0
            ? await _fsql.Select<User>().Where(u => userIds.Contains(u.Id)).ToListAsync()
            : new List<User>();
        var userDict = users.ToDictionary(u => u.Id);

        var groups = groupIds.Count > 0
            ? await _fsql.Select<Group_>().Where(g => groupIds.Contains(g.Id)).ToListAsync()
            : new List<Group_>();
        var groupDict = groups.ToDictionary(g => g.Id);

        var merged = new List<(DateTime SentAt, MessageSearchResultDto Dto)>();
        foreach (var m in privateMsgs)
        {
            var peerId = m.SenderId == userId ? m.ReceiverId : m.SenderId;
            merged.Add((m.SentAt, new MessageSearchResultDto
            {
                Type = "private",
                SessionId = peerId,
                SessionName = userDict.TryGetValue(peerId, out var peer) ? peer.Nickname : $"用户{peerId}",
                SenderName = m.SenderId == userId ? "我" : (userDict.TryGetValue(m.SenderId, out var su) ? su.Nickname : "未知"),
                SenderAvatar = userDict.TryGetValue(m.SenderId, out var sa) ? sa.Avatar : null,
                Content = m.Content,
                MessageType = m.MessageType,
                MessageId = m.ClientMessageId,
                SentAt = AsUtc(m.SentAt)
            }));
        }
        foreach (var m in groupMsgs)
        {
            merged.Add((m.SentAt, new MessageSearchResultDto
            {
                Type = "group",
                SessionId = m.GroupId,
                SessionName = groupDict.TryGetValue(m.GroupId, out var g) ? g.Name : $"群{m.GroupId}",
                SenderName = m.SenderId == userId ? "我" : (userDict.TryGetValue(m.SenderId, out var su) ? su.Nickname : "未知"),
                SenderAvatar = userDict.TryGetValue(m.SenderId, out var sa) ? sa.Avatar : null,
                Content = m.Content,
                MessageType = m.MessageType,
                MessageId = m.ClientMessageId,
                SentAt = AsUtc(m.SentAt)
            }));
        }

        var sorted = merged.OrderByDescending(x => x.SentAt).ToList();
        var items = sorted.Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Dto).ToList();

        return ApiResponse<PagedResult<MessageSearchResultDto>>.Ok(new PagedResult<MessageSearchResultDto>
        {
            Items = items,
            Total = privateTotal + groupTotal,
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
                IsDeleted = m.IsDeleted,
                MessageId = m.ClientMessageId,
                ReplyTo = m.ReplyMessageId,
                ReplyContent = m.ReplyContent,
                ReplySender = m.ReplySenderName,
                SentAt = AsUtc(m.SentAt)
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
                    LastTime = AsUtc(last.SentAt),
                    UnreadCount = unreadPrivate.GetValueOrDefault(peerId),
                    IsPinned = setting?.IsPinned ?? false,
                    Muted = setting?.Muted ?? false,
                    IsBot = userDict.GetValueOrDefault(peerId)?.IsBot ?? false
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
                    LastTime = AsUtc(last?.SentAt ?? DateTime.MinValue),
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
