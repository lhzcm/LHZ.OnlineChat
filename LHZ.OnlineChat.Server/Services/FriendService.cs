using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Models.Entities;

namespace LHZ.OnlineChat.Server.Services;

/// <summary>
/// 好友管理服务
/// </summary>
public class FriendService
{
    private readonly IFreeSql _fsql;
    private readonly RedisService _redis;
    private readonly WsMessageHandler _wsMessageHandler;

    public FriendService(IFreeSql fsql, RedisService redis, WsMessageHandler wsMessageHandler)
    {
        _fsql = fsql;
        _redis = redis;
        _wsMessageHandler = wsMessageHandler;
    }

    /// <summary>
    /// 发送好友申请（按账号 ID 查找目标用户）
    /// </summary>
    public async Task<ApiResponse> SendFriendRequestAsync(int userId, int accountId)
    {
        if (accountId <= 0)
            return ApiResponse.Fail("请输入正确的账号 ID");

        // 查找目标用户
        var targetUser = await _fsql.Select<User>()
            .Where(u => u.Id == accountId)
            .FirstAsync();

        if (targetUser == null)
            return ApiResponse.Fail("用户不存在");

        if (targetUser.Id == userId)
            return ApiResponse.Fail("不能添加自己为好友");

        // 检查是否已经是好友或已有待处理申请
        var existing = await _fsql.Select<Friend>()
            .Where(f =>
                (f.UserId == userId && f.FriendId == targetUser.Id) ||
                (f.UserId == targetUser.Id && f.FriendId == userId))
            .FirstAsync();

        if (existing != null)
        {
            return existing.Status switch
            {
                0 => ApiResponse.Fail("已发送好友申请，等待对方确认"),
                1 => ApiResponse.Fail("你们已经是好友了"),
                2 => ApiResponse.Fail("对方已将你屏蔽"),
                _ => ApiResponse.Fail("操作失败")
            };
        }

        // 创建好友申请
        var friend = new Friend
        {
            UserId = userId,
            FriendId = targetUser.Id,
            Status = 0, // 待确认
            CreatedAt = DateTime.UtcNow
        };

        await _fsql.Insert(friend).ExecuteAffrowsAsync();

        // 实时通知对方（在线时）
        _wsMessageHandler.NotifyFriendRequestAsync(targetUser.Id, userId);

        return ApiResponse.Ok("好友申请已发送");
    }

    /// <summary>
    /// 接受好友申请
    /// </summary>
    public async Task<ApiResponse> AcceptFriendRequestAsync(long requestId, int currentUserId)
    {
        var request = await _fsql.Select<Friend>().Where(f => f.Id == requestId).FirstAsync();
        if (request == null)
            return ApiResponse.Fail("申请不存在");

        if (request.FriendId != currentUserId)
            return ApiResponse.Fail("无权操作此申请");

        if (request.Status != 0)
            return ApiResponse.Fail("该申请已处理");

        request.Status = 1; // 已接受
        await _fsql.Update<Friend>().SetSource(request).ExecuteAffrowsAsync();

        // 实时通知双方（刷新好友列表）
        _wsMessageHandler.NotifyFriendAcceptedAsync(request.UserId, request.FriendId);

        return ApiResponse.Ok("已添加为好友");
    }

    /// <summary>
    /// 拒绝好友申请
    /// </summary>
    public async Task<ApiResponse> RejectFriendRequestAsync(long requestId, int currentUserId)
    {
        var request = await _fsql.Select<Friend>().Where(f => f.Id == requestId).FirstAsync();
        if (request == null)
            return ApiResponse.Fail("申请不存在");

        if (request.FriendId != currentUserId)
            return ApiResponse.Fail("无权操作此申请");

        await _fsql.Delete<Friend>().Where(f => f.Id == requestId).ExecuteAffrowsAsync();

        // 实时通知申请人
        _wsMessageHandler.NotifyFriendRejectedAsync(request.UserId, request.FriendId);

        return ApiResponse.Ok("已拒绝好友申请");
    }

    /// <summary>
    /// 删除好友
    /// </summary>
    public async Task<ApiResponse> DeleteFriendAsync(int userId, int friendId)
    {
        var friendship = await _fsql.Select<Friend>()
            .Where(f =>
                (f.UserId == userId && f.FriendId == friendId && f.Status == 1) ||
                (f.UserId == friendId && f.FriendId == userId && f.Status == 1))
            .FirstAsync();

        if (friendship == null)
            return ApiResponse.Fail("好友关系不存在");

        await _fsql.Delete<Friend>().Where(f => f.Id == friendship.Id).ExecuteAffrowsAsync();
        return ApiResponse.Ok("已删除好友");
    }

    /// <summary>
    /// 获取好友列表（含在线状态）
    /// </summary>
    public async Task<ApiResponse<List<FriendInfo>>> GetFriendsAsync(int userId)
    {
        // 查询所有已接受的好友关系
        var friendships = await _fsql.Select<Friend>()
            .Where(f =>
                (f.UserId == userId || f.FriendId == userId) && f.Status == 1)
            .ToListAsync();

        var friendIds = friendships
            .Select(f => f.UserId == userId ? f.FriendId : f.UserId)
            .Distinct()
            .ToList();

        if (friendIds.Count == 0)
            return ApiResponse<List<FriendInfo>>.Ok(new List<FriendInfo>());

        // 查询好友用户信息
        var users = await _fsql.Select<User>()
            .Where(u => friendIds.Contains(u.Id))
            .ToListAsync();

        // 查询我设置的备注/分类（设置者视角）
        var tags = await _fsql.Select<FriendTag>()
            .Where(t => t.UserId == userId && friendIds.Contains(t.FriendId))
            .ToListAsync();
        var tagDict = tags.ToDictionary(t => t.FriendId);

        // 检查在线状态
        var result = new List<FriendInfo>();
        foreach (var user in users)
        {
            var isOnline = await _redis.KeyExistsAsync($"ws:online:{user.Id}");
            var tag = tagDict.GetValueOrDefault(user.Id);
            result.Add(new FriendInfo
            {
                UserId = user.Id,
                Nickname = user.Nickname,
                Avatar = user.Avatar,
                IsOnline = isOnline,
                Status = 1,
                Remark = tag?.Remark,
                Category = tag?.Category
            });
        }

        // 在线好友排前面
        result = result.OrderByDescending(f => f.IsOnline).ThenBy(f => f.Nickname).ToList();

        return ApiResponse<List<FriendInfo>>.Ok(result);
    }

    /// <summary>
    /// 设置好友备注（空 = 清除备注）
    /// </summary>
    public async Task<ApiResponse> SetFriendRemarkAsync(int userId, int friendId, string? remark)
    {
        if (!await IsFriendAsync(userId, friendId))
            return ApiResponse.Fail("好友关系不存在");

        remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim();
        if (remark != null && remark.Length > 50)
            return ApiResponse.Fail("备注长度不能超过 50 个字符");

        await UpsertTagAsync(userId, friendId, tag => _fsql.Update<FriendTag>()
            .Set(t => t.Remark, remark)
            .Where(t => t.Id == tag.Id)
            .ExecuteAffrowsAsync());

        return ApiResponse.Ok(remark == null ? "已清除备注" : "备注已保存");
    }

    /// <summary>
    /// 设置好友分类标签（空 = 清除分类，未分组）
    /// </summary>
    public async Task<ApiResponse> SetFriendCategoryAsync(int userId, int friendId, string? category)
    {
        if (!await IsFriendAsync(userId, friendId))
            return ApiResponse.Fail("好友关系不存在");

        category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (category != null && category.Length > 30)
            return ApiResponse.Fail("分类名称不能超过 30 个字符");

        await UpsertTagAsync(userId, friendId, tag => _fsql.Update<FriendTag>()
            .Set(t => t.Category, category)
            .Where(t => t.Id == tag.Id)
            .ExecuteAffrowsAsync());

        return ApiResponse.Ok(category == null ? "已清除分类" : "分类已保存");
    }

    /// <summary>
    /// 判断两人是否为好友（已接受）
    /// </summary>
    private async Task<bool> IsFriendAsync(int userId, int friendId)
    {
        if (userId == friendId) return false;
        return await _fsql.Select<Friend>()
            .Where(f => f.Status == 1 &&
                ((f.UserId == userId && f.FriendId == friendId) ||
                 (f.UserId == friendId && f.FriendId == userId)))
            .AnyAsync();
    }

    /// <summary>
    /// upsert 好友设置：存在则更新，不存在则创建
    /// </summary>
    private async Task UpsertTagAsync(int userId, int friendId, Func<FriendTag, Task> updateAction)
    {
        var tag = await _fsql.Select<FriendTag>()
            .Where(t => t.UserId == userId && t.FriendId == friendId)
            .FirstAsync();

        if (tag == null)
        {
            await _fsql.Insert(new FriendTag { UserId = userId, FriendId = friendId }).ExecuteAffrowsAsync();
            tag = await _fsql.Select<FriendTag>()
                .Where(t => t.UserId == userId && t.FriendId == friendId)
                .FirstAsync();
        }

        if (tag != null)
        {
            await updateAction(tag);
        }
    }

    /// <summary>
    /// 获取待处理的好友申请（别人发给我的）
    /// </summary>
    public async Task<ApiResponse<List<FriendRequestInfo>>> GetPendingRequestsAsync(int userId)
    {
        var requests = await _fsql.Select<Friend>()
            .Where(f => f.FriendId == userId && f.Status == 0)
            .ToListAsync();

        var requesterIds = requests.Select(r => r.UserId).Distinct().ToList();
        if (requesterIds.Count == 0)
            return ApiResponse<List<FriendRequestInfo>>.Ok(new List<FriendRequestInfo>());

        var users = await _fsql.Select<User>()
            .Where(u => requesterIds.Contains(u.Id))
            .ToListAsync();

        var userDict = users.ToDictionary(u => u.Id);

        var result = requests.Select(r => new FriendRequestInfo
        {
            Id = r.Id,
            UserId = r.UserId,
            Nickname = userDict.TryGetValue(r.UserId, out var u) ? u.Nickname : "未知",
            Avatar = userDict.TryGetValue(r.UserId, out var u2) ? u2.Avatar : null,
            CreatedAt = r.CreatedAt
        }).ToList();

        return ApiResponse<List<FriendRequestInfo>>.Ok(result);
    }
}
