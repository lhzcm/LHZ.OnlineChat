using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Friends;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Friends.Queries;

/// <summary>好友列表（含在线状态、我的备注与分类；在线优先、再按昵称排序）</summary>
public sealed class GetFriendsQuery : IQuery<ApiResponse<List<FriendInfo>>>
{
    public int UserId { get; set; }
}

internal sealed class GetFriendsHandler : IRequestHandler<GetFriendsQuery, ApiResponse<List<FriendInfo>>>
{
    private readonly IFriendshipRepository _friendships;
    private readonly IFriendSettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly IPresenceStore _presence;

    public GetFriendsHandler(
        IFriendshipRepository friendships,
        IFriendSettingRepository settings,
        IUserRepository users,
        IPresenceStore presence)
    {
        _friendships = friendships;
        _settings = settings;
        _users = users;
        _presence = presence;
    }

    public async Task<ApiResponse<List<FriendInfo>>> Handle(GetFriendsQuery query, CancellationToken ct)
    {
        var friendIds = await _friendships.ListFriendIdsOfAsync(query.UserId, ct).ConfigureAwait(false);
        if (friendIds.Count == 0)
            return ApiResponse<List<FriendInfo>>.Ok(new List<FriendInfo>());

        var users = await _users.GetManyAsync(friendIds, ct).ConfigureAwait(false);
        var settings = await _settings.GetManyAsync(query.UserId, friendIds, ct).ConfigureAwait(false);

        // 批量查在线状态：原实现在 foreach 里逐个 await Redis，好友多时是 N 次往返
        var online = await _presence.GetStatesAsync(friendIds, ct).ConfigureAwait(false);

        var items = friendIds
            .Where(users.ContainsKey)
            .Select(id =>
            {
                var user = users[id];
                settings.TryGetValue(id, out var setting);
                return new FriendInfo
                {
                    UserId = id,
                    Nickname = user.Nickname,
                    Avatar = user.Avatar,
                    IsOnline = online.TryGetValue(id, out var isOnline) && isOnline,
                    Status = (int)FriendshipStatus.Accepted,
                    IsBot = user.IsBot,
                    Remark = setting?.Remark,
                    Category = setting?.Category
                };
            })
            .OrderByDescending(f => f.IsOnline)
            .ThenBy(f => f.Nickname, StringComparer.CurrentCulture)
            .ToList();

        return ApiResponse<List<FriendInfo>>.Ok(items);
    }
}

/// <summary>待处理的好友申请（别人发给我的）</summary>
public sealed class GetPendingFriendRequestsQuery : IQuery<ApiResponse<List<FriendRequestInfo>>>
{
    public int UserId { get; set; }
}

internal sealed class GetPendingFriendRequestsHandler
    : IRequestHandler<GetPendingFriendRequestsQuery, ApiResponse<List<FriendRequestInfo>>>
{
    private readonly IFriendshipRepository _friendships;
    private readonly IUserRepository _users;

    public GetPendingFriendRequestsHandler(IFriendshipRepository friendships, IUserRepository users)
    {
        _friendships = friendships;
        _users = users;
    }

    public async Task<ApiResponse<List<FriendRequestInfo>>> Handle(
        GetPendingFriendRequestsQuery query, CancellationToken ct)
    {
        var requests = await _friendships.ListPendingForAsync(query.UserId, ct).ConfigureAwait(false);
        if (requests.Count == 0)
            return ApiResponse<List<FriendRequestInfo>>.Ok(new List<FriendRequestInfo>());

        var requesterIds = requests.Select(r => r.UserId).Distinct().ToList();
        var users = await _users.GetManyAsync(requesterIds, ct).ConfigureAwait(false);

        var items = requests.Select(r => new FriendRequestInfo
        {
            Id = r.Id,
            UserId = r.UserId,
            Nickname = users.TryGetValue(r.UserId, out var u) ? u.Nickname : "未知",
            Avatar = users.TryGetValue(r.UserId, out var u2) ? u2.Avatar : null,
            CreatedAt = UtcTime.Normalize(r.CreatedAt)
        }).ToList();

        return ApiResponse<List<FriendRequestInfo>>.Ok(items);
    }
}
