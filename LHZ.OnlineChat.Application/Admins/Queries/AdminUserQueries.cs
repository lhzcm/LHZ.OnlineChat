using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Application.Users;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Friends;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Admins.Queries;

/// <summary>管理后台用户列表（搜索/筛选/分页）</summary>
public sealed class ListUsersQuery : IQuery<ApiResponse<PagedResult<AdminUserDto>>>
{
    public string? Keyword { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public bool? IsBot { get; set; }

    public bool? Banned { get; set; }
}

internal sealed class ListUsersHandler : IRequestHandler<ListUsersQuery, ApiResponse<PagedResult<AdminUserDto>>>
{
    private readonly IUserRepository _users;
    private readonly AdminUserDtoBuilder _builder;

    public ListUsersHandler(IUserRepository users, AdminUserDtoBuilder builder)
    {
        _users = users;
        _builder = builder;
    }

    public async Task<ApiResponse<PagedResult<AdminUserDto>>> Handle(ListUsersQuery query, CancellationToken ct)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var (users, total) = await _users
            .SearchAsync(query.Keyword, page, query.IsBot, query.Banned, ct)
            .ConfigureAwait(false);

        var items = await _builder.BuildAsync(users, ct).ConfigureAwait(false);
        return ApiResponse<PagedResult<AdminUserDto>>.Ok(
            PagedResult<AdminUserDto>.Create(items, total, page));
    }
}

/// <summary>用户详情（含登录设备）</summary>
public sealed class GetUserDetailQuery : IQuery<ApiResponse<AdminUserDetailDto>>
{
    public int UserId { get; set; }
}

internal sealed class GetUserDetailHandler : IRequestHandler<GetUserDetailQuery, ApiResponse<AdminUserDetailDto>>
{
    private readonly IUserRepository _users;
    private readonly ISessionStore _sessions;
    private readonly AdminUserDtoBuilder _builder;

    public GetUserDetailHandler(
        IUserRepository users, ISessionStore sessions, AdminUserDtoBuilder builder)
    {
        _users = users;
        _sessions = sessions;
        _builder = builder;
    }

    public async Task<ApiResponse<AdminUserDetailDto>> Handle(GetUserDetailQuery query, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(query.UserId, ct).ConfigureAwait(false)
                   ?? throw new EntityNotFoundException("用户不存在");

        var dto = (await _builder.BuildAsync(new[] { user }, ct).ConfigureAwait(false))[0];
        var sessions = await _sessions.ListSessionsAsync(query.UserId, ct).ConfigureAwait(false);

        return ApiResponse<AdminUserDetailDto>.Ok(new AdminUserDetailDto
        {
            User = dto,
            Sessions = sessions
                .Select(s => s.ToDto(string.Empty))
                .OrderByDescending(s => s.LastActiveAt)
                .ToList()
        });
    }
}

/// <summary>
/// 管理后台用户 DTO 的装配器（好友数/群数/消息数/在线状态）。
/// 列表和详情共用，四组聚合统计一次批量查完，不在循环里逐个查。
/// </summary>
public sealed class AdminUserDtoBuilder
{
    private readonly IFriendshipRepository _friendships;
    private readonly IGroupMemberRepository _members;
    private readonly IPrivateMessageRepository _privateMessages;
    private readonly IGroupMessageRepository _groupMessages;
    private readonly IConnectionRegistry _connections;

    public AdminUserDtoBuilder(
        IFriendshipRepository friendships,
        IGroupMemberRepository members,
        IPrivateMessageRepository privateMessages,
        IGroupMessageRepository groupMessages,
        IConnectionRegistry connections)
    {
        _friendships = friendships;
        _members = members;
        _privateMessages = privateMessages;
        _groupMessages = groupMessages;
        _connections = connections;
    }

    public async Task<List<AdminUserDto>> BuildAsync(
        IReadOnlyCollection<User> users, CancellationToken ct)
    {
        if (users.Count == 0) return new List<AdminUserDto>();

        var userIds = users.Select(u => u.Id).ToList();

        var friendCounts = await _friendships.CountAcceptedByUserAsync(userIds, ct).ConfigureAwait(false);
        var groupCounts = await _members.CountByUserAsync(userIds, ct).ConfigureAwait(false);
        var privateCounts = await _privateMessages.CountBySenderAsync(userIds, ct).ConfigureAwait(false);
        var groupMessageCounts = await _groupMessages.CountBySenderAsync(userIds, ct).ConfigureAwait(false);
        var online = _connections.GetOnlineStates(userIds);

        return users.Select(u => new AdminUserDto
        {
            Id = u.Id,
            Nickname = u.Nickname,
            Email = u.Email?.Value,
            Avatar = u.Avatar,
            IsBot = u.IsBot,
            IsBanned = u.IsBanned,
            BanReason = u.BanReason,
            BannedAt = UtcTime.Normalize(u.BannedAt),
            CreatedAt = UtcTime.Normalize(u.CreatedAt),
            IsOnline = online.TryGetValue(u.Id, out var isOnline) && isOnline,
            FriendCount = friendCounts.GetValueOrDefault(u.Id),
            GroupCount = groupCounts.GetValueOrDefault(u.Id),
            MessageCount = privateCounts.GetValueOrDefault(u.Id) + groupMessageCounts.GetValueOrDefault(u.Id)
        }).ToList();
    }
}
