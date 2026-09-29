using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;

namespace LHZ.OnlineChat.Infrastructure.Persistence.Repositories;

/// <summary>用户仓储（FreeSql）</summary>
internal sealed class UserRepository : IUserRepository
{
    private readonly IFreeSql _fsql;

    public UserRepository(IFreeSql fsql) => _fsql = fsql;

    public Task<User?> FindByIdAsync(int userId, CancellationToken ct = default)
        => _fsql.Select<User>().Where(u => u.Id == userId).FirstAsync(ct)!;

    /// <summary>
    /// 注意：条件用「整体比较值对象」而非 u.Email.Value ——
    /// FreeSql 无法解析值对象列上的成员访问（见 ValueObjectTypeHandlers 的说明）。
    /// </summary>
    public Task<User?> FindByEmailAsync(Email email, CancellationToken ct = default)
        => _fsql.Select<User>().Where(u => u.Email == email).FirstAsync(ct)!;

    public Task<bool> EmailExistsAsync(Email email, int? excludeUserId = null, CancellationToken ct = default)
    {
        var query = _fsql.Select<User>().Where(u => u.Email == email);
        if (excludeUserId.HasValue) query = query.Where(u => u.Id != excludeUserId.Value);
        return query.AnyAsync(ct);
    }

    public async Task<IReadOnlyDictionary<int, User>> GetManyAsync(
        IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, User>();

        var users = await _fsql.Select<User>().Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        return users.ToDictionary(u => u.Id);
    }

    public async Task<(IReadOnlyList<User> Items, int Total)> SearchAsync(
        string? keyword, PageRequest page, bool? isBot, bool? banned, CancellationToken ct = default)
    {
        var query = _fsql.Select<User>();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            var pattern = FreeSqlExtensions.ToLikePattern(kw);

            // Email 是值对象列，无法写成 u.Email.Value.Contains(kw)（FreeSql 解析不了值对象上的成员访问），
            // 因此这一段条件用参数化的原生 SQL 表达。
            if (int.TryParse(kw, System.Globalization.CultureInfo.InvariantCulture, out var id))
            {
                query = query.Where(
                    "(\"Id\" = @id OR \"Nickname\" LIKE @kw OR (\"Email\" IS NOT NULL AND \"Email\" LIKE @kw))",
                    new { id, kw = pattern });
            }
            else
            {
                query = query.Where(
                    "(\"Nickname\" LIKE @kw OR (\"Email\" IS NOT NULL AND \"Email\" LIKE @kw))",
                    new { kw = pattern });
            }
        }

        if (isBot.HasValue) query = query.Where(u => u.IsBot == isBot.Value);
        if (banned.HasValue) query = query.Where(u => u.IsBanned == banned.Value);

        var total = (int)await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(u => u.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task AddAsync(User user, CancellationToken ct = default)
        => _fsql.InsertWithIdentityAsync(user, ct);

    public Task UpdateAsync(User user, CancellationToken ct = default)
        => _fsql.Update<User>().SetSource(user).ExecuteAffrowsAsync(ct);

    public Task DeleteAsync(int userId, CancellationToken ct = default)
        => _fsql.Delete<User>().Where(u => u.Id == userId).ExecuteAffrowsAsync(ct);

    public async Task<int> CountAsync(CancellationToken ct = default)
        => (int)await _fsql.Select<User>().CountAsync(ct);

    public async Task<int> CountBannedAsync(CancellationToken ct = default)
        => (int)await _fsql.Select<User>().Where(u => u.IsBanned).CountAsync(ct);

    public async Task<int> CountRegisteredSinceAsync(DateTime since, CancellationToken ct = default)
        => (int)await _fsql.Select<User>().Where(u => u.CreatedAt >= since).CountAsync(ct);

    public async Task<int> CountRegisteredBetweenAsync(
        DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default)
        => (int)await _fsql.Select<User>()
            .Where(u => u.CreatedAt >= fromInclusive && u.CreatedAt < toExclusive)
            .CountAsync(ct);
}
