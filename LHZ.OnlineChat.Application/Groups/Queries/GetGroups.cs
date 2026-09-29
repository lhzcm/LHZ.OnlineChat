using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Groups.Queries;

/// <summary>我的群组列表</summary>
public sealed class GetMyGroupsQuery : IQuery<ApiResponse<List<GroupInfo>>>
{
    public int UserId { get; set; }
}

internal sealed class GetMyGroupsHandler : IRequestHandler<GetMyGroupsQuery, ApiResponse<List<GroupInfo>>>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;

    public GetMyGroupsHandler(IGroupRepository groups, IGroupMemberRepository members)
    {
        _groups = groups;
        _members = members;
    }

    public async Task<ApiResponse<List<GroupInfo>>> Handle(GetMyGroupsQuery query, CancellationToken ct)
    {
        var memberships = await _members.ListOfUserAsync(query.UserId, ct).ConfigureAwait(false);
        if (memberships.Count == 0)
            return ApiResponse<List<GroupInfo>>.Ok(new List<GroupInfo>());

        var groupIds = memberships.Select(m => m.GroupId).ToList();
        var groups = await _groups.GetManyAsync(groupIds, ct).ConfigureAwait(false);

        // 成员数一次聚合查出；原实现是在 foreach 里逐群 CountAsync（N+1）
        var memberCounts = await _members.CountByGroupAsync(groupIds, ct).ConfigureAwait(false);
        var myRoles = memberships.ToDictionary(m => m.GroupId, m => m.Role);

        var items = groupIds
            .Where(groups.ContainsKey)
            .Select(id =>
            {
                var group = groups[id];
                return new GroupInfo
                {
                    Id = group.Id,
                    Name = group.Name,
                    Avatar = group.Avatar,
                    OwnerId = group.OwnerId,
                    MemberCount = memberCounts.TryGetValue(id, out var count) ? count : 0,
                    CreatedAt = UtcTime.Normalize(group.CreatedAt),
                    Announcement = group.Announcement?.Text,
                    AnnouncementAt = UtcTime.Normalize(group.AnnouncementAt),
                    MyRole = (int)myRoles.GetValueOrDefault(id, GroupRole.Member)
                };
            })
            .ToList();

        return ApiResponse<List<GroupInfo>>.Ok(items);
    }
}

/// <summary>群成员列表（群主→管理员→成员，同级在线优先）</summary>
public sealed class GetGroupMembersQuery : IQuery<ApiResponse<List<GroupMemberInfo>>>
{
    public long GroupId { get; set; }
}

internal sealed class GetGroupMembersHandler
    : IRequestHandler<GetGroupMembersQuery, ApiResponse<List<GroupMemberInfo>>>
{
    private readonly IGroupMemberRepository _members;
    private readonly IUserRepository _users;
    private readonly IPresenceStore _presence;

    public GetGroupMembersHandler(
        IGroupMemberRepository members, IUserRepository users, IPresenceStore presence)
    {
        _members = members;
        _users = users;
        _presence = presence;
    }

    public async Task<ApiResponse<List<GroupMemberInfo>>> Handle(
        GetGroupMembersQuery query, CancellationToken ct)
    {
        var members = await _members.ListOfGroupAsync(query.GroupId, ct).ConfigureAwait(false);
        if (members.Count == 0)
            return ApiResponse<List<GroupMemberInfo>>.Ok(new List<GroupMemberInfo>());

        var userIds = members.Select(m => m.UserId).ToList();
        var users = await _users.GetManyAsync(userIds, ct).ConfigureAwait(false);
        var online = await _presence.GetStatesAsync(userIds, ct).ConfigureAwait(false);

        var items = members
            .Select(m =>
            {
                users.TryGetValue(m.UserId, out var user);
                return new GroupMemberInfo
                {
                    UserId = m.UserId,
                    Nickname = user?.Nickname ?? "未知",
                    Avatar = user?.Avatar,
                    Role = (int)m.Role,
                    IsOnline = online.TryGetValue(m.UserId, out var isOnline) && isOnline,
                    IsBot = user?.IsBot ?? false
                };
            })
            .OrderBy(m => m.Role)
            .ThenByDescending(m => m.IsOnline)
            .ToList();

        return ApiResponse<List<GroupMemberInfo>>.Ok(items);
    }
}
