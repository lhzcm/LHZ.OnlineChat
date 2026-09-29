using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Robots;

namespace LHZ.OnlineChat.Infrastructure.Persistence.Repositories;

/// <summary>机器人仓储（FreeSql）</summary>
internal sealed class RobotRepository : IRobotRepository
{
    private readonly IFreeSql _fsql;

    public RobotRepository(IFreeSql fsql) => _fsql = fsql;

    public Task<Robot?> FindByIdAsync(long robotId, CancellationToken ct = default)
        => _fsql.Select<Robot>().Where(r => r.Id == robotId).FirstAsync(ct)!;

    public async Task<Robot> GetRequiredAsync(long robotId, CancellationToken ct = default)
        => await FindByIdAsync(robotId, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException("机器人不存在");

    public Task<Robot?> FindByBotUserIdAsync(int botUserId, CancellationToken ct = default)
        => _fsql.Select<Robot>().Where(r => r.UserId == botUserId).FirstAsync(ct)!;

    public async Task<IReadOnlyList<Robot>> ListByBotUserIdsAsync(
        IEnumerable<int> botUserIds, CancellationToken ct = default)
    {
        var ids = botUserIds.Distinct().ToList();
        if (ids.Count == 0) return Array.Empty<Robot>();

        return await _fsql.Select<Robot>().Where(r => ids.Contains(r.UserId)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Robot>> ListByOwnerAsync(int ownerId, CancellationToken ct = default)
        => await _fsql.Select<Robot>()
            .Where(r => r.OwnerId == ownerId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<Robot> Items, int Total)> SearchAsync(
        string? keyword, PageRequest page, CancellationToken ct = default)
    {
        var query = _fsql.Select<Robot>();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            if (int.TryParse(kw, System.Globalization.CultureInfo.InvariantCulture, out var id))
                query = query.Where(r => r.Id == id || r.UserId == id || r.Name.Contains(kw));
            else
                query = query.Where(r => r.Name.Contains(kw));
        }

        var total = (int)await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
        => (int)await _fsql.Select<Robot>().CountAsync(ct);

    public Task AddAsync(Robot robot, CancellationToken ct = default)
        => _fsql.InsertWithLongIdentityAsync(robot, ct);

    public Task UpdateAsync(Robot robot, CancellationToken ct = default)
        => _fsql.Update<Robot>().SetSource(robot).ExecuteAffrowsAsync(ct);

    public Task DeleteAsync(long robotId, CancellationToken ct = default)
        => _fsql.Delete<Robot>().Where(r => r.Id == robotId).ExecuteAffrowsAsync(ct);
}

/// <summary>管理员仓储（FreeSql）</summary>
internal sealed class AdminRepository : IAdminRepository
{
    private readonly IFreeSql _fsql;

    public AdminRepository(IFreeSql fsql) => _fsql = fsql;

    public Task<Admin?> FindByIdAsync(int adminId, CancellationToken ct = default)
        => _fsql.Select<Admin>().Where(a => a.Id == adminId).FirstAsync(ct)!;

    public async Task<Admin> GetRequiredAsync(int adminId, CancellationToken ct = default)
        => await FindByIdAsync(adminId, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException("管理员不存在");

    public Task<Admin?> FindByUsernameAsync(string username, CancellationToken ct = default)
        => _fsql.Select<Admin>().Where(a => a.Username == username).FirstAsync(ct)!;

    public Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default)
        => _fsql.Select<Admin>().Where(a => a.Username == username).AnyAsync(ct);

    public Task<bool> AnyAsync(CancellationToken ct = default)
        => _fsql.Select<Admin>().AnyAsync(ct);

    public async Task<IReadOnlyList<Admin>> ListAllAsync(CancellationToken ct = default)
        => await _fsql.Select<Admin>().OrderBy(a => a.Id).ToListAsync(ct);

    public Task AddAsync(Admin admin, CancellationToken ct = default)
        => _fsql.InsertWithIdentityAsync(admin, ct);

    public Task UpdateAsync(Admin admin, CancellationToken ct = default)
        => _fsql.Update<Admin>().SetSource(admin).ExecuteAffrowsAsync(ct);

    public Task DeleteAsync(int adminId, CancellationToken ct = default)
        => _fsql.Delete<Admin>().Where(a => a.Id == adminId).ExecuteAffrowsAsync(ct);
}

/// <summary>审计日志仓储（FreeSql）</summary>
internal sealed class AdminAuditLogRepository : IAdminAuditLogRepository
{
    private readonly IFreeSql _fsql;

    public AdminAuditLogRepository(IFreeSql fsql) => _fsql = fsql;

    public async Task<(IReadOnlyList<AdminAuditLog> Items, int Total)> PageAsync(
        PageRequest page, string? action, CancellationToken ct = default)
    {
        var query = _fsql.Select<AdminAuditLog>();
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(l => l.Action == action);

        var total = (int)await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(l => l.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task AddAsync(AdminAuditLog log, CancellationToken ct = default)
        => _fsql.InsertWithLongIdentityAsync(log, ct);
}
