using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Blacklists;

/// <summary>
/// 黑名单条目聚合根。
/// 规则：拉黑后自动解除好友关系；被拉黑者无法给拉黑者发私聊、无法发好友申请。
/// </summary>
public sealed class BlacklistEntry : AggregateRoot<long>
{
    private BlacklistEntry() { }

    /// <summary>拉黑者</summary>
    public int UserId { get; private set; }

    /// <summary>被拉黑者</summary>
    public int BlockedUserId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static BlacklistEntry Create(int userId, int blockedUserId, DateTime now)
    {
        DomainException.Ensure(blockedUserId > 0, "无效的用户 ID");
        DomainException.Ensure(userId != blockedUserId, "不能拉黑自己");
        return new BlacklistEntry
        {
            UserId = userId,
            BlockedUserId = blockedUserId,
            CreatedAt = now
        };
    }
}

/// <summary>某用户被另一用户拉黑 —— 订阅方推送 WS blocked 通知并解除好友关系</summary>
public sealed record UserBlocked(int BlockerId, int BlockedUserId, DateTime OccurredAt) : IDomainEvent;

/// <summary>黑名单仓储</summary>
public interface IBlacklistRepository
{
    Task<bool> ExistsAsync(int userId, int blockedUserId, CancellationToken ct = default);

    /// <summary>receiver 是否拉黑了 sender（私聊消息拦截）</summary>
    Task<bool> IsBlockedByAsync(int receiverId, int senderId, CancellationToken ct = default);

    /// <summary>两人间是否存在任一方向的拉黑（好友申请拦截）</summary>
    Task<bool> ExistsEitherDirectionAsync(int userId, int otherUserId, CancellationToken ct = default);

    /// <summary>我的黑名单（按拉黑时间倒序）</summary>
    Task<IReadOnlyList<BlacklistEntry>> ListOfAsync(int userId, CancellationToken ct = default);

    Task AddAsync(BlacklistEntry entry, CancellationToken ct = default);

    /// <summary>解除拉黑，返回受影响行数（0 表示原本不在黑名单）</summary>
    Task<int> RemoveAsync(int userId, int blockedUserId, CancellationToken ct = default);
}
