using System.Text.Json;
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
        long userId, long friendId, int page = 1, int pageSize = 50)
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
            var cachedList = cachedMessages
                .Select(m => JsonSerializer.Deserialize<MessageDto>(m))
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
        long groupId, long userId, int page = 1, int pageSize = 50)
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
    public async Task<ApiResponse> MarkAsReadAsync(long messageId, long currentUserId)
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
    public async Task<ApiResponse> MarkAllAsReadAsync(long senderId, long currentUserId)
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
    public async Task<ApiResponse<object>> GetUnreadCountAsync(long userId)
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
    public async Task<ApiResponse<List<MessageDto>>> GetOfflineMessagesAsync(long userId)
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
                SentAt = m.SentAt
            })
            .ToList();

        return ApiResponse<List<MessageDto>>.Ok(result);
    }

    /// <summary>
    /// 获取私聊 Redis 缓存 Key
    /// </summary>
    private static string GetPrivateChatCacheKey(long userId1, long userId2)
    {
        var minId = Math.Min(userId1, userId2);
        var maxId = Math.Max(userId1, userId2);
        return $"chat:private:{minId}:{maxId}";
    }
}
