using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Models.Entities;

namespace LHZ.OnlineChat.Server.Services;

/// <summary>
/// 黑名单服务
/// 拉黑规则：拉黑后自动解除好友关系；被拉黑者无法给拉黑者发私聊消息、无法发送好友申请。
/// 通知由控制器通过 WsMessageHandler 发送（避免单例/作用域循环依赖）。
/// </summary>
public class BlacklistService
{
    private readonly IFreeSql _fsql;

    public BlacklistService(IFreeSql fsql)
    {
        _fsql = fsql;
    }

    /// <summary>
    /// 拉黑用户（自动解除好友关系，并通知被拉黑者）
    /// </summary>
    public async Task<ApiResponse> BlockAsync(int userId, int blockedId)
    {
        if (blockedId <= 0)
            return ApiResponse.Fail("无效的用户 ID");
        if (userId == blockedId)
            return ApiResponse.Fail("不能拉黑自己");

        var target = await _fsql.Select<User>().Where(u => u.Id == blockedId).FirstAsync();
        if (target == null)
            return ApiResponse.Fail("用户不存在");

        var exists = await _fsql.Select<Blacklist>()
            .Where(b => b.UserId == userId && b.BlockedUserId == blockedId)
            .AnyAsync();
        if (exists)
            return ApiResponse.Fail("该用户已在黑名单中");

        await _fsql.Insert(new Blacklist
        {
            UserId = userId,
            BlockedUserId = blockedId,
            CreatedAt = DateTime.UtcNow
        }).ExecuteAffrowsAsync();

        // 拉黑后自动解除好友关系（双向）
        await _fsql.Delete<Friend>()
            .Where(f =>
                ((f.UserId == userId && f.FriendId == blockedId) ||
                 (f.UserId == blockedId && f.FriendId == userId)) &&
                f.Status == 1)
            .ExecuteAffrowsAsync();

        return ApiResponse.Ok("已拉黑");
    }

    /// <summary>
    /// 解除拉黑
    /// </summary>
    public async Task<ApiResponse> UnblockAsync(int userId, int blockedId)
    {
        var affected = await _fsql.Delete<Blacklist>()
            .Where(b => b.UserId == userId && b.BlockedUserId == blockedId)
            .ExecuteAffrowsAsync();

        return affected > 0 ? ApiResponse.Ok("已解除拉黑") : ApiResponse.Fail("该用户不在你的黑名单中");
    }

    /// <summary>
    /// 获取我的黑名单列表
    /// </summary>
    public async Task<ApiResponse<List<BlacklistUserDto>>> GetBlacklistAsync(int userId)
    {
        var items = await _fsql.Select<Blacklist>()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        if (items.Count == 0)
            return ApiResponse<List<BlacklistUserDto>>.Ok(new List<BlacklistUserDto>());

        var userIds = items.Select(b => b.BlockedUserId).ToList();
        var users = await _fsql.Select<User>().Where(u => userIds.Contains(u.Id)).ToListAsync();
        var userDict = users.ToDictionary(u => u.Id);

        var result = items.Select(b => new BlacklistUserDto
        {
            UserId = b.BlockedUserId,
            Nickname = userDict.TryGetValue(b.BlockedUserId, out var u) ? u.Nickname : "未知",
            Avatar = userDict.TryGetValue(b.BlockedUserId, out var u2) ? u2.Avatar : null,
            BlockedAt = b.CreatedAt
        }).ToList();

        return ApiResponse<List<BlacklistUserDto>>.Ok(result);
    }

    /// <summary>
    /// 是否被拉黑：receiver 是否拉黑了 sender（私聊消息拦截用）
    /// </summary>
    public async Task<bool> IsBlockedByAsync(int receiverId, int senderId)
        => await _fsql.Select<Blacklist>()
            .Where(b => b.UserId == receiverId && b.BlockedUserId == senderId)
            .AnyAsync();

    /// <summary>
    /// 双方是否存在任一方向的拉黑关系（好友申请等场景）
    /// </summary>
    public async Task<bool> IsBlockedEitherAsync(int userId1, int userId2)
        => await _fsql.Select<Blacklist>()
            .Where(b =>
                (b.UserId == userId1 && b.BlockedUserId == userId2) ||
                (b.UserId == userId2 && b.BlockedUserId == userId1))
            .AnyAsync();
}
