using LHZ.OnlineChat.Domain.Blacklists;
using LHZ.OnlineChat.Domain.Friends;

namespace LHZ.OnlineChat.Infrastructure.Persistence.Repositories;

/// <summary>好友关系仓储（FreeSql）</summary>
internal sealed class FriendshipRepository : IFriendshipRepository
{
    private readonly DbSession _db;

    public FriendshipRepository(DbSession db) => _db = db;

    public Task<Friendship?> FindByIdAsync(long id, CancellationToken ct = default)
        => _db.Select<Friendship>().Where(f => f.Id == id).FirstAsync(ct)!;

    public Task<Friendship?> FindBetweenAsync(int userId, int otherUserId, CancellationToken ct = default)
        => _db.Select<Friendship>()
            .Where(f => (f.UserId == userId && f.FriendId == otherUserId)
                        || (f.UserId == otherUserId && f.FriendId == userId))
            .FirstAsync(ct)!;

    public Task<bool> AreFriendsAsync(int userId, int otherUserId, CancellationToken ct = default)
    {
        if (userId == otherUserId) return Task.FromResult(false);
        return _db.Select<Friendship>()
            .Where(f => f.Status == FriendshipStatus.Accepted
                        && ((f.UserId == userId && f.FriendId == otherUserId)
                            || (f.UserId == otherUserId && f.FriendId == userId)))
            .AnyAsync(ct);
    }

    public async Task<IReadOnlyList<Friendship>> ListAcceptedOfAsync(int userId, CancellationToken ct = default)
        => await _db.Select<Friendship>()
            .Where(f => (f.UserId == userId || f.FriendId == userId) && f.Status == FriendshipStatus.Accepted)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<int>> ListFriendIdsOfAsync(int userId, CancellationToken ct = default)
    {
        var friendships = await ListAcceptedOfAsync(userId, ct);
        return friendships
            .Select(f => f.UserId == userId ? f.FriendId : f.UserId)
            .Distinct()
            .ToList();
    }

    public async Task<IReadOnlyList<Friendship>> ListPendingForAsync(int userId, CancellationToken ct = default)
        => await _db.Select<Friendship>()
            .Where(f => f.FriendId == userId && f.Status == FriendshipStatus.Pending)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<int, int>> CountAcceptedByUserAsync(
        IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, int>();

        var rows = await _db.Select<Friendship>()
            .Where(f => ids.Contains(f.UserId) && f.Status == FriendshipStatus.Accepted)
            .GroupBy(f => f.UserId)
            .ToListAsync(g => new { UserId = g.Key, Count = g.Count() }, ct);

        return rows.ToDictionary(r => r.UserId, r => r.Count);
    }

    public Task AddAsync(Friendship friendship, CancellationToken ct = default)
        => _db.InsertWithLongIdentityAsync(friendship, ct);

    public Task UpdateAsync(Friendship friendship, CancellationToken ct = default)
        => _db.Update<Friendship>().SetSource(friendship).ExecuteAffrowsAsync(ct);

    public Task DeleteAsync(long id, CancellationToken ct = default)
        => _db.Delete<Friendship>().Where(f => f.Id == id).ExecuteAffrowsAsync(ct);

    public Task<int> DeleteAcceptedBetweenAsync(int userId, int otherUserId, CancellationToken ct = default)
        => _db.Delete<Friendship>()
            .Where(f => f.Status == FriendshipStatus.Accepted
                        && ((f.UserId == userId && f.FriendId == otherUserId)
                            || (f.UserId == otherUserId && f.FriendId == userId)))
            .ExecuteAffrowsAsync(ct);

    public Task DeleteAllOfAsync(int userId, CancellationToken ct = default)
        => _db.Delete<Friendship>()
            .Where(f => f.UserId == userId || f.FriendId == userId)
            .ExecuteAffrowsAsync(ct);
}

/// <summary>好友设置（备注/分类）仓储</summary>
internal sealed class FriendSettingRepository : IFriendSettingRepository
{
    private readonly DbSession _db;

    public FriendSettingRepository(DbSession db) => _db = db;

    public Task<FriendSetting?> FindAsync(int userId, int friendId, CancellationToken ct = default)
        => _db.Select<FriendSetting>()
            .Where(t => t.UserId == userId && t.FriendId == friendId)
            .FirstAsync(ct)!;

    public async Task<IReadOnlyDictionary<int, FriendSetting>> GetManyAsync(
        int userId, IEnumerable<int> friendIds, CancellationToken ct = default)
    {
        var ids = friendIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, FriendSetting>();

        var settings = await _db.Select<FriendSetting>()
            .Where(t => t.UserId == userId && ids.Contains(t.FriendId))
            .ToListAsync(ct);

        return settings.ToDictionary(t => t.FriendId);
    }

    public Task AddAsync(FriendSetting setting, CancellationToken ct = default)
        => _db.InsertWithLongIdentityAsync(setting, ct);

    public Task UpdateAsync(FriendSetting setting, CancellationToken ct = default)
        => _db.Update<FriendSetting>().SetSource(setting).ExecuteAffrowsAsync(ct);
}

/// <summary>黑名单仓储</summary>
internal sealed class BlacklistRepository : IBlacklistRepository
{
    private readonly DbSession _db;

    public BlacklistRepository(DbSession db) => _db = db;

    public Task<bool> ExistsAsync(int userId, int blockedUserId, CancellationToken ct = default)
        => _db.Select<BlacklistEntry>()
            .Where(b => b.UserId == userId && b.BlockedUserId == blockedUserId)
            .AnyAsync(ct);

    public Task<bool> IsBlockedByAsync(int receiverId, int senderId, CancellationToken ct = default)
        => _db.Select<BlacklistEntry>()
            .Where(b => b.UserId == receiverId && b.BlockedUserId == senderId)
            .AnyAsync(ct);

    public Task<bool> ExistsEitherDirectionAsync(int userId, int otherUserId, CancellationToken ct = default)
        => _db.Select<BlacklistEntry>()
            .Where(b => (b.UserId == userId && b.BlockedUserId == otherUserId)
                        || (b.UserId == otherUserId && b.BlockedUserId == userId))
            .AnyAsync(ct);

    public async Task<IReadOnlyList<BlacklistEntry>> ListOfAsync(int userId, CancellationToken ct = default)
        => await _db.Select<BlacklistEntry>()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

    public Task AddAsync(BlacklistEntry entry, CancellationToken ct = default)
        => _db.InsertWithLongIdentityAsync(entry, ct);

    public Task<int> RemoveAsync(int userId, int blockedUserId, CancellationToken ct = default)
        => _db.Delete<BlacklistEntry>()
            .Where(b => b.UserId == userId && b.BlockedUserId == blockedUserId)
            .ExecuteAffrowsAsync(ct);
}
