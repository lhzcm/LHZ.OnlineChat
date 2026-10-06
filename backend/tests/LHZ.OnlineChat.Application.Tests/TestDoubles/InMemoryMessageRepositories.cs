using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Application.Tests.TestDoubles;

internal sealed class InMemoryPrivateMessageRepository
    : InMemoryStore<PrivateMessage, long>, IPrivateMessageRepository
{
    protected override long ToId(long identity) => identity;

    public Task<PrivateMessage?> FindByIdAsync(long id, CancellationToken ct = default)
        => Task.FromResult(Get(id));

    public Task<(IReadOnlyList<PrivateMessage> Items, int Total)> PageBetweenAsync(
        int userId, int peerId, PageRequest page, CancellationToken ct = default)
    {
        var all = Between(userId, peerId).OrderByDescending(m => m.SentAt).ToList();
        IReadOnlyList<PrivateMessage> items = all.Skip(page.Skip).Take(page.PageSize).ToList();
        return Task.FromResult((items, all.Count));
    }

    public Task<(IReadOnlyList<PrivateMessage> Items, int Total)> SearchBetweenAsync(
        int userId, int peerId, string keyword, PageRequest page, CancellationToken ct = default)
    {
        var all = Between(userId, peerId).Where(m => Matches(m, keyword))
            .OrderByDescending(m => m.SentAt).ToList();
        IReadOnlyList<PrivateMessage> items = all.Skip(page.Skip).Take(page.PageSize).ToList();
        return Task.FromResult((items, all.Count));
    }

    public Task<(IReadOnlyList<PrivateMessage> Items, int Total)> SearchOfUserAsync(
        int userId, string keyword, int take, CancellationToken ct = default)
    {
        var all = Where(m => (m.SenderId == userId || m.ReceiverId == userId) && Matches(m, keyword))
            .OrderByDescending(m => m.SentAt).ToList();
        IReadOnlyList<PrivateMessage> items = all.Take(take).ToList();
        return Task.FromResult((items, all.Count));
    }

    public Task<(IReadOnlyList<PrivateMessage> Items, int Total)> SearchForAdminAsync(
        string? keyword, int? userId, int take, CancellationToken ct = default)
    {
        var query = All.AsEnumerable();
        if (userId.HasValue)
            query = query.Where(m => m.SenderId == userId.Value || m.ReceiverId == userId.Value);
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(m => m.Content.Contains(keyword.Trim(), StringComparison.Ordinal));

        var all = query.OrderByDescending(m => m.SentAt).ToList();
        IReadOnlyList<PrivateMessage> items = all.Take(take).ToList();
        return Task.FromResult((items, all.Count));
    }

    /// <summary>与真实实现一致：按时间倒序取最近 limit 条（不是最早 limit 条）</summary>
    public Task<IReadOnlyList<PrivateMessage>> ListUnreadForAsync(
        int userId, int limit, CancellationToken ct = default)
    {
        IReadOnlyList<PrivateMessage> items = Where(m => m.ReceiverId == userId && !m.IsRead)
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .Take(limit)
            .ToList();
        return Task.FromResult(items);
    }

    public Task<int> CountUnreadForAsync(int userId, CancellationToken ct = default)
        => Task.FromResult(Where(m => m.ReceiverId == userId && !m.IsRead).Count());

    public Task<IReadOnlyDictionary<int, int>> CountUnreadBySenderAsync(
        int userId, CancellationToken ct = default)
    {
        IReadOnlyDictionary<int, int> result = Where(m => m.ReceiverId == userId && !m.IsRead)
            .GroupBy(m => m.SenderId)
            .ToDictionary(g => g.Key, g => g.Count());
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<PrivateMessage>> ListRecentOfUserAsync(
        int userId, int take, CancellationToken ct = default)
    {
        IReadOnlyList<PrivateMessage> items =
            Where(m => m.SenderId == userId || m.ReceiverId == userId)
                .OrderByDescending(m => m.SentAt).Take(take).ToList();
        return Task.FromResult(items);
    }

    public Task<PrivateMessage?> FindRecallableAsync(
        int senderId, int receiverId, string messageId, DateTime earliestSentAt,
        CancellationToken ct = default)
        => Task.FromResult(Where(m =>
            m.SenderId == senderId
            && m.ReceiverId == receiverId
            && !m.IsDeleted
            && m.SentAt >= earliestSentAt
            && m.HasPublicId(messageId)).FirstOrDefault());

    /// <summary>幂等发送查重：同一发送者的客户端消息号（替身里不分接收者，与唯一索引口径一致）</summary>
    public Task<PrivateMessage?> FindByClientMessageIdAsync(
        int senderId, string clientMessageId, CancellationToken ct = default)
        => Task.FromResult(Where(m =>
            m.SenderId == senderId && m.ClientMessageId == clientMessageId).FirstOrDefault());

    public Task<int> MarkAllReadAsync(int senderId, int receiverId, CancellationToken ct = default)    {
        var matched = Where(m => m.SenderId == senderId && m.ReceiverId == receiverId && !m.IsRead)
            .ToList();
        foreach (var m in matched) m.MarkAsRead(receiverId);
        return Task.FromResult(matched.Count);
    }

    public Task AddAsync(PrivateMessage message, CancellationToken ct = default)
    {
        Insert(message);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(PrivateMessage message, CancellationToken ct = default)
    {
        Seed(message);
        return Task.CompletedTask;
    }

    public Task<long> CountAsync(CancellationToken ct = default) => Task.FromResult((long)All.Count);

    public Task<long> CountSentBetweenAsync(
        DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default)
        => Task.FromResult((long)Where(m =>
            m.SentAt >= fromInclusive && m.SentAt < toExclusive).Count());

    public Task<long> CountSentSinceAsync(DateTime since, CancellationToken ct = default)
        => Task.FromResult((long)Where(m => m.SentAt >= since).Count());

    public Task<IReadOnlyList<int>> ListDistinctSendersSinceAsync(
        DateTime since, CancellationToken ct = default)
    {
        IReadOnlyList<int> items = Where(m => m.SentAt >= since)
            .Select(m => m.SenderId).Distinct().ToList();
        return Task.FromResult(items);
    }

    public Task<IReadOnlyDictionary<int, long>> TopSendersAsync(
        int take, CancellationToken ct = default)
    {
        IReadOnlyDictionary<int, long> result = All
            .GroupBy(m => m.SenderId)
            .OrderByDescending(g => g.Count())
            .Take(take)
            .ToDictionary(g => g.Key, g => (long)g.Count());
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<int, long>> CountBySenderAsync(
        IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToHashSet();
        IReadOnlyDictionary<int, long> result = Where(m => ids.Contains(m.SenderId))
            .GroupBy(m => m.SenderId)
            .ToDictionary(g => g.Key, g => (long)g.Count());
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<DateTime, long>> CountByHourSinceAsync(
        int hours, CancellationToken ct = default)
    {
        IReadOnlyDictionary<DateTime, long> result = All
            .GroupBy(m => Truncate(m.SentAtUtc))
            .ToDictionary(g => g.Key, g => (long)g.Count());
        return Task.FromResult(result);
    }

    internal static DateTime Truncate(DateTime value)
        => new(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc);

    private IEnumerable<PrivateMessage> Between(int a, int b)
        => Where(m => (m.SenderId == a && m.ReceiverId == b) || (m.SenderId == b && m.ReceiverId == a));

    private static bool Matches(PrivateMessage m, string keyword)
        => !m.IsDeleted && m.Content.Contains(keyword, StringComparison.Ordinal);
}

internal sealed class InMemoryGroupMessageRepository
    : InMemoryStore<GroupMessage, long>, IGroupMessageRepository
{
    protected override long ToId(long identity) => identity;

    public Task<GroupMessage?> FindByIdAsync(long id, CancellationToken ct = default)
        => Task.FromResult(Get(id));

    public Task<long> MaxIdOfGroupAsync(long groupId, CancellationToken ct = default)
    {
        var ofGroup = Where(m => m.GroupId == groupId).ToList();
        return Task.FromResult(ofGroup.Count == 0 ? 0 : ofGroup.Max(m => m.Id));
    }

    public Task<(IReadOnlyList<GroupMessage> Items, int Total)> PageOfGroupAsync(
        long groupId, PageRequest page, CancellationToken ct = default)
    {
        var all = Where(m => m.GroupId == groupId).OrderByDescending(m => m.SentAt).ToList();
        IReadOnlyList<GroupMessage> items = all.Skip(page.Skip).Take(page.PageSize).ToList();
        return Task.FromResult((items, all.Count));
    }

    public Task<(IReadOnlyList<GroupMessage> Items, int Total)> SearchInGroupAsync(
        long groupId, string keyword, PageRequest page, CancellationToken ct = default)
    {
        var all = Where(m => m.GroupId == groupId && Matches(m, keyword))
            .OrderByDescending(m => m.SentAt).ToList();
        IReadOnlyList<GroupMessage> items = all.Skip(page.Skip).Take(page.PageSize).ToList();
        return Task.FromResult((items, all.Count));
    }

    public Task<(IReadOnlyList<GroupMessage> Items, int Total)> SearchInGroupsAsync(
        IReadOnlyList<long> groupIds, string keyword, int take, CancellationToken ct = default)
    {
        var ids = groupIds.ToHashSet();
        var all = Where(m => ids.Contains(m.GroupId) && Matches(m, keyword))
            .OrderByDescending(m => m.SentAt).ToList();
        IReadOnlyList<GroupMessage> items = all.Take(take).ToList();
        return Task.FromResult((items, all.Count));
    }

    public Task<(IReadOnlyList<GroupMessage> Items, int Total)> SearchForAdminAsync(
        string? keyword, int? senderId, long? groupId, int take, CancellationToken ct = default)
    {
        var query = All.AsEnumerable();
        if (groupId.HasValue) query = query.Where(m => m.GroupId == groupId.Value);
        if (senderId.HasValue) query = query.Where(m => m.SenderId == senderId.Value);
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(m => m.Content.Contains(keyword.Trim(), StringComparison.Ordinal));

        var all = query.OrderByDescending(m => m.SentAt).ToList();
        IReadOnlyList<GroupMessage> items = all.Take(take).ToList();
        return Task.FromResult((items, all.Count));
    }

    public Task<IReadOnlyList<GroupMessage>> ListAfterCursorAsync(
        long groupId, long afterMessageId, int limit, CancellationToken ct = default)
    {
        IReadOnlyList<GroupMessage> items = Where(m =>
                m.GroupId == groupId && m.Id > afterMessageId && !m.IsDeleted)
            .OrderBy(m => m.SentAt).Take(limit).ToList();
        return Task.FromResult(items);
    }

    public Task<int> CountAfterCursorAsync(
        long groupId, long afterMessageId, CancellationToken ct = default)
        => Task.FromResult(Where(m => m.GroupId == groupId && m.Id > afterMessageId).Count());

    public Task<IReadOnlyDictionary<long, GroupMessage>> LatestOfGroupsAsync(
        IReadOnlyList<long> groupIds, CancellationToken ct = default)
    {
        var ids = groupIds.ToHashSet();
        IReadOnlyDictionary<long, GroupMessage> result = Where(m => ids.Contains(m.GroupId))
            .GroupBy(m => m.GroupId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.SentAt).First());
        return Task.FromResult(result);
    }

    public Task<GroupMessage?> FindRecallableAsync(
        long groupId, int senderId, string messageId, DateTime earliestSentAt,
        CancellationToken ct = default)
        => Task.FromResult(Where(m =>
            m.GroupId == groupId
            && m.SenderId == senderId
            && !m.IsDeleted
            && m.SentAt >= earliestSentAt
            && m.HasPublicId(messageId)).FirstOrDefault());

    /// <summary>幂等发送查重（口径与唯一索引 ux_grpmsg_sender_client 一致）</summary>
    public Task<GroupMessage?> FindByClientMessageIdAsync(
        int senderId, string clientMessageId, CancellationToken ct = default)
        => Task.FromResult(Where(m =>
            m.SenderId == senderId && m.ClientMessageId == clientMessageId).FirstOrDefault());

    public Task AddAsync(GroupMessage message, CancellationToken ct = default)
    {
        Insert(message);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(GroupMessage message, CancellationToken ct = default)
    {
        Seed(message);
        return Task.CompletedTask;
    }

    public Task DeleteAllOfGroupAsync(long groupId, CancellationToken ct = default)
    {
        RemoveWhere(m => m.GroupId == groupId);
        return Task.CompletedTask;
    }

    public Task<long> CountAsync(CancellationToken ct = default) => Task.FromResult((long)All.Count);

    public Task<long> CountSentBetweenAsync(
        DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default)
        => Task.FromResult((long)Where(m =>
            m.SentAt >= fromInclusive && m.SentAt < toExclusive).Count());

    public Task<long> CountSentSinceAsync(DateTime since, CancellationToken ct = default)
        => Task.FromResult((long)Where(m => m.SentAt >= since).Count());

    public Task<IReadOnlyList<int>> ListDistinctSendersSinceAsync(
        DateTime since, CancellationToken ct = default)
    {
        IReadOnlyList<int> items = Where(m => m.SentAt >= since)
            .Select(m => m.SenderId).Distinct().ToList();
        return Task.FromResult(items);
    }

    public Task<IReadOnlyDictionary<int, long>> TopSendersAsync(
        int take, CancellationToken ct = default)
    {
        IReadOnlyDictionary<int, long> result = All
            .GroupBy(m => m.SenderId)
            .OrderByDescending(g => g.Count())
            .Take(take)
            .ToDictionary(g => g.Key, g => (long)g.Count());
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<int, long>> CountBySenderAsync(
        IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToHashSet();
        IReadOnlyDictionary<int, long> result = Where(m => ids.Contains(m.SenderId))
            .GroupBy(m => m.SenderId)
            .ToDictionary(g => g.Key, g => (long)g.Count());
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<long, long>> TopGroupsAsync(
        int take, CancellationToken ct = default)
    {
        IReadOnlyDictionary<long, long> result = All
            .GroupBy(m => m.GroupId)
            .OrderByDescending(g => g.Count())
            .Take(take)
            .ToDictionary(g => g.Key, g => (long)g.Count());
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<long, long>> CountByGroupAsync(
        IEnumerable<long> groupIds, CancellationToken ct = default)
    {
        var ids = groupIds.Distinct().ToHashSet();
        IReadOnlyDictionary<long, long> result = Where(m => ids.Contains(m.GroupId))
            .GroupBy(m => m.GroupId)
            .ToDictionary(g => g.Key, g => (long)g.Count());
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<DateTime, long>> CountByHourSinceAsync(
        int hours, CancellationToken ct = default)
    {
        IReadOnlyDictionary<DateTime, long> result = All
            .GroupBy(m => InMemoryPrivateMessageRepository.Truncate(m.SentAtUtc))
            .ToDictionary(g => g.Key, g => (long)g.Count());
        return Task.FromResult(result);
    }

    private static bool Matches(GroupMessage m, string keyword)
        => !m.IsDeleted && m.Content.Contains(keyword, StringComparison.Ordinal);
}

internal sealed class InMemorySessionSettingRepository
    : InMemoryStore<SessionSetting, long>, ISessionSettingRepository
{
    protected override long ToId(long identity) => identity;

    public Task<SessionSetting?> FindAsync(
        int userId, ChatSessionType type, long sessionId, CancellationToken ct = default)
    {
        var typeName = type.ToStorage();
        return Task.FromResult(Where(s =>
            s.UserId == userId && s.SessionType == typeName && s.SessionId == sessionId)
            .FirstOrDefault());
    }

    public Task<IReadOnlyDictionary<string, SessionSetting>> ListOfUserAsync(
        int userId, CancellationToken ct = default)
    {
        IReadOnlyDictionary<string, SessionSetting> result = Where(s => s.UserId == userId)
            .ToDictionary(s => $"{s.SessionType}_{s.SessionId}");
        return Task.FromResult(result);
    }

    public Task AddAsync(SessionSetting setting, CancellationToken ct = default)
    {
        Insert(setting);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(SessionSetting setting, CancellationToken ct = default)
    {
        Seed(setting);
        return Task.CompletedTask;
    }

    public Task DeleteBySessionAsync(
        ChatSessionType type, long sessionId, CancellationToken ct = default)
    {
        var typeName = type.ToStorage();
        RemoveWhere(s => s.SessionType == typeName && s.SessionId == sessionId);
        return Task.CompletedTask;
    }
}
