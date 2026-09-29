using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Groups;

/// <summary>群组仓储</summary>
public interface IGroupRepository
{
    Task<Group?> FindByIdAsync(long groupId, CancellationToken ct = default);

    /// <summary>取群不存在则抛 EntityNotFoundException（省掉每个用例一次 null 判断）</summary>
    Task<Group> GetRequiredAsync(long groupId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<long, Group>> GetManyAsync(IEnumerable<long> groupIds, CancellationToken ct = default);

    /// <summary>管理后台：按名称关键词分页</summary>
    Task<(IReadOnlyList<Group> Items, int Total)> SearchAsync(
        string? keyword, PageRequest page, CancellationToken ct = default);

    Task<int> CountAsync(CancellationToken ct = default);

    Task<int> CountCreatedSinceAsync(DateTime since, CancellationToken ct = default);

    /// <summary>插入并回填自增主键</summary>
    Task AddAsync(Group group, CancellationToken ct = default);

    Task UpdateAsync(Group group, CancellationToken ct = default);

    Task DeleteAsync(long groupId, CancellationToken ct = default);
}

/// <summary>群成员仓储</summary>
public interface IGroupMemberRepository
{
    Task<GroupMember?> FindAsync(long groupId, int userId, CancellationToken ct = default);

    /// <summary>取成员，不在群中则抛 —— notMemberMessage 允许按场景定制提示语</summary>
    Task<GroupMember> GetRequiredAsync(long groupId, int userId, string notMemberMessage = "你不是该群组成员",
        CancellationToken ct = default);

    Task<bool> ExistsAsync(long groupId, int userId, CancellationToken ct = default);

    /// <summary>群内全部成员（按角色升序：群主→管理员→成员）</summary>
    Task<IReadOnlyList<GroupMember>> ListOfGroupAsync(long groupId, CancellationToken ct = default);

    /// <summary>群内全部成员的账号 ID（广播用，只取 ID 避免整行开销）</summary>
    Task<IReadOnlyList<int>> ListMemberIdsAsync(long groupId, CancellationToken ct = default);

    /// <summary>我加入的全部群（含各群的已读游标与角色）</summary>
    Task<IReadOnlyList<GroupMember>> ListOfUserAsync(int userId, CancellationToken ct = default);

    /// <summary>这批用户中哪些已在群里（邀请时排除）</summary>
    Task<IReadOnlyList<int>> FilterExistingAsync(long groupId, IEnumerable<int> userIds, CancellationToken ct = default);

    /// <summary>各群成员数</summary>
    Task<IReadOnlyDictionary<long, int>> CountByGroupAsync(IEnumerable<long> groupIds, CancellationToken ct = default);

    /// <summary>各用户加入的群数（管理后台统计）</summary>
    Task<IReadOnlyDictionary<int, int>> CountByUserAsync(IEnumerable<int> userIds, CancellationToken ct = default);

    Task AddAsync(GroupMember member, CancellationToken ct = default);

    Task AddRangeAsync(IEnumerable<GroupMember> members, CancellationToken ct = default);

    Task UpdateAsync(GroupMember member, CancellationToken ct = default);

    /// <summary>移出群，返回受影响行数</summary>
    Task<int> RemoveAsync(long groupId, int userId, CancellationToken ct = default);

    Task DeleteAllOfGroupAsync(long groupId, CancellationToken ct = default);

    /// <summary>清理某账号的全部群成员记录（删除机器人时）</summary>
    Task DeleteAllOfUserAsync(int userId, CancellationToken ct = default);
}
