namespace LHZ.OnlineChat.Domain.Friends;

/// <summary>好友关系仓储</summary>
public interface IFriendshipRepository
{
    Task<Friendship?> FindByIdAsync(long id, CancellationToken ct = default);

    /// <summary>取两人之间的关系（任一方向），不限状态</summary>
    Task<Friendship?> FindBetweenAsync(int userId, int otherUserId, CancellationToken ct = default);

    /// <summary>两人是否已是好友（已接受）</summary>
    Task<bool> AreFriendsAsync(int userId, int otherUserId, CancellationToken ct = default);

    /// <summary>该用户的全部已接受关系（任一方向）</summary>
    Task<IReadOnlyList<Friendship>> ListAcceptedOfAsync(int userId, CancellationToken ct = default);

    /// <summary>该用户的全部好友账号 ID（已接受，已去重）</summary>
    Task<IReadOnlyList<int>> ListFriendIdsOfAsync(int userId, CancellationToken ct = default);

    /// <summary>别人发给我的待确认申请</summary>
    Task<IReadOnlyList<Friendship>> ListPendingForAsync(int userId, CancellationToken ct = default);

    /// <summary>该用户的已接受好友数（管理后台统计）</summary>
    Task<IReadOnlyDictionary<int, int>> CountAcceptedByUserAsync(IEnumerable<int> userIds, CancellationToken ct = default);

    Task AddAsync(Friendship friendship, CancellationToken ct = default);

    Task UpdateAsync(Friendship friendship, CancellationToken ct = default);

    Task DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>解除两人之间的已接受关系（拉黑时自动调用），返回受影响行数</summary>
    Task<int> DeleteAcceptedBetweenAsync(int userId, int otherUserId, CancellationToken ct = default);

    /// <summary>清理某账号的全部好友关系（删除机器人时）</summary>
    Task DeleteAllOfAsync(int userId, CancellationToken ct = default);
}

/// <summary>好友设置（备注/分类）仓储</summary>
public interface IFriendSettingRepository
{
    Task<FriendSetting?> FindAsync(int userId, int friendId, CancellationToken ct = default);

    /// <summary>我对这批好友的设置（键为好友账号 ID）</summary>
    Task<IReadOnlyDictionary<int, FriendSetting>> GetManyAsync(
        int userId, IEnumerable<int> friendIds, CancellationToken ct = default);

    Task AddAsync(FriendSetting setting, CancellationToken ct = default);

    Task UpdateAsync(FriendSetting setting, CancellationToken ct = default);
}
