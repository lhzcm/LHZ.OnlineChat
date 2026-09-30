using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Groups;

namespace LHZ.OnlineChat.Infrastructure.Persistence.Repositories;

/// <summary>群组仓储（FreeSql）</summary>
internal sealed class GroupRepository : IGroupRepository
{
    private readonly DbSession _db;

    public GroupRepository(DbSession db) => _db = db;

    public Task<Group?> FindByIdAsync(long groupId, CancellationToken ct = default)
        => _db.Select<Group>().Where(g => g.Id == groupId).FirstAsync(ct)!;

    public async Task<Group> GetRequiredAsync(long groupId, CancellationToken ct = default)
        => await FindByIdAsync(groupId, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException("群组不存在");

    public async Task<IReadOnlyDictionary<long, Group>> GetManyAsync(
        IEnumerable<long> groupIds, CancellationToken ct = default)
    {
        var ids = groupIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<long, Group>();

        var groups = await _db.Select<Group>().Where(g => ids.Contains(g.Id)).ToListAsync(ct);
        return groups.ToDictionary(g => g.Id);
    }

    public async Task<(IReadOnlyList<Group> Items, int Total)> SearchAsync(
        string? keyword, PageRequest page, CancellationToken ct = default)
    {
        var query = _db.Select<Group>();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(g => g.Name.Contains(kw));
        }

        var total = (int)await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(g => g.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
        => (int)await _db.Select<Group>().CountAsync(ct);

    public async Task<int> CountCreatedSinceAsync(DateTime since, CancellationToken ct = default)
        => (int)await _db.Select<Group>().Where(g => g.CreatedAt >= since).CountAsync(ct);

    public Task AddAsync(Group group, CancellationToken ct = default)
        => _db.InsertWithLongIdentityAsync(group, ct);

    public Task UpdateAsync(Group group, CancellationToken ct = default)
        => _db.Update<Group>().SetSource(group).ExecuteAffrowsAsync(ct);

    public Task DeleteAsync(long groupId, CancellationToken ct = default)
        => _db.Delete<Group>().Where(g => g.Id == groupId).ExecuteAffrowsAsync(ct);
}

/// <summary>群成员仓储（FreeSql）</summary>
internal sealed class GroupMemberRepository : IGroupMemberRepository
{
    private readonly DbSession _db;

    public GroupMemberRepository(DbSession db) => _db = db;

    public Task<GroupMember?> FindAsync(long groupId, int userId, CancellationToken ct = default)
        => _db.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == userId)
            .FirstAsync(ct)!;

    public async Task<GroupMember> GetRequiredAsync(
        long groupId, int userId, string notMemberMessage = "你不是该群组成员", CancellationToken ct = default)
        => await FindAsync(groupId, userId, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(notMemberMessage);

    public Task<bool> ExistsAsync(long groupId, int userId, CancellationToken ct = default)
        => _db.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == userId)
            .AnyAsync(ct);

    public async Task<IReadOnlyList<GroupMember>> ListOfGroupAsync(long groupId, CancellationToken ct = default)
        => await _db.Select<GroupMember>()
            .Where(m => m.GroupId == groupId)
            .OrderBy(m => m.Role)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<int>> ListMemberIdsAsync(long groupId, CancellationToken ct = default)
        => await _db.Select<GroupMember>()
            .Where(m => m.GroupId == groupId)
            .ToListAsync(m => m.UserId, ct);

    public async Task<IReadOnlyList<GroupMember>> ListOfUserAsync(int userId, CancellationToken ct = default)
        => await _db.Select<GroupMember>()
            .Where(m => m.UserId == userId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<int>> FilterExistingAsync(
        long groupId, IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return Array.Empty<int>();

        return await _db.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && ids.Contains(m.UserId))
            .ToListAsync(m => m.UserId, ct);
    }

    public async Task<IReadOnlyDictionary<long, int>> CountByGroupAsync(
        IEnumerable<long> groupIds, CancellationToken ct = default)
    {
        var ids = groupIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<long, int>();

        var rows = await _db.Select<GroupMember>()
            .Where(m => ids.Contains(m.GroupId))
            .GroupBy(m => m.GroupId)
            .ToListAsync(g => new { GroupId = g.Key, Count = g.Count() }, ct);

        return rows.ToDictionary(r => r.GroupId, r => r.Count);
    }

    public async Task<IReadOnlyDictionary<int, int>> CountByUserAsync(
        IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, int>();

        var rows = await _db.Select<GroupMember>()
            .Where(m => ids.Contains(m.UserId))
            .GroupBy(m => m.UserId)
            .ToListAsync(g => new { UserId = g.Key, Count = g.Count() }, ct);

        return rows.ToDictionary(r => r.UserId, r => r.Count);
    }

    public Task AddAsync(GroupMember member, CancellationToken ct = default)
        => _db.InsertWithLongIdentityAsync(member, ct);

    public async Task AddRangeAsync(IEnumerable<GroupMember> members, CancellationToken ct = default)
    {
        var list = members.ToList();
        if (list.Count == 0) return;
        await _db.Insert(list).ExecuteAffrowsAsync(ct);
    }

    public Task UpdateAsync(GroupMember member, CancellationToken ct = default)
        => _db.Update<GroupMember>().SetSource(member).ExecuteAffrowsAsync(ct);

    public Task<int> RemoveAsync(long groupId, int userId, CancellationToken ct = default)
        => _db.Delete<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == userId)
            .ExecuteAffrowsAsync(ct);

    public Task DeleteAllOfGroupAsync(long groupId, CancellationToken ct = default)
        => _db.Delete<GroupMember>().Where(m => m.GroupId == groupId).ExecuteAffrowsAsync(ct);

    public Task DeleteAllOfUserAsync(int userId, CancellationToken ct = default)
        => _db.Delete<GroupMember>().Where(m => m.UserId == userId).ExecuteAffrowsAsync(ct);
}
