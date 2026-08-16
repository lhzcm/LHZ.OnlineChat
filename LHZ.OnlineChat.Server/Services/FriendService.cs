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
    /// 发送好友申请
    /// </summary>
    public async Task<ApiResponse> SendFriendRequestAsync(long userId, string friendUsername)
    {
        if (string.IsNullOrWhiteSpace(friendUsername))
            return ApiResponse.Fail("好友用户名不能为空");

        // 查找目标用户
        var targetUser = await _fsql.Select<User>()
            .Where(u => u.Username == friendUsername)
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
    public async Task<ApiResponse> AcceptFriendRequestAsync(long requestId, long currentUserId)
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

        return ApiResponse.Ok("已添加为好友");
    }

    /// <summary>
    /// 拒绝好友申请
    /// </summary>
    public async Task<ApiResponse> RejectFriendRequestAsync(long requestId, long currentUserId)
    {
        var request = await _fsql.Select<Friend>().Where(f => f.Id == requestId).FirstAsync();
        if (request == null)
            return ApiResponse.Fail("申请不存在");

        if (request.FriendId != currentUserId)
            return ApiResponse.Fail("无权操作此申请");

        await _fsql.Delete<Friend>().Where(f => f.Id == requestId).ExecuteAffrowsAsync();
        return ApiResponse.Ok("已拒绝好友申请");
    }

    /// <summary>
    /// 删除好友
    /// </summary>
    public async Task<ApiResponse> DeleteFriendAsync(long userId, long friendId)
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
    public async Task<ApiResponse<List<FriendInfo>>> GetFriendsAsync(long userId)
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

        // 检查在线状态
        var result = new List<FriendInfo>();
        foreach (var user in users)
        {
            var isOnline = await _redis.KeyExistsAsync($"ws:online:{user.Id}");
            result.Add(new FriendInfo
            {
                UserId = user.Id,
                Username = user.Username,
                Nickname = user.Nickname,
                Avatar = user.Avatar,
                IsOnline = isOnline,
                Status = 1
            });
        }

        // 在线好友排前面
        result = result.OrderByDescending(f => f.IsOnline).ThenBy(f => f.Nickname).ToList();

        return ApiResponse<List<FriendInfo>>.Ok(result);
    }

    /// <summary>
    /// 获取待处理的好友申请（别人发给我的）
    /// </summary>
    public async Task<ApiResponse<List<FriendRequestInfo>>> GetPendingRequestsAsync(long userId)
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
            Username = userDict.TryGetValue(r.UserId, out var u) ? u.Username : "未知",
            Nickname = userDict.TryGetValue(r.UserId, out var u2) ? u2.Nickname : "未知",
            Avatar = userDict.TryGetValue(r.UserId, out var u3) ? u3.Avatar : null,
            CreatedAt = r.CreatedAt
        }).ToList();

        return ApiResponse<List<FriendRequestInfo>>.Ok(result);
    }
}
