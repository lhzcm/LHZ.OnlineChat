using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Models.Entities;

namespace LHZ.OnlineChat.Server.Services;

/// <summary>
/// 群组管理服务
/// </summary>
public class GroupService
{
    private readonly IFreeSql _fsql;
    private readonly RedisService _redis;
    private readonly WsMessageHandler _wsMessageHandler;

    public GroupService(IFreeSql fsql, RedisService redis, WsMessageHandler wsMessageHandler)
    {
        _fsql = fsql;
        _redis = redis;
        _wsMessageHandler = wsMessageHandler;
    }

    /// <summary>
    /// 创建群组
    /// </summary>
    public async Task<ApiResponse<GroupInfo>> CreateGroupAsync(int ownerId, CreateGroupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return ApiResponse<GroupInfo>.Fail("群组名称不能为空");

        // 创建群组
        var group = new Group_
        {
            Name = request.Name,
            Avatar = request.Avatar,
            OwnerId = ownerId,
            CreatedAt = DateTime.UtcNow
        };

        var groupId = await _fsql.Insert(group).ExecuteIdentityAsync();

        // 创建者自动成为群主
        var member = new GroupMember
        {
            GroupId = groupId,
            UserId = ownerId,
            Role = 0, // 群主
            JoinedAt = DateTime.UtcNow
        };
        await _fsql.Insert(member).ExecuteAffrowsAsync();

        return ApiResponse<GroupInfo>.Ok(new GroupInfo
        {
            Id = groupId,
            Name = request.Name,
            Avatar = request.Avatar,
            OwnerId = ownerId,
            MemberCount = 1,
            CreatedAt = group.CreatedAt
        }, "群组创建成功");
    }

    /// <summary>
    /// 加入群组
    /// </summary>
    public async Task<ApiResponse> JoinGroupAsync(long groupId, int userId)    {
        // 检查群组是否存在
        var group = await _fsql.Select<Group_>().Where(g => g.Id == groupId).FirstAsync();
        if (group == null)
            return ApiResponse.Fail("群组不存在");

        // 检查是否已是成员
        var exists = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == userId)
            .AnyAsync();

        if (exists)
            return ApiResponse.Fail("你已经是该群组成员");

        // 加入群组（已读游标设为当前最新消息，避免加入前的历史被当作离线消息补发）
        var maxId = await _fsql.Select<GroupMessage>()
            .Where(gm => gm.GroupId == groupId)
            .MaxAsync(gm => gm.Id);

        var member = new GroupMember
        {
            GroupId = groupId,
            UserId = userId,
            Role = 2, // 普通成员
            LastReadMessageId = maxId,
            JoinedAt = DateTime.UtcNow
        };
        await _fsql.Insert(member).ExecuteAffrowsAsync();

        return ApiResponse.Ok("加入群组成功");
    }

    /// <summary>
    /// 邀请好友加入群组（仅群主/管理员可邀请，且只能邀请自己的好友）
    /// </summary>
    public async Task<ApiResponse> InviteMembersAsync(long groupId, int operatorId, List<int> userIds)
    {
        if (userIds == null || userIds.Count == 0)
            return ApiResponse.Fail("请选择要邀请的好友");

        var group = await _fsql.Select<Group_>().Where(g => g.Id == groupId).FirstAsync();
        if (group == null)
            return ApiResponse.Fail("群组不存在");

        // 权限校验：群主(0)或管理员(1)
        var operatorMember = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == operatorId)
            .FirstAsync();
        if (operatorMember == null)
            return ApiResponse.Fail("你不是该群组成员");
        if (operatorMember.Role > 1)
            return ApiResponse.Fail("只有群主或管理员可以邀请成员");

        var targetIds = userIds.Distinct().ToList();

        // 只能邀请自己的好友
        var friendIds = (await _fsql.Select<Friend>()
            .Where(f => f.Status == 1 && (f.UserId == operatorId || f.FriendId == operatorId))
            .ToListAsync())
            .Select(f => f.UserId == operatorId ? f.FriendId : f.UserId)
            .ToHashSet();

        var notFriend = targetIds.Where(id => !friendIds.Contains(id)).ToList();
        if (notFriend.Count > 0)
            return ApiResponse.Fail("只能邀请自己的好友加入群组");

        // 排除已在群中的
        var existingIds = (await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && targetIds.Contains(m.UserId))
            .ToListAsync())
            .Select(m => m.UserId)
            .ToHashSet();

        var toInvite = targetIds.Where(id => !existingIds.Contains(id)).ToList();
        if (toInvite.Count == 0)
            return ApiResponse.Fail("所选好友都已在该群中");

        // 批量加入（已读游标 = 当前最新消息，与普通加入一致）
        var maxId = await _fsql.Select<GroupMessage>()
            .Where(gm => gm.GroupId == groupId)
            .MaxAsync(gm => gm.Id);

        var members = toInvite.Select(uid => new GroupMember
        {
            GroupId = groupId,
            UserId = uid,
            Role = 2, // 普通成员
            LastReadMessageId = maxId,
            JoinedAt = DateTime.UtcNow
        }).ToList();

        await _fsql.Insert(members).ExecuteAffrowsAsync();

        // 实时通知被邀请者
        foreach (var uid in toInvite)
        {
            _wsMessageHandler.NotifyGroupInvitedAsync(uid, groupId);
        }

        return ApiResponse.Ok($"已邀请 {toInvite.Count} 位好友加入群组");
    }

    /// <summary>
    /// 退出群组
    /// </summary>
    public async Task<ApiResponse> LeaveGroupAsync(long groupId, int userId)
    {
        var member = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == userId)
            .FirstAsync();

        if (member == null)
            return ApiResponse.Fail("你不是该群组成员");

        if (member.Role == 0) // 群主
            return ApiResponse.Fail("群主不能直接退出，请先转让群主或解散群组");

        await _fsql.Delete<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == userId)
            .ExecuteAffrowsAsync();

        return ApiResponse.Ok("已退出群组");
    }

    /// <summary>
    /// 踢出成员（群主/管理员）
    /// </summary>
    public async Task<ApiResponse> KickMemberAsync(long groupId, int operatorId, int targetUserId)
    {
        // 检查操作者权限
        var operatorMember = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == operatorId)
            .FirstAsync();

        if (operatorMember == null)
            return ApiResponse.Fail("你不是该群组成员");

        if (operatorMember.Role > 1) // 不是群主或管理员
            return ApiResponse.Fail("无权踢人");

        // 检查目标成员
        var targetMember = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == targetUserId)
            .FirstAsync();

        if (targetMember == null)
            return ApiResponse.Fail("目标用户不是群成员");

        if (targetMember.Role == 0) // 不能踢群主
            return ApiResponse.Fail("不能踢出群主");

        if (operatorMember.Role == 1 && targetMember.Role == 1) // 管理员不能踢管理员
            return ApiResponse.Fail("管理员不能踢出其他管理员");

        await _fsql.Delete<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == targetUserId)
            .ExecuteAffrowsAsync();

        return ApiResponse.Ok("已踢出成员");
    }

    /// <summary>
    /// 解散群组（仅群主）
    /// </summary>
    public async Task<ApiResponse> DismissGroupAsync(long groupId, int userId)
    {
        var group = await _fsql.Select<Group_>().Where(g => g.Id == groupId).FirstAsync();
        if (group == null)
            return ApiResponse.Fail("群组不存在");

        if (group.OwnerId != userId)
            return ApiResponse.Fail("只有群主才能解散群组");

        // 删除群成员和群组
        await _fsql.Delete<GroupMember>().Where(m => m.GroupId == groupId).ExecuteAffrowsAsync();
        await _fsql.Delete<Group_>().Where(g => g.Id == groupId).ExecuteAffrowsAsync();

        return ApiResponse.Ok("群组已解散");
    }

    /// <summary>
    /// 获取我的群组列表
    /// </summary>
    public async Task<ApiResponse<List<GroupInfo>>> GetMyGroupsAsync(int userId)
    {
        var memberGroups = await _fsql.Select<GroupMember>()
            .Where(m => m.UserId == userId)
            .ToListAsync();

        if (memberGroups.Count == 0)
            return ApiResponse<List<GroupInfo>>.Ok(new List<GroupInfo>());

        var groupIds = memberGroups.Select(m => m.GroupId).ToList();
        var groups = await _fsql.Select<Group_>()
            .Where(g => groupIds.Contains(g.Id))
            .ToListAsync();

        var result = new List<GroupInfo>();
        foreach (var group in groups)
        {
            var memberCount = await _fsql.Select<GroupMember>()
                .Where(m => m.GroupId == group.Id)
                .CountAsync();

            result.Add(new GroupInfo
            {
                Id = group.Id,
                Name = group.Name,
                Avatar = group.Avatar,
                OwnerId = group.OwnerId,
                MemberCount = (int)memberCount,
                CreatedAt = group.CreatedAt
            });
        }

        return ApiResponse<List<GroupInfo>>.Ok(result);
    }

    /// <summary>
    /// 获取群成员列表（含在线状态）
    /// </summary>
    public async Task<ApiResponse<List<GroupMemberInfo>>> GetGroupMembersAsync(long groupId)
    {
        var members = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId)
            .ToListAsync();

        if (members.Count == 0)
            return ApiResponse<List<GroupMemberInfo>>.Ok(new List<GroupMemberInfo>());

        var userIds = members.Select(m => m.UserId).ToList();
        var users = await _fsql.Select<User>()
            .Where(u => userIds.Contains(u.Id))
            .ToListAsync();

        var userDict = users.ToDictionary(u => u.Id);

        var result = new List<GroupMemberInfo>();
        foreach (var member in members)
        {
            var user = userDict.GetValueOrDefault(member.UserId);
            var isOnline = await _redis.KeyExistsAsync($"ws:online:{member.UserId}");

            result.Add(new GroupMemberInfo
            {
                UserId = member.UserId,
                Nickname = user?.Nickname ?? "未知",
                Avatar = user?.Avatar,
                Role = member.Role,
                IsOnline = isOnline
            });
        }

        // 群主排第一，管理员排第二，按角色和在线状态排序
        result = result.OrderBy(m => m.Role).ThenByDescending(m => m.IsOnline).ToList();

        return ApiResponse<List<GroupMemberInfo>>.Ok(result);
    }
}
