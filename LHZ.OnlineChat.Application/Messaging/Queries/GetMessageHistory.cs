using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Friends;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Messaging.Queries;

/// <summary>私聊历史（分页；第一页优先命中 Redis 缓存）</summary>
public sealed class GetPrivateHistoryQuery : IQuery<ApiResponse<PagedResult<MessageDto>>>
{
    public int UserId { get; set; }

    public int FriendId { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 50;
}

internal sealed class GetPrivateHistoryHandler
    : IRequestHandler<GetPrivateHistoryQuery, ApiResponse<PagedResult<MessageDto>>>
{
    private const int MaxPageSize = 100;

    private readonly IPrivateMessageRepository _messages;
    private readonly IFriendshipRepository _friendships;
    private readonly IUserRepository _users;
    private readonly IRecentMessageCache _cache;

    public GetPrivateHistoryHandler(
        IPrivateMessageRepository messages,
        IFriendshipRepository friendships,
        IUserRepository users,
        IRecentMessageCache cache)
    {
        _messages = messages;
        _friendships = friendships;
        _users = users;
        _cache = cache;
    }

    public async Task<ApiResponse<PagedResult<MessageDto>>> Handle(
        GetPrivateHistoryQuery query, CancellationToken ct)
    {
        var areFriends = await _friendships
            .AreFriendsAsync(query.UserId, query.FriendId, ct)
            .ConfigureAwait(false);
        DomainException.Ensure(areFriends, "不是好友关系");

        var page = new PageRequest(query.Page, query.PageSize, MaxPageSize);

        // 第一页且缓存足够时直接返回缓存（与改造前一致）
        if (page.Page == 1)
        {
            var cached = await _cache.GetPrivateAsync(query.UserId, query.FriendId, ct).ConfigureAwait(false);
            if (cached.Count >= page.PageSize)
            {
                var (_, cachedTotal) = await _messages
                    .PageBetweenAsync(query.UserId, query.FriendId, new PageRequest(1, 1), ct)
                    .ConfigureAwait(false);

                var cachedItems = cached
                    .OrderBy(m => m.SentAt)
                    .Take(page.PageSize)
                    .Select(m => m.ToDto())
                    .ToList();

                return ApiResponse<PagedResult<MessageDto>>.Ok(
                    PagedResult<MessageDto>.Create(cachedItems, cachedTotal, page));
            }
        }

        var (messages, total) = await _messages
            .PageBetweenAsync(query.UserId, query.FriendId, page, ct)
            .ConfigureAwait(false);

        var senders = await _users
            .GetManyAsync(messages.Select(m => m.SenderId).Distinct(), ct)
            .ConfigureAwait(false);

        var items = messages
            .OrderBy(m => m.SentAt)
            .Select(m => m.ToDto(senders.GetValueOrDefault(m.SenderId)))
            .ToList();

        return ApiResponse<PagedResult<MessageDto>>.Ok(PagedResult<MessageDto>.Create(items, total, page));
    }
}

/// <summary>群聊历史（分页）</summary>
public sealed class GetGroupHistoryQuery : IQuery<ApiResponse<PagedResult<GroupMessageDto>>>
{
    public long GroupId { get; set; }

    public int UserId { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 50;
}

internal sealed class GetGroupHistoryHandler
    : IRequestHandler<GetGroupHistoryQuery, ApiResponse<PagedResult<GroupMessageDto>>>
{
    private const int MaxPageSize = 100;

    private readonly IGroupMessageRepository _messages;
    private readonly IGroupMemberRepository _members;
    private readonly IUserRepository _users;

    public GetGroupHistoryHandler(
        IGroupMessageRepository messages, IGroupMemberRepository members, IUserRepository users)
    {
        _messages = messages;
        _members = members;
        _users = users;
    }

    public async Task<ApiResponse<PagedResult<GroupMessageDto>>> Handle(
        GetGroupHistoryQuery query, CancellationToken ct)
    {
        var isMember = await _members.ExistsAsync(query.GroupId, query.UserId, ct).ConfigureAwait(false);
        DomainException.Ensure(isMember, "你不是该群成员");

        var page = new PageRequest(query.Page, query.PageSize, MaxPageSize);
        var (messages, total) = await _messages.PageOfGroupAsync(query.GroupId, page, ct).ConfigureAwait(false);

        var senders = await _users
            .GetManyAsync(messages.Select(m => m.SenderId).Distinct(), ct)
            .ConfigureAwait(false);

        var items = messages
            .OrderBy(m => m.SentAt)
            .Select(m => m.ToDto(senders.GetValueOrDefault(m.SenderId)))
            .ToList();

        return ApiResponse<PagedResult<GroupMessageDto>>.Ok(
            PagedResult<GroupMessageDto>.Create(items, total, page));
    }
}

/// <summary>离线消息（未读私聊，上线时拉取）</summary>
public sealed class GetOfflineMessagesQuery : IQuery<ApiResponse<List<MessageDto>>>
{
    public int UserId { get; set; }
}

internal sealed class GetOfflineMessagesHandler
    : IRequestHandler<GetOfflineMessagesQuery, ApiResponse<List<MessageDto>>>
{
    private readonly IPrivateMessageRepository _messages;
    private readonly IUserRepository _users;

    public GetOfflineMessagesHandler(IPrivateMessageRepository messages, IUserRepository users)
    {
        _messages = messages;
        _users = users;
    }

    public async Task<ApiResponse<List<MessageDto>>> Handle(
        GetOfflineMessagesQuery query, CancellationToken ct)
    {
        var messages = await _messages.ListUnreadForAsync(query.UserId, ct).ConfigureAwait(false);
        var senders = await _users
            .GetManyAsync(messages.Select(m => m.SenderId).Distinct(), ct)
            .ConfigureAwait(false);

        var items = messages
            .Select(m => m.ToDto(senders.GetValueOrDefault(m.SenderId)))
            .ToList();

        return ApiResponse<List<MessageDto>>.Ok(items);
    }
}

/// <summary>未读汇总</summary>
public sealed class GetUnreadCountQuery : IQuery<ApiResponse<UnreadCountDto>>
{
    public int UserId { get; set; }
}

internal sealed class GetUnreadCountHandler : IRequestHandler<GetUnreadCountQuery, ApiResponse<UnreadCountDto>>
{
    private readonly IPrivateMessageRepository _messages;

    public GetUnreadCountHandler(IPrivateMessageRepository messages) => _messages = messages;

    public async Task<ApiResponse<UnreadCountDto>> Handle(GetUnreadCountQuery query, CancellationToken ct)
    {
        var unread = await _messages.CountUnreadForAsync(query.UserId, ct).ConfigureAwait(false);
        return ApiResponse<UnreadCountDto>.Ok(new UnreadCountDto { PrivateUnread = unread });
    }
}
