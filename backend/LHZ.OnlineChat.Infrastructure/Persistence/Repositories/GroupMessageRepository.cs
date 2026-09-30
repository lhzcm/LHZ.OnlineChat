using FreeSql;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Infrastructure.Persistence.Repositories;

/// <summary>群聊消息仓储（FreeSql）</summary>
internal sealed class GroupMessageRepository : IGroupMessageRepository
{
    private readonly DbSession _db;

    public GroupMessageRepository(DbSession db) => _db = db;

    public Task<GroupMessage?> FindByIdAsync(long id, CancellationToken ct = default)
        => _db.Select<GroupMessage>().Where(m => m.Id == id).FirstAsync(ct)!;

    public Task<long> MaxIdOfGroupAsync(long groupId, CancellationToken ct = default)
        => _db.Select<GroupMessage>()
            .Where(m => m.GroupId == groupId)
            .MaxAsync(m => m.Id, ct);

    public async Task<(IReadOnlyList<GroupMessage> Items, int Total)> PageOfGroupAsync(
        long groupId, PageRequest page, CancellationToken ct = default)
    {
        var total = (int)await _db.Select<GroupMessage>()
            .Where(m => m.GroupId == groupId)
            .CountAsync(ct);

        var items = await _db.Select<GroupMessage>()
            .Where(m => m.GroupId == groupId)
            .OrderByDescending(m => m.SentAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<(IReadOnlyList<GroupMessage> Items, int Total)> SearchInGroupAsync(
        long groupId, string keyword, PageRequest page, CancellationToken ct = default)
    {
        var total = (int)await InGroupMatching(groupId, keyword).CountAsync(ct);
        var items = await InGroupMatching(groupId, keyword)
            .OrderByDescending(m => m.SentAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<(IReadOnlyList<GroupMessage> Items, int Total)> SearchInGroupsAsync(
        IReadOnlyList<long> groupIds, string keyword, int take, CancellationToken ct = default)
    {
        if (groupIds.Count == 0) return (Array.Empty<GroupMessage>(), 0);

        var ids = groupIds.ToList();
        var total = (int)await InGroupsMatching(ids, keyword).CountAsync(ct);
        var items = await InGroupsMatching(ids, keyword)
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<(IReadOnlyList<GroupMessage> Items, int Total)> SearchForAdminAsync(
        string? keyword, int? senderId, long? groupId, int take, CancellationToken ct = default)
    {
        var query = _db.Select<GroupMessage>();
        if (groupId.HasValue) query = query.Where(m => m.GroupId == groupId.Value);
        if (senderId.HasValue) query = query.Where(m => m.SenderId == senderId.Value);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(m => m.Content.Contains(kw));
        }

        var total = (int)await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<IReadOnlyList<GroupMessage>> ListAfterCursorAsync(
        long groupId, long afterMessageId, int limit, CancellationToken ct = default)
        => await _db.Select<GroupMessage>()
            .Where(m => m.GroupId == groupId && m.Id > afterMessageId && !m.IsDeleted)
            .OrderBy(m => m.SentAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<int> CountAfterCursorAsync(
        long groupId, long afterMessageId, CancellationToken ct = default)
        => (int)await _db.Select<GroupMessage>()
            .Where(m => m.GroupId == groupId && m.Id > afterMessageId)
            .CountAsync(ct);

    /// <summary>
    /// 各群的最后一条消息。
    /// 原实现是「取最近 500 条再在内存里按群去重」，群多时会漏掉冷门群；
    /// 这里用窗口函数一次取准。
    /// </summary>
    public async Task<IReadOnlyDictionary<long, GroupMessage>> LatestOfGroupsAsync(
        IReadOnlyList<long> groupIds, CancellationToken ct = default)
    {
        if (groupIds.Count == 0) return new Dictionary<long, GroupMessage>();

        var ids = groupIds.ToList();

        // 每群取 SentAt 最大的一条：先查每群的最大 Id，再按 Id 取回整行
        var latestIds = await _db.Select<GroupMessage>()
            .Where(m => ids.Contains(m.GroupId))
            .GroupBy(m => m.GroupId)
            .ToListAsync(g => new { GroupId = g.Key, MaxId = g.Max(g.Value.Id) }, ct);

        if (latestIds.Count == 0) return new Dictionary<long, GroupMessage>();

        var messageIds = latestIds.Select(x => x.MaxId).ToList();
        var messages = await _db.Select<GroupMessage>()
            .Where(m => messageIds.Contains(m.Id))
            .ToListAsync(ct);

        return messages.ToDictionary(m => m.GroupId);
    }

    public Task<GroupMessage?> FindRecallableAsync(
        long groupId, int senderId, string messageId, DateTime earliestSentAt, CancellationToken ct = default)
        => _db.Select<GroupMessage>()
            .Where(m => m.GroupId == groupId
                        && m.SenderId == senderId
                        && !m.IsDeleted
                        && m.SentAt >= earliestSentAt)
            .Where("(\"ClientMessageId\" = @mid OR CAST(\"Id\" AS text) = @mid)", new { mid = messageId })
            .FirstAsync(ct)!;

    public Task AddAsync(GroupMessage message, CancellationToken ct = default)
        => _db.InsertWithLongIdentityAsync(message, ct);

    public Task UpdateAsync(GroupMessage message, CancellationToken ct = default)
        => _db.Update<GroupMessage>().SetSource(message).ExecuteAffrowsAsync(ct);

    public Task DeleteAllOfGroupAsync(long groupId, CancellationToken ct = default)
        => _db.Delete<GroupMessage>().Where(m => m.GroupId == groupId).ExecuteAffrowsAsync(ct);

    // ==================== 统计 ====================

    public Task<long> CountAsync(CancellationToken ct = default)
        => _db.Select<GroupMessage>().CountAsync(ct);

    public Task<long> CountSentBetweenAsync(
        DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default)
        => _db.Select<GroupMessage>()
            .Where(m => m.SentAt >= fromInclusive && m.SentAt < toExclusive)
            .CountAsync(ct);

    public Task<long> CountSentSinceAsync(DateTime since, CancellationToken ct = default)
        => _db.Select<GroupMessage>().Where(m => m.SentAt >= since).CountAsync(ct);

    public async Task<IReadOnlyList<int>> ListDistinctSendersSinceAsync(
        DateTime since, CancellationToken ct = default)
    {
        var rows = await _db.Select<GroupMessage>()
            .Where(m => m.SentAt >= since)
            .GroupBy(m => m.SenderId)
            .ToListAsync(g => new { g.Key }, ct);

        return rows.Select(r => r.Key).ToList();
    }

    public async Task<IReadOnlyDictionary<int, long>> TopSendersAsync(
        int take, CancellationToken ct = default)
    {
        var rows = await _db.Select<GroupMessage>()
            .GroupBy(m => m.SenderId)
            .OrderByDescending(g => g.Count())
            .Take(take)
            .ToListAsync(g => new { SenderId = g.Key, Count = g.Count() }, ct);

        return rows.ToDictionary(r => r.SenderId, r => (long)r.Count);
    }

    public async Task<IReadOnlyDictionary<int, long>> CountBySenderAsync(
        IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, long>();

        var rows = await _db.Select<GroupMessage>()
            .Where(m => ids.Contains(m.SenderId))
            .GroupBy(m => m.SenderId)
            .ToListAsync(g => new { SenderId = g.Key, Count = g.Count() }, ct);

        return rows.ToDictionary(r => r.SenderId, r => (long)r.Count);
    }

    public async Task<IReadOnlyDictionary<long, long>> TopGroupsAsync(
        int take, CancellationToken ct = default)
    {
        var rows = await _db.Select<GroupMessage>()
            .GroupBy(m => m.GroupId)
            .OrderByDescending(g => g.Count())
            .Take(take)
            .ToListAsync(g => new { GroupId = g.Key, Count = g.Count() }, ct);

        return rows.ToDictionary(r => r.GroupId, r => (long)r.Count);
    }

    public async Task<IReadOnlyDictionary<long, long>> CountByGroupAsync(
        IEnumerable<long> groupIds, CancellationToken ct = default)
    {
        var ids = groupIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<long, long>();

        var rows = await _db.Select<GroupMessage>()
            .Where(m => ids.Contains(m.GroupId))
            .GroupBy(m => m.GroupId)
            .ToListAsync(g => new { GroupId = g.Key, Count = g.Count() }, ct);

        return rows.ToDictionary(r => r.GroupId, r => (long)r.Count);
    }

    public Task<IReadOnlyDictionary<DateTime, long>> CountByHourSinceAsync(
        int hours, CancellationToken ct = default)
        => Task.FromResult(HourlyAggregate.Query(_db.Orm, "GroupMessage", hours));

    // ==================== 查询片段 ====================

    private ISelect<GroupMessage> InGroupMatching(long groupId, string keyword)
        => _db.Select<GroupMessage>()
            .Where(m => m.GroupId == groupId && !m.IsDeleted && m.Content.Contains(keyword));

    private ISelect<GroupMessage> InGroupsMatching(List<long> groupIds, string keyword)
        => _db.Select<GroupMessage>()
            .Where(m => groupIds.Contains(m.GroupId) && !m.IsDeleted && m.Content.Contains(keyword));
}

/// <summary>会话设置仓储</summary>
internal sealed class SessionSettingRepository : ISessionSettingRepository
{
    private readonly DbSession _db;

    public SessionSettingRepository(DbSession db) => _db = db;

    public Task<SessionSetting?> FindAsync(
        int userId, ChatSessionType type, long sessionId, CancellationToken ct = default)
    {
        var typeName = type.ToStorage();
        return _db.Select<SessionSetting>()
            .Where(s => s.UserId == userId && s.SessionType == typeName && s.SessionId == sessionId)
            .FirstAsync(ct)!;
    }

    public async Task<IReadOnlyDictionary<string, SessionSetting>> ListOfUserAsync(
        int userId, CancellationToken ct = default)
    {
        var settings = await _db.Select<SessionSetting>()
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

        // 键格式与 GetChatSessionsHandler.SessionSettingKey 约定一致
        return settings.ToDictionary(s => $"{s.SessionType}_{s.SessionId}");
    }

    public Task AddAsync(SessionSetting setting, CancellationToken ct = default)
        => _db.InsertWithLongIdentityAsync(setting, ct);

    public Task UpdateAsync(SessionSetting setting, CancellationToken ct = default)
        => _db.Update<SessionSetting>().SetSource(setting).ExecuteAffrowsAsync(ct);

    public Task DeleteBySessionAsync(
        ChatSessionType type, long sessionId, CancellationToken ct = default)
    {
        var typeName = type.ToStorage();
        return _db.Delete<SessionSetting>()
            .Where(s => s.SessionType == typeName && s.SessionId == sessionId)
            .ExecuteAffrowsAsync(ct);
    }
}
