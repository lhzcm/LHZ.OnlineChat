using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Friends;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Messaging.Queries;

/// <summary>
/// 会话列表（私聊 + 群聊聚合）：置顶优先，再按最后消息时间倒序。
/// </summary>
public sealed class GetChatSessionsQuery : IQuery<ApiResponse<List<SessionDto>>>
{
    /// <summary>聚合最近会话时扫描的消息条数上限</summary>
    public const int RecentScanSize = 500;

    public int UserId { get; set; }
}

internal sealed class GetChatSessionsHandler : IRequestHandler<GetChatSessionsQuery, ApiResponse<List<SessionDto>>>
{
    private readonly IPrivateMessageRepository _privateMessages;
    private readonly IGroupMessageRepository _groupMessages;
    private readonly IGroupMemberRepository _members;
    private readonly IGroupRepository _groups;
    private readonly IUserRepository _users;
    private readonly IFriendSettingRepository _friendSettings;
    private readonly ISessionSettingRepository _sessionSettings;

    public GetChatSessionsHandler(
        IPrivateMessageRepository privateMessages,
        IGroupMessageRepository groupMessages,
        IGroupMemberRepository members,
        IGroupRepository groups,
        IUserRepository users,
        IFriendSettingRepository friendSettings,
        ISessionSettingRepository sessionSettings)
    {
        _privateMessages = privateMessages;
        _groupMessages = groupMessages;
        _members = members;
        _groups = groups;
        _users = users;
        _friendSettings = friendSettings;
        _sessionSettings = sessionSettings;
    }

    public async Task<ApiResponse<List<SessionDto>>> Handle(GetChatSessionsQuery query, CancellationToken ct)
    {
        var settings = await _sessionSettings.ListOfUserAsync(query.UserId, ct).ConfigureAwait(false);
        var sessions = new List<SessionDto>();

        sessions.AddRange(await BuildPrivateSessionsAsync(query.UserId, settings, ct).ConfigureAwait(false));
        sessions.AddRange(await BuildGroupSessionsAsync(query.UserId, settings, ct).ConfigureAwait(false));

        var ordered = sessions
            .OrderByDescending(s => s.IsPinned)
            .ThenByDescending(s => s.LastTime)
            .ToList();

        return ApiResponse<List<SessionDto>>.Ok(ordered);
    }

    private async Task<List<SessionDto>> BuildPrivateSessionsAsync(
        int userId, IReadOnlyDictionary<string, SessionSetting> settings, CancellationToken ct)
    {
        var recent = await _privateMessages
            .ListRecentOfUserAsync(userId, GetChatSessionsQuery.RecentScanSize, ct)
            .ConfigureAwait(false);

        // 每个对端只保留最新一条（recent 已按时间倒序）
        var lastByPeer = new Dictionary<int, PrivateMessage>();
        foreach (var m in recent)
        {
            var peerId = m.SenderId == userId ? m.ReceiverId : m.SenderId;
            lastByPeer.TryAdd(peerId, m);
        }

        if (lastByPeer.Count == 0) return new List<SessionDto>();

        var peerIds = lastByPeer.Keys.ToList();
        var unread = await _privateMessages.CountUnreadBySenderAsync(userId, ct).ConfigureAwait(false);
        var users = await _users.GetManyAsync(peerIds, ct).ConfigureAwait(false);
        var remarks = await _friendSettings.GetManyAsync(userId, peerIds, ct).ConfigureAwait(false);

        return lastByPeer.Select(pair =>
        {
            var (peerId, last) = pair;
            var peer = users.GetValueOrDefault(peerId);
            var remark = remarks.GetValueOrDefault(peerId)?.Remark;
            var setting = settings.GetValueOrDefault(SessionSettingKey(ChatSessionTypeNames.Private, peerId));

            return new SessionDto
            {
                Type = ChatSessionTypeNames.Private,
                Id = peerId,
                // 会话名优先显示我设的备注
                Name = string.IsNullOrWhiteSpace(remark) ? peer?.Nickname ?? $"用户{peerId}" : remark,
                Avatar = peer?.Avatar,
                LastMessage = last.Content,
                LastTime = last.SentAtUtc,
                UnreadCount = unread.GetValueOrDefault(peerId),
                IsPinned = setting?.IsPinned ?? false,
                Muted = setting?.Muted ?? false,
                IsBot = peer?.IsBot ?? false
            };
        }).ToList();
    }

    private async Task<List<SessionDto>> BuildGroupSessionsAsync(
        int userId, IReadOnlyDictionary<string, SessionSetting> settings, CancellationToken ct)
    {
        var memberships = await _members.ListOfUserAsync(userId, ct).ConfigureAwait(false);
        if (memberships.Count == 0) return new List<SessionDto>();

        var groupIds = memberships.Select(m => m.GroupId).ToList();
        var groups = await _groups.GetManyAsync(groupIds, ct).ConfigureAwait(false);
        var lastMessages = await _groupMessages.LatestOfGroupsAsync(groupIds, ct).ConfigureAwait(false);

        var result = new List<SessionDto>(memberships.Count);
        foreach (var membership in memberships)
        {
            var groupId = membership.GroupId;
            var group = groups.GetValueOrDefault(groupId);
            var last = lastMessages.GetValueOrDefault(groupId);

            // 未读 = 已读游标之后的条数
            var unread = await _groupMessages
                .CountAfterCursorAsync(groupId, membership.LastReadMessageId, ct)
                .ConfigureAwait(false);

            var setting = settings.GetValueOrDefault(SessionSettingKey(ChatSessionTypeNames.Group, groupId));

            result.Add(new SessionDto
            {
                Type = ChatSessionTypeNames.Group,
                Id = groupId,
                Name = group?.Name ?? $"群{groupId}",
                Avatar = group?.Avatar,
                LastMessage = last?.Content ?? string.Empty,
                LastTime = last is null ? DateTime.MinValue : last.SentAtUtc,
                UnreadCount = unread,
                IsPinned = setting?.IsPinned ?? false,
                Muted = setting?.Muted ?? false
            });
        }

        return result;
    }

    /// <summary>会话设置字典的键：与 ISessionSettingRepository.ListOfUserAsync 约定一致</summary>
    internal static string SessionSettingKey(string type, long sessionId) => $"{type}_{sessionId}";
}
