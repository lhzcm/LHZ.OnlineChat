using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Blacklists;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Friends;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Robots;
using LHZ.OnlineChat.Domain.Users;

namespace LHZ.OnlineChat.Application.Tests.TestDoubles;

/// <summary>
/// 内存仓储的公共部分：自增主键分配 + 按标识存取。
/// 真实分配主键很重要 —— 用例里「插入后才能发事件/生成令牌」这类顺序问题，
/// 只有在替身也会回填 Id 时才测得出来。
/// </summary>
internal abstract class InMemoryStore<TEntity, TId>
    where TEntity : AggregateRoot<TId>
    where TId : struct, IEquatable<TId>
{
    private readonly Dictionary<TId, TEntity> _items = new();

    internal IReadOnlyCollection<TEntity> All => _items.Values;

    /// <summary>下一个自增主键（从 1 开始；用户表由子类改成 10000 起）</summary>
    protected long NextIdentity { get; set; } = 1;

    protected abstract TId ToId(long identity);

    /// <summary>插入并回填自增主键，模拟 ExecuteIdentity 的行为</summary>
    protected void Insert(TEntity entity)
    {
        var id = ToId(NextIdentity++);
        entity.AssignPersistedId(id);
        _items[id] = entity;
    }

    /// <summary>直接放入一个已有标识的实体（测试预置数据）</summary>
    internal void Seed(TEntity entity) => _items[entity.Id] = entity;

    protected TEntity? Get(TId id) => _items.GetValueOrDefault(id);

    protected void Remove(TId id) => _items.Remove(id);

    protected void RemoveWhere(Func<TEntity, bool> predicate)
    {
        foreach (var key in _items.Where(kv => predicate(kv.Value)).Select(kv => kv.Key).ToList())
        {
            _items.Remove(key);
        }
    }

    protected IEnumerable<TEntity> Where(Func<TEntity, bool> predicate) => _items.Values.Where(predicate);
}

// ==================================================================
// Users
// ==================================================================

internal sealed class InMemoryUserRepository : InMemoryStore<User, int>, IUserRepository
{
    internal InMemoryUserRepository() => NextIdentity = 10000;

    /// <summary>记录 UpdateAsync 被调用的次数，用于断言「改了就存」</summary>
    internal int UpdateCount { get; private set; }

    protected override int ToId(long identity) => (int)identity;

    public Task<User?> FindByIdAsync(int userId, CancellationToken ct = default)
        => Task.FromResult(Get(userId));

    public Task<User?> FindByEmailAsync(Email email, CancellationToken ct = default)
        => Task.FromResult(Where(u => u.Email == email).FirstOrDefault());

    public Task<bool> EmailExistsAsync(
        Email email, int? excludeUserId = null, CancellationToken ct = default)
        => Task.FromResult(Where(u =>
            u.Email == email && (!excludeUserId.HasValue || u.Id != excludeUserId.Value)).Any());

    public Task<IReadOnlyDictionary<int, User>> GetManyAsync(
        IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToHashSet();
        IReadOnlyDictionary<int, User> result = Where(u => ids.Contains(u.Id)).ToDictionary(u => u.Id);
        return Task.FromResult(result);
    }

    public Task<(IReadOnlyList<User> Items, int Total)> SearchAsync(
        string? keyword, PageRequest page, bool? isBot, bool? banned, CancellationToken ct = default)
    {
        var query = All.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            var byId = int.TryParse(kw, out var id);
            query = query.Where(u =>
                (byId && u.Id == id)
                || u.Nickname.Contains(kw, StringComparison.Ordinal)
                || (u.Email is not null && u.Email.Value.Contains(kw, StringComparison.Ordinal)));
        }

        if (isBot.HasValue) query = query.Where(u => u.IsBot == isBot.Value);
        if (banned.HasValue) query = query.Where(u => u.IsBanned == banned.Value);

        var all = query.OrderByDescending(u => u.Id).ToList();
        IReadOnlyList<User> items = all.Skip(page.Skip).Take(page.PageSize).ToList();
        return Task.FromResult((items, all.Count));
    }

    public Task AddAsync(User user, CancellationToken ct = default)
    {
        Insert(user);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(User user, CancellationToken ct = default)
    {
        UpdateCount++;
        Seed(user);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int userId, CancellationToken ct = default)
    {
        Remove(userId);
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(All.Count);

    public Task<int> CountBannedAsync(CancellationToken ct = default)
        => Task.FromResult(Where(u => u.IsBanned).Count());

    public Task<int> CountRegisteredSinceAsync(DateTime since, CancellationToken ct = default)
        => Task.FromResult(Where(u => u.CreatedAt >= since).Count());

    public Task<int> CountRegisteredBetweenAsync(
        DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default)
        => Task.FromResult(Where(u => u.CreatedAt >= fromInclusive && u.CreatedAt < toExclusive).Count());
}

// ==================================================================
// Friends
// ==================================================================

internal sealed class InMemoryFriendshipRepository
    : InMemoryStore<Friendship, long>, IFriendshipRepository
{
    protected override long ToId(long identity) => identity;

    public Task<Friendship?> FindByIdAsync(long id, CancellationToken ct = default)
        => Task.FromResult(Get(id));

    public Task<Friendship?> FindBetweenAsync(
        int userId, int otherUserId, CancellationToken ct = default)
        => Task.FromResult(Where(f => Between(f, userId, otherUserId)).FirstOrDefault());

    public Task<bool> AreFriendsAsync(int userId, int otherUserId, CancellationToken ct = default)
        => Task.FromResult(userId != otherUserId
            && Where(f => Between(f, userId, otherUserId) && f.IsAccepted).Any());

    public Task<IReadOnlyList<Friendship>> ListAcceptedOfAsync(
        int userId, CancellationToken ct = default)
    {
        IReadOnlyList<Friendship> items = Where(f => f.Involves(userId) && f.IsAccepted).ToList();
        return Task.FromResult(items);
    }

    public async Task<IReadOnlyList<int>> ListFriendIdsOfAsync(
        int userId, CancellationToken ct = default)
    {
        var accepted = await ListAcceptedOfAsync(userId, ct);
        return accepted.Select(f => f.PeerOf(userId)).Distinct().ToList();
    }

    public Task<IReadOnlyList<Friendship>> ListPendingForAsync(
        int userId, CancellationToken ct = default)
    {
        IReadOnlyList<Friendship> items = Where(f =>
            f.FriendId == userId && f.Status == FriendshipStatus.Pending).ToList();
        return Task.FromResult(items);
    }

    public Task<IReadOnlyDictionary<int, int>> CountAcceptedByUserAsync(
        IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToHashSet();
        IReadOnlyDictionary<int, int> result = Where(f => f.IsAccepted && ids.Contains(f.UserId))
            .GroupBy(f => f.UserId)
            .ToDictionary(g => g.Key, g => g.Count());
        return Task.FromResult(result);
    }

    public Task AddAsync(Friendship friendship, CancellationToken ct = default)
    {
        Insert(friendship);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Friendship friendship, CancellationToken ct = default)
    {
        Seed(friendship);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(long id, CancellationToken ct = default)
    {
        Remove(id);
        return Task.CompletedTask;
    }

    public Task<int> DeleteAcceptedBetweenAsync(
        int userId, int otherUserId, CancellationToken ct = default)
    {
        var matched = Where(f => Between(f, userId, otherUserId) && f.IsAccepted).ToList();
        foreach (var f in matched) Remove(f.Id);
        return Task.FromResult(matched.Count);
    }

    public Task DeleteAllOfAsync(int userId, CancellationToken ct = default)
    {
        RemoveWhere(f => f.Involves(userId));
        return Task.CompletedTask;
    }

    private static bool Between(Friendship f, int a, int b)
        => (f.UserId == a && f.FriendId == b) || (f.UserId == b && f.FriendId == a);
}

internal sealed class InMemoryFriendSettingRepository
    : InMemoryStore<FriendSetting, long>, IFriendSettingRepository
{
    protected override long ToId(long identity) => identity;

    public Task<FriendSetting?> FindAsync(int userId, int friendId, CancellationToken ct = default)
        => Task.FromResult(Where(s => s.UserId == userId && s.FriendId == friendId).FirstOrDefault());

    public Task<IReadOnlyDictionary<int, FriendSetting>> GetManyAsync(
        int userId, IEnumerable<int> friendIds, CancellationToken ct = default)
    {
        var ids = friendIds.Distinct().ToHashSet();
        IReadOnlyDictionary<int, FriendSetting> result =
            Where(s => s.UserId == userId && ids.Contains(s.FriendId)).ToDictionary(s => s.FriendId);
        return Task.FromResult(result);
    }

    public Task AddAsync(FriendSetting setting, CancellationToken ct = default)
    {
        Insert(setting);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(FriendSetting setting, CancellationToken ct = default)
    {
        Seed(setting);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryBlacklistRepository
    : InMemoryStore<BlacklistEntry, long>, IBlacklistRepository
{
    protected override long ToId(long identity) => identity;

    public Task<bool> ExistsAsync(int userId, int blockedUserId, CancellationToken ct = default)
        => Task.FromResult(Where(b => b.UserId == userId && b.BlockedUserId == blockedUserId).Any());

    public Task<bool> IsBlockedByAsync(int receiverId, int senderId, CancellationToken ct = default)
        => Task.FromResult(Where(b => b.UserId == receiverId && b.BlockedUserId == senderId).Any());

    public Task<bool> ExistsEitherDirectionAsync(
        int userId, int otherUserId, CancellationToken ct = default)
        => Task.FromResult(Where(b =>
            (b.UserId == userId && b.BlockedUserId == otherUserId)
            || (b.UserId == otherUserId && b.BlockedUserId == userId)).Any());

    public Task<IReadOnlyList<BlacklistEntry>> ListOfAsync(
        int userId, CancellationToken ct = default)
    {
        IReadOnlyList<BlacklistEntry> items = Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt).ToList();
        return Task.FromResult(items);
    }

    public Task AddAsync(BlacklistEntry entry, CancellationToken ct = default)
    {
        Insert(entry);
        return Task.CompletedTask;
    }

    public Task<int> RemoveAsync(int userId, int blockedUserId, CancellationToken ct = default)
    {
        var matched = Where(b => b.UserId == userId && b.BlockedUserId == blockedUserId).ToList();
        foreach (var b in matched) Remove(b.Id);
        return Task.FromResult(matched.Count);
    }
}

// ==================================================================
// Groups
// ==================================================================

internal sealed class InMemoryGroupRepository : InMemoryStore<Group, long>, IGroupRepository
{
    protected override long ToId(long identity) => identity;

    public Task<Group?> FindByIdAsync(long groupId, CancellationToken ct = default)
        => Task.FromResult(Get(groupId));

    public async Task<Group> GetRequiredAsync(long groupId, CancellationToken ct = default)
        => await FindByIdAsync(groupId, ct) ?? throw new EntityNotFoundException("群组不存在");

    public Task<IReadOnlyDictionary<long, Group>> GetManyAsync(
        IEnumerable<long> groupIds, CancellationToken ct = default)
    {
        var ids = groupIds.Distinct().ToHashSet();
        IReadOnlyDictionary<long, Group> result = Where(g => ids.Contains(g.Id)).ToDictionary(g => g.Id);
        return Task.FromResult(result);
    }

    public Task<(IReadOnlyList<Group> Items, int Total)> SearchAsync(
        string? keyword, PageRequest page, CancellationToken ct = default)
    {
        var query = All.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(g => g.Name.Contains(kw, StringComparison.Ordinal));
        }

        var all = query.OrderByDescending(g => g.Id).ToList();
        IReadOnlyList<Group> items = all.Skip(page.Skip).Take(page.PageSize).ToList();
        return Task.FromResult((items, all.Count));
    }

    public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(All.Count);

    public Task<int> CountCreatedSinceAsync(DateTime since, CancellationToken ct = default)
        => Task.FromResult(Where(g => g.CreatedAt >= since).Count());

    public Task AddAsync(Group group, CancellationToken ct = default)
    {
        Insert(group);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Group group, CancellationToken ct = default)
    {
        Seed(group);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(long groupId, CancellationToken ct = default)
    {
        Remove(groupId);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryGroupMemberRepository
    : InMemoryStore<GroupMember, long>, IGroupMemberRepository
{
    protected override long ToId(long identity) => identity;

    public Task<GroupMember?> FindAsync(long groupId, int userId, CancellationToken ct = default)
        => Task.FromResult(Where(m => m.GroupId == groupId && m.UserId == userId).FirstOrDefault());

    public async Task<GroupMember> GetRequiredAsync(
        long groupId, int userId, string notMemberMessage = "你不是该群组成员",
        CancellationToken ct = default)
        => await FindAsync(groupId, userId, ct) ?? throw new EntityNotFoundException(notMemberMessage);

    public Task<bool> ExistsAsync(long groupId, int userId, CancellationToken ct = default)
        => Task.FromResult(Where(m => m.GroupId == groupId && m.UserId == userId).Any());

    public Task<IReadOnlyList<GroupMember>> ListOfGroupAsync(
        long groupId, CancellationToken ct = default)
    {
        IReadOnlyList<GroupMember> items = Where(m => m.GroupId == groupId)
            .OrderBy(m => m.Role).ToList();
        return Task.FromResult(items);
    }

    public Task<IReadOnlyList<int>> ListMemberIdsAsync(long groupId, CancellationToken ct = default)
    {
        IReadOnlyList<int> items = Where(m => m.GroupId == groupId).Select(m => m.UserId).ToList();
        return Task.FromResult(items);
    }

    public Task<IReadOnlyList<GroupMember>> ListOfUserAsync(
        int userId, CancellationToken ct = default)
    {
        IReadOnlyList<GroupMember> items = Where(m => m.UserId == userId).ToList();
        return Task.FromResult(items);
    }

    public Task<IReadOnlyList<int>> FilterExistingAsync(
        long groupId, IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToHashSet();
        IReadOnlyList<int> items = Where(m => m.GroupId == groupId && ids.Contains(m.UserId))
            .Select(m => m.UserId).ToList();
        return Task.FromResult(items);
    }

    public Task<IReadOnlyDictionary<long, int>> CountByGroupAsync(
        IEnumerable<long> groupIds, CancellationToken ct = default)
    {
        var ids = groupIds.Distinct().ToHashSet();
        IReadOnlyDictionary<long, int> result = Where(m => ids.Contains(m.GroupId))
            .GroupBy(m => m.GroupId)
            .ToDictionary(g => g.Key, g => g.Count());
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<int, int>> CountByUserAsync(
        IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToHashSet();
        IReadOnlyDictionary<int, int> result = Where(m => ids.Contains(m.UserId))
            .GroupBy(m => m.UserId)
            .ToDictionary(g => g.Key, g => g.Count());
        return Task.FromResult(result);
    }

    public Task AddAsync(GroupMember member, CancellationToken ct = default)
    {
        Insert(member);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IEnumerable<GroupMember> members, CancellationToken ct = default)
    {
        foreach (var member in members) Insert(member);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(GroupMember member, CancellationToken ct = default)
    {
        Seed(member);
        return Task.CompletedTask;
    }

    public Task<int> RemoveAsync(long groupId, int userId, CancellationToken ct = default)
    {
        var matched = Where(m => m.GroupId == groupId && m.UserId == userId).ToList();
        foreach (var m in matched) Remove(m.Id);
        return Task.FromResult(matched.Count);
    }

    public Task DeleteAllOfGroupAsync(long groupId, CancellationToken ct = default)
    {
        RemoveWhere(m => m.GroupId == groupId);
        return Task.CompletedTask;
    }

    public Task DeleteAllOfUserAsync(int userId, CancellationToken ct = default)
    {
        RemoveWhere(m => m.UserId == userId);
        return Task.CompletedTask;
    }
}

// ==================================================================
// Robots / Admins
// ==================================================================

internal sealed class InMemoryRobotRepository : InMemoryStore<Robot, long>, IRobotRepository
{
    protected override long ToId(long identity) => identity;

    public Task<Robot?> FindByIdAsync(long robotId, CancellationToken ct = default)
        => Task.FromResult(Get(robotId));

    public async Task<Robot> GetRequiredAsync(long robotId, CancellationToken ct = default)
        => await FindByIdAsync(robotId, ct) ?? throw new EntityNotFoundException("机器人不存在");

    public Task<Robot?> FindByBotUserIdAsync(int botUserId, CancellationToken ct = default)
        => Task.FromResult(Where(r => r.UserId == botUserId).FirstOrDefault());

    public Task<IReadOnlyList<Robot>> ListByBotUserIdsAsync(
        IEnumerable<int> botUserIds, CancellationToken ct = default)
    {
        var ids = botUserIds.Distinct().ToHashSet();
        IReadOnlyList<Robot> items = Where(r => ids.Contains(r.UserId)).ToList();
        return Task.FromResult(items);
    }

    public Task<IReadOnlyList<Robot>> ListByOwnerAsync(int ownerId, CancellationToken ct = default)
    {
        IReadOnlyList<Robot> items = Where(r => r.OwnerId == ownerId)
            .OrderByDescending(r => r.CreatedAt).ToList();
        return Task.FromResult(items);
    }

    public Task<(IReadOnlyList<Robot> Items, int Total)> SearchAsync(
        string? keyword, PageRequest page, CancellationToken ct = default)
    {
        var query = All.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            var byId = int.TryParse(kw, out var id);
            query = query.Where(r =>
                (byId && (r.Id == id || r.UserId == id))
                || r.Name.Contains(kw, StringComparison.Ordinal));
        }

        var all = query.OrderByDescending(r => r.Id).ToList();
        IReadOnlyList<Robot> items = all.Skip(page.Skip).Take(page.PageSize).ToList();
        return Task.FromResult((items, all.Count));
    }

    public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(All.Count);

    public Task AddAsync(Robot robot, CancellationToken ct = default)
    {
        Insert(robot);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Robot robot, CancellationToken ct = default)
    {
        Seed(robot);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(long robotId, CancellationToken ct = default)
    {
        Remove(robotId);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryAdminRepository : InMemoryStore<Admin, int>, IAdminRepository
{
    protected override int ToId(long identity) => (int)identity;

    public Task<Admin?> FindByIdAsync(int adminId, CancellationToken ct = default)
        => Task.FromResult(Get(adminId));

    public async Task<Admin> GetRequiredAsync(int adminId, CancellationToken ct = default)
        => await FindByIdAsync(adminId, ct) ?? throw new EntityNotFoundException("管理员不存在");

    public Task<Admin?> FindByUsernameAsync(string username, CancellationToken ct = default)
        => Task.FromResult(Where(a => a.Username == username).FirstOrDefault());

    public Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default)
        => Task.FromResult(Where(a => a.Username == username).Any());

    public Task<bool> AnyAsync(CancellationToken ct = default) => Task.FromResult(All.Count > 0);

    public Task<IReadOnlyList<Admin>> ListAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Admin> items = All.OrderBy(a => a.Id).ToList();
        return Task.FromResult(items);
    }

    public Task AddAsync(Admin admin, CancellationToken ct = default)
    {
        Insert(admin);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Admin admin, CancellationToken ct = default)
    {
        Seed(admin);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int adminId, CancellationToken ct = default)
    {
        Remove(adminId);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryAdminAuditLogRepository
    : InMemoryStore<AdminAuditLog, long>, IAdminAuditLogRepository
{
    protected override long ToId(long identity) => identity;

    public Task<(IReadOnlyList<AdminAuditLog> Items, int Total)> PageAsync(
        PageRequest page, string? action, CancellationToken ct = default)
    {
        var query = All.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(l => l.Action == action);

        var all = query.OrderByDescending(l => l.Id).ToList();
        IReadOnlyList<AdminAuditLog> items = all.Skip(page.Skip).Take(page.PageSize).ToList();
        return Task.FromResult((items, all.Count));
    }

    public Task AddAsync(AdminAuditLog log, CancellationToken ct = default)
    {
        Insert(log);
        return Task.CompletedTask;
    }
}
