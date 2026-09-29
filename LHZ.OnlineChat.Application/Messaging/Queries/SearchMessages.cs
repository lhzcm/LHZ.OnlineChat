using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Friends;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Messaging.Queries;

/// <summary>
/// 消息搜索：不带 scope 为全局（私聊 + 我的群聊合并），带 scope 为会话内搜索。
/// 内容匹配走 pg_trgm GIN 索引（见 DatabaseInitializer）。
/// </summary>
public sealed class SearchMessagesQuery : IQuery<ApiResponse<PagedResult<MessageSearchResultDto>>>
{
    public const int MaxKeywordLength = 50;

    public int UserId { get; set; }

    public string Keyword { get; set; } = string.Empty;

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 30;

    /// <summary>private | group | null（全局）</summary>
    public string? ScopeType { get; set; }

    /// <summary>会话内搜索的目标：private=好友账号 ID，group=群 ID</summary>
    public long? ScopeId { get; set; }
}

internal sealed class SearchMessagesHandler
    : IRequestHandler<SearchMessagesQuery, ApiResponse<PagedResult<MessageSearchResultDto>>>
{
    private readonly IPrivateMessageRepository _privateMessages;
    private readonly IGroupMessageRepository _groupMessages;
    private readonly IFriendshipRepository _friendships;
    private readonly IGroupMemberRepository _members;
    private readonly IGroupRepository _groups;
    private readonly IUserRepository _users;

    public SearchMessagesHandler(
        IPrivateMessageRepository privateMessages,
        IGroupMessageRepository groupMessages,
        IFriendshipRepository friendships,
        IGroupMemberRepository members,
        IGroupRepository groups,
        IUserRepository users)
    {
        _privateMessages = privateMessages;
        _groupMessages = groupMessages;
        _friendships = friendships;
        _members = members;
        _groups = groups;
        _users = users;
    }

    public async Task<ApiResponse<PagedResult<MessageSearchResultDto>>> Handle(
        SearchMessagesQuery query, CancellationToken ct)
    {
        var keyword = (query.Keyword ?? string.Empty).Trim();
        DomainException.Ensure(keyword.Length > 0, "请输入搜索关键词");
        if (keyword.Length > SearchMessagesQuery.MaxKeywordLength)
            keyword = keyword[..SearchMessagesQuery.MaxKeywordLength];

        var page = new PageRequest(query.Page, query.PageSize);
        var scope = ChatSessionTypeNames.TryParse(query.ScopeType);

        return scope switch
        {
            ChatSessionType.Private => await SearchPrivateScopeAsync(query, keyword, page, ct)
                .ConfigureAwait(false),
            ChatSessionType.Group => await SearchGroupScopeAsync(query, keyword, page, ct)
                .ConfigureAwait(false),
            _ => await SearchGloballyAsync(query, keyword, page, ct).ConfigureAwait(false)
        };
    }

    /// <summary>会话内搜索：单个私聊</summary>
    private async Task<ApiResponse<PagedResult<MessageSearchResultDto>>> SearchPrivateScopeAsync(
        SearchMessagesQuery query, string keyword, PageRequest page, CancellationToken ct)
    {
        var peerId = (int)(query.ScopeId ?? 0);
        DomainException.Ensure(peerId > 0, "无效的会话");

        var areFriends = await _friendships.AreFriendsAsync(query.UserId, peerId, ct).ConfigureAwait(false);
        DomainException.Ensure(areFriends, "不是好友关系");

        var (messages, total) = await _privateMessages
            .SearchBetweenAsync(query.UserId, peerId, keyword, page, ct)
            .ConfigureAwait(false);

        var peer = await _users.FindByIdAsync(peerId, ct).ConfigureAwait(false);
        var peerName = peer?.Nickname ?? $"用户{peerId}";

        var items = messages.Select(m => new MessageSearchResultDto
        {
            Type = ChatSessionTypeNames.Private,
            SessionId = peerId,
            SessionName = peerName,
            SenderName = m.SenderId == query.UserId ? "我" : peerName,
            SenderAvatar = m.SenderId == query.UserId ? null : peer?.Avatar,
            Content = m.Content,
            MessageType = (int)m.Kind,
            MessageId = m.ClientMessageId,
            SentAt = m.SentAtUtc
        }).ToList();

        return ApiResponse<PagedResult<MessageSearchResultDto>>.Ok(
            PagedResult<MessageSearchResultDto>.Create(items, total, page));
    }

    /// <summary>会话内搜索：单个群</summary>
    private async Task<ApiResponse<PagedResult<MessageSearchResultDto>>> SearchGroupScopeAsync(
        SearchMessagesQuery query, string keyword, PageRequest page, CancellationToken ct)
    {
        var groupId = query.ScopeId ?? 0;
        DomainException.Ensure(groupId > 0, "无效的会话");

        var isMember = await _members.ExistsAsync(groupId, query.UserId, ct).ConfigureAwait(false);
        DomainException.Ensure(isMember, "你不是该群成员");

        var (messages, total) = await _groupMessages
            .SearchInGroupAsync(groupId, keyword, page, ct)
            .ConfigureAwait(false);

        var group = await _groups.FindByIdAsync(groupId, ct).ConfigureAwait(false);
        var senders = await _users
            .GetManyAsync(messages.Select(m => m.SenderId).Distinct(), ct)
            .ConfigureAwait(false);

        var items = messages.Select(m => new MessageSearchResultDto
        {
            Type = ChatSessionTypeNames.Group,
            SessionId = groupId,
            SessionName = group?.Name ?? $"群{groupId}",
            SenderName = m.SenderId == query.UserId
                ? "我"
                : senders.GetValueOrDefault(m.SenderId)?.Nickname ?? "未知",
            SenderAvatar = senders.GetValueOrDefault(m.SenderId)?.Avatar,
            Content = m.Content,
            MessageType = (int)m.Kind,
            MessageId = m.ClientMessageId,
            SentAt = m.SentAtUtc
        }).ToList();

        return ApiResponse<PagedResult<MessageSearchResultDto>>.Ok(
            PagedResult<MessageSearchResultDto>.Create(items, total, page));
    }

    /// <summary>全局搜索：两表各取前 N 条，合并按时间倒序后统一裁剪分页</summary>
    private async Task<ApiResponse<PagedResult<MessageSearchResultDto>>> SearchGloballyAsync(
        SearchMessagesQuery query, string keyword, PageRequest page, CancellationToken ct)
    {
        var take = page.TakeThroughCurrentPage;

        var (privateMessages, privateTotal) = await _privateMessages
            .SearchOfUserAsync(query.UserId, keyword, take, ct)
            .ConfigureAwait(false);

        var groupIds = (await _members.ListOfUserAsync(query.UserId, ct).ConfigureAwait(false))
            .Select(m => m.GroupId)
            .ToList();

        IReadOnlyList<GroupMessage> groupMessages = Array.Empty<GroupMessage>();
        var groupTotal = 0;
        if (groupIds.Count > 0)
        {
            (groupMessages, groupTotal) = await _groupMessages
                .SearchInGroupsAsync(groupIds, keyword, take, ct)
                .ConfigureAwait(false);
        }

        // 一次性把要展示的用户/群名都取回来
        var userIds = privateMessages
            .Select(m => m.SenderId == query.UserId ? m.ReceiverId : m.SenderId)
            .Concat(groupMessages.Select(m => m.SenderId))
            .Append(query.UserId)
            .Distinct()
            .ToList();
        var users = await _users.GetManyAsync(userIds, ct).ConfigureAwait(false);
        var groups = groupIds.Count > 0
            ? await _groups.GetManyAsync(groupIds, ct).ConfigureAwait(false)
            : new Dictionary<long, Group>();

        var merged = new List<(DateTime SentAt, MessageSearchResultDto Dto)>(
            privateMessages.Count + groupMessages.Count);

        foreach (var m in privateMessages)
        {
            var peerId = m.SenderId == query.UserId ? m.ReceiverId : m.SenderId;
            merged.Add((m.SentAtUtc, new MessageSearchResultDto
            {
                Type = ChatSessionTypeNames.Private,
                SessionId = peerId,
                SessionName = users.GetValueOrDefault(peerId)?.Nickname ?? $"用户{peerId}",
                SenderName = m.SenderId == query.UserId
                    ? "我"
                    : users.GetValueOrDefault(m.SenderId)?.Nickname ?? "未知",
                SenderAvatar = users.GetValueOrDefault(m.SenderId)?.Avatar,
                Content = m.Content,
                MessageType = (int)m.Kind,
                MessageId = m.ClientMessageId,
                SentAt = m.SentAtUtc
            }));
        }

        foreach (var m in groupMessages)
        {
            merged.Add((m.SentAtUtc, new MessageSearchResultDto
            {
                Type = ChatSessionTypeNames.Group,
                SessionId = m.GroupId,
                SessionName = groups.GetValueOrDefault(m.GroupId)?.Name ?? $"群{m.GroupId}",
                SenderName = m.SenderId == query.UserId
                    ? "我"
                    : users.GetValueOrDefault(m.SenderId)?.Nickname ?? "未知",
                SenderAvatar = users.GetValueOrDefault(m.SenderId)?.Avatar,
                Content = m.Content,
                MessageType = (int)m.Kind,
                MessageId = m.ClientMessageId,
                SentAt = m.SentAtUtc
            }));
        }

        var items = merged
            .OrderByDescending(x => x.SentAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(x => x.Dto)
            .ToList();

        return ApiResponse<PagedResult<MessageSearchResultDto>>.Ok(
            PagedResult<MessageSearchResultDto>.Create(items, privateTotal + groupTotal, page));
    }
}
