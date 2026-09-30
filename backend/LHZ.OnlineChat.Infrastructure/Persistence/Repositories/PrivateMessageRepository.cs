using FreeSql;
using System.Data;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Infrastructure.Persistence.Repositories;

/// <summary>私聊消息仓储（FreeSql）</summary>
internal sealed class PrivateMessageRepository : IPrivateMessageRepository
{
    private readonly DbSession _db;

    public PrivateMessageRepository(DbSession db) => _db = db;

    public Task<PrivateMessage?> FindByIdAsync(long id, CancellationToken ct = default)
        => _db.Select<PrivateMessage>().Where(m => m.Id == id).FirstAsync(ct)!;

    public async Task<(IReadOnlyList<PrivateMessage> Items, int Total)> PageBetweenAsync(
        int userId, int peerId, PageRequest page, CancellationToken ct = default)
    {
        var total = (int)await _db.Select<PrivateMessage>()
            .Where(m => (m.SenderId == userId && m.ReceiverId == peerId)
                        || (m.SenderId == peerId && m.ReceiverId == userId))
            .CountAsync(ct);

        var items = await _db.Select<PrivateMessage>()
            .Where(m => (m.SenderId == userId && m.ReceiverId == peerId)
                        || (m.SenderId == peerId && m.ReceiverId == userId))
            .OrderByDescending(m => m.SentAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<(IReadOnlyList<PrivateMessage> Items, int Total)> SearchBetweenAsync(
        int userId, int peerId, string keyword, PageRequest page, CancellationToken ct = default)
    {
        var total = (int)await BetweenMatching(userId, peerId, keyword).CountAsync(ct);
        var items = await BetweenMatching(userId, peerId, keyword)
            .OrderByDescending(m => m.SentAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<(IReadOnlyList<PrivateMessage> Items, int Total)> SearchOfUserAsync(
        int userId, string keyword, int take, CancellationToken ct = default)
    {
        var total = (int)await OfUserMatching(userId, keyword).CountAsync(ct);
        var items = await OfUserMatching(userId, keyword)
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<(IReadOnlyList<PrivateMessage> Items, int Total)> SearchForAdminAsync(
        string? keyword, int? userId, int take, CancellationToken ct = default)
    {
        var query = _db.Select<PrivateMessage>();
        if (userId.HasValue)
            query = query.Where(m => m.SenderId == userId.Value || m.ReceiverId == userId.Value);
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

    public async Task<IReadOnlyList<PrivateMessage>> ListUnreadForAsync(
        int userId, CancellationToken ct = default)
        => await _db.Select<PrivateMessage>()
            .Where(m => m.ReceiverId == userId && !m.IsRead)
            .OrderBy(m => m.SentAt)
            .ToListAsync(ct);

    public async Task<int> CountUnreadForAsync(int userId, CancellationToken ct = default)
        => (int)await _db.Select<PrivateMessage>()
            .Where(m => m.ReceiverId == userId && !m.IsRead)
            .CountAsync(ct);

    /// <summary>
    /// 按发送者分组统计未读。
    /// 原实现是把全部未读行拉到内存再 foreach 累加；这里改成 SQL 聚合。
    /// </summary>
    public async Task<IReadOnlyDictionary<int, int>> CountUnreadBySenderAsync(
        int userId, CancellationToken ct = default)
    {
        var rows = await _db.Select<PrivateMessage>()
            .Where(m => m.ReceiverId == userId && !m.IsRead)
            .GroupBy(m => m.SenderId)
            .ToListAsync(g => new { SenderId = g.Key, Count = g.Count() }, ct);

        return rows.ToDictionary(r => r.SenderId, r => r.Count);
    }

    public async Task<IReadOnlyList<PrivateMessage>> ListRecentOfUserAsync(
        int userId, int take, CancellationToken ct = default)
        => await _db.Select<PrivateMessage>()
            .Where(m => m.SenderId == userId || m.ReceiverId == userId)
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .ToListAsync(ct);

    /// <summary>
    /// 找可撤回的消息：本人发出、未撤回、在时间窗内，且 messageId 匹配（客户端 ID 或数据库 ID）。
    /// 数据库 ID 的字符串比较用原生 SQL，避免依赖 ORM 对 Id.ToString() 的翻译。
    /// </summary>
    public Task<PrivateMessage?> FindRecallableAsync(
        int senderId, int receiverId, string messageId, DateTime earliestSentAt, CancellationToken ct = default)
        => _db.Select<PrivateMessage>()
            .Where(m => m.SenderId == senderId
                        && m.ReceiverId == receiverId
                        && !m.IsDeleted
                        && m.SentAt >= earliestSentAt)
            .Where("(\"ClientMessageId\" = @mid OR CAST(\"Id\" AS text) = @mid)", new { mid = messageId })
            .FirstAsync(ct)!;

    public Task<int> MarkAllReadAsync(int senderId, int receiverId, CancellationToken ct = default)
        => _db.Update<PrivateMessage>()
            .Set(m => m.IsRead, true)
            .Where(m => m.SenderId == senderId && m.ReceiverId == receiverId && !m.IsRead)
            .ExecuteAffrowsAsync(ct);

    public Task AddAsync(PrivateMessage message, CancellationToken ct = default)
        => _db.InsertWithLongIdentityAsync(message, ct);

    public Task UpdateAsync(PrivateMessage message, CancellationToken ct = default)
        => _db.Update<PrivateMessage>().SetSource(message).ExecuteAffrowsAsync(ct);

    // ==================== 统计 ====================

    public Task<long> CountAsync(CancellationToken ct = default)
        => _db.Select<PrivateMessage>().CountAsync(ct);

    public Task<long> CountSentBetweenAsync(
        DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default)
        => _db.Select<PrivateMessage>()
            .Where(m => m.SentAt >= fromInclusive && m.SentAt < toExclusive)
            .CountAsync(ct);

    public Task<long> CountSentSinceAsync(DateTime since, CancellationToken ct = default)
        => _db.Select<PrivateMessage>().Where(m => m.SentAt >= since).CountAsync(ct);

    public async Task<IReadOnlyList<int>> ListDistinctSendersSinceAsync(
        DateTime since, CancellationToken ct = default)
    {
        var rows = await _db.Select<PrivateMessage>()
            .Where(m => m.SentAt >= since)
            .GroupBy(m => m.SenderId)
            .ToListAsync(g => new { g.Key }, ct);

        return rows.Select(r => r.Key).ToList();
    }

    public async Task<IReadOnlyDictionary<int, long>> TopSendersAsync(
        int take, CancellationToken ct = default)
    {
        var rows = await _db.Select<PrivateMessage>()
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

        var rows = await _db.Select<PrivateMessage>()
            .Where(m => ids.Contains(m.SenderId))
            .GroupBy(m => m.SenderId)
            .ToListAsync(g => new { SenderId = g.Key, Count = g.Count() }, ct);

        return rows.ToDictionary(r => r.SenderId, r => (long)r.Count);
    }

    public Task<IReadOnlyDictionary<DateTime, long>> CountByHourSinceAsync(
        int hours, CancellationToken ct = default)
        => Task.FromResult(HourlyAggregate.Query(_db.Orm, "PrivateMessage", hours));

    // ==================== 查询片段 ====================

    private ISelect<PrivateMessage> BetweenMatching(int userId, int peerId, string keyword)
        => _db.Select<PrivateMessage>()
            .Where(m => ((m.SenderId == userId && m.ReceiverId == peerId)
                         || (m.SenderId == peerId && m.ReceiverId == userId))
                        && !m.IsDeleted
                        && m.Content.Contains(keyword));

    private ISelect<PrivateMessage> OfUserMatching(int userId, string keyword)
        => _db.Select<PrivateMessage>()
            .Where(m => (m.SenderId == userId || m.ReceiverId == userId)
                        && !m.IsDeleted
                        && m.Content.Contains(keyword));
}

/// <summary>
/// 按小时聚合消息量（原生 SQL date_trunc，一次查完 24 个点）。
/// 两个消息表共用同一段逻辑，只有表名不同。
/// </summary>
internal static class HourlyAggregate
{
    internal static IReadOnlyDictionary<DateTime, long> Query(IFreeSql fsql, string tableName, int hours)
    {
        var result = new Dictionary<DateTime, long>();
        try
        {
            // 表名来自调用方硬编码的常量，不是外部输入
            var sql = $"""
                SELECT date_trunc('hour', "SentAt") AS "H", COUNT(*) AS "Cnt"
                FROM "{tableName}"
                WHERE "SentAt" >= NOW() - (INTERVAL '1 hour' * {hours})
                GROUP BY 1
                """;

            var table = fsql.Ado.ExecuteDataTable(sql);
            foreach (DataRow row in table.Rows)
            {
                var hour = DateTime.SpecifyKind((DateTime)row["H"], DateTimeKind.Utc);
                result[hour] = Convert.ToInt64(row["Cnt"], System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 与改造前一致：聚合失败不影响仪表盘其余部分
            Console.WriteLine($"[DASHBOARD] {tableName} 小时分布查询失败: {ex.Message}");
        }

        return result;
    }
}
