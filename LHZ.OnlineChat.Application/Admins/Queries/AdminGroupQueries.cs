using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Admins.Queries;

/// <summary>管理后台群列表（按名称关键词分页）</summary>
public sealed class ListGroupsQuery : IQuery<ApiResponse<PagedResult<AdminGroupDto>>>
{
    public string? Keyword { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

internal sealed class ListGroupsHandler : IRequestHandler<ListGroupsQuery, ApiResponse<PagedResult<AdminGroupDto>>>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;
    private readonly IGroupMessageRepository _messages;
    private readonly IUserRepository _users;

    public ListGroupsHandler(
        IGroupRepository groups,
        IGroupMemberRepository members,
        IGroupMessageRepository messages,
        IUserRepository users)
    {
        _groups = groups;
        _members = members;
        _messages = messages;
        _users = users;
    }

    public async Task<ApiResponse<PagedResult<AdminGroupDto>>> Handle(
        ListGroupsQuery query, CancellationToken ct)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var (groups, total) = await _groups.SearchAsync(query.Keyword, page, ct).ConfigureAwait(false);

        if (groups.Count == 0)
            return ApiResponse<PagedResult<AdminGroupDto>>.Ok(PagedResult<AdminGroupDto>.Empty(page));

        var groupIds = groups.Select(g => g.Id).ToList();
        var owners = await _users
            .GetManyAsync(groups.Select(g => g.OwnerId).Distinct(), ct)
            .ConfigureAwait(false);
        var memberCounts = await _members.CountByGroupAsync(groupIds, ct).ConfigureAwait(false);
        var messageCounts = await _messages.CountByGroupAsync(groupIds, ct).ConfigureAwait(false);

        var items = groups.Select(g => new AdminGroupDto
        {
            Id = g.Id,
            Name = g.Name,
            Avatar = g.Avatar,
            OwnerId = g.OwnerId,
            OwnerName = owners.GetValueOrDefault(g.OwnerId)?.Nickname ?? $"用户{g.OwnerId}",
            MemberCount = memberCounts.GetValueOrDefault(g.Id),
            MessageCount = messageCounts.GetValueOrDefault(g.Id),
            Announcement = g.Announcement?.Text,
            CreatedAt = UtcTime.Normalize(g.CreatedAt)
        });

        return ApiResponse<PagedResult<AdminGroupDto>>.Ok(
            PagedResult<AdminGroupDto>.Create(items, total, page));
    }
}

/// <summary>群详情（含成员列表与禁言状态）</summary>
public sealed class GetGroupDetailQuery : IQuery<ApiResponse<AdminGroupDetailDto>>
{
    public long GroupId { get; set; }
}

internal sealed class GetGroupDetailHandler
    : IRequestHandler<GetGroupDetailQuery, ApiResponse<AdminGroupDetailDto>>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;
    private readonly IGroupMessageRepository _messages;
    private readonly IUserRepository _users;
    private readonly IConnectionRegistry _connections;

    public GetGroupDetailHandler(
        IGroupRepository groups,
        IGroupMemberRepository members,
        IGroupMessageRepository messages,
        IUserRepository users,
        IConnectionRegistry connections)
    {
        _groups = groups;
        _members = members;
        _messages = messages;
        _users = users;
        _connections = connections;
    }

    public async Task<ApiResponse<AdminGroupDetailDto>> Handle(
        GetGroupDetailQuery query, CancellationToken ct)
    {
        var group = await _groups.FindByIdAsync(query.GroupId, ct).ConfigureAwait(false)
                    ?? throw new EntityNotFoundException("群不存在");

        var members = await _members.ListOfGroupAsync(query.GroupId, ct).ConfigureAwait(false);
        var userIds = members.Select(m => m.UserId).ToList();
        var users = await _users.GetManyAsync(userIds, ct).ConfigureAwait(false);
        var owner = await _users.FindByIdAsync(group.OwnerId, ct).ConfigureAwait(false);
        var messageCounts = await _messages
            .CountByGroupAsync(new[] { query.GroupId }, ct)
            .ConfigureAwait(false);
        var online = _connections.GetOnlineStates(userIds);

        return ApiResponse<AdminGroupDetailDto>.Ok(new AdminGroupDetailDto
        {
            Group = new AdminGroupDto
            {
                Id = group.Id,
                Name = group.Name,
                Avatar = group.Avatar,
                OwnerId = group.OwnerId,
                OwnerName = owner?.Nickname ?? $"用户{group.OwnerId}",
                MemberCount = members.Count,
                MessageCount = messageCounts.GetValueOrDefault(query.GroupId),
                Announcement = group.Announcement?.Text,
                CreatedAt = UtcTime.Normalize(group.CreatedAt)
            },
            Members = members.Select(m => new AdminGroupMemberDto
            {
                UserId = m.UserId,
                Nickname = users.GetValueOrDefault(m.UserId)?.Nickname ?? $"用户{m.UserId}",
                Avatar = users.GetValueOrDefault(m.UserId)?.Avatar,
                Role = (int)m.Role,
                IsOnline = online.TryGetValue(m.UserId, out var isOnline) && isOnline,
                IsBot = users.GetValueOrDefault(m.UserId)?.IsBot ?? false,
                MutedUntil = UtcTime.Normalize(m.MutedUntil)
            }).ToList()
        });
    }
}
