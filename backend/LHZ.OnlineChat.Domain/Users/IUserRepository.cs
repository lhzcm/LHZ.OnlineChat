using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Users;

/// <summary>
/// 用户仓储。接口属于领域层，实现（FreeSql）在基础设施层 —— 依赖方向由外向内。
/// </summary>
public interface IUserRepository
{
    Task<User?> FindByIdAsync(int userId, CancellationToken ct = default);

    Task<User?> FindByEmailAsync(Email email, CancellationToken ct = default);

    /// <summary>邮箱是否已被占用（excludeUserId 用于换绑时排除自己）</summary>
    Task<bool> EmailExistsAsync(Email email, int? excludeUserId = null, CancellationToken ct = default);

    /// <summary>批量取用户（成员列表/消息发送者等展示场景）</summary>
    Task<IReadOnlyDictionary<int, User>> GetManyAsync(IEnumerable<int> userIds, CancellationToken ct = default);

    /// <summary>
    /// 管理后台检索：关键词匹配账号 ID / 昵称 / 邮箱，可按机器人与封禁状态过滤。
    /// </summary>
    Task<(IReadOnlyList<User> Items, int Total)> SearchAsync(
        string? keyword, PageRequest page, bool? isBot, bool? banned, CancellationToken ct = default);

    /// <summary>插入并回填自增主键（账号 ID）</summary>
    Task AddAsync(User user, CancellationToken ct = default);

    Task UpdateAsync(User user, CancellationToken ct = default);

    Task DeleteAsync(int userId, CancellationToken ct = default);

    // ===== 统计（仪表盘） =====

    Task<int> CountAsync(CancellationToken ct = default);

    Task<int> CountBannedAsync(CancellationToken ct = default);

    Task<int> CountRegisteredSinceAsync(DateTime since, CancellationToken ct = default);

    Task<int> CountRegisteredBetweenAsync(
        DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default);
}
