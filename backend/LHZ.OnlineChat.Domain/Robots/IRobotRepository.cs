using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Robots;

/// <summary>机器人仓储</summary>
public interface IRobotRepository
{
    Task<Robot?> FindByIdAsync(long robotId, CancellationToken ct = default);

    Task<Robot> GetRequiredAsync(long robotId, CancellationToken ct = default);

    /// <summary>按机器人账号 ID 取配置（私聊触发时判断收件人是不是机器人）</summary>
    Task<Robot?> FindByBotUserIdAsync(int botUserId, CancellationToken ct = default);

    /// <summary>按机器人账号 ID 批量取（群内 @ 了多个机器人时）</summary>
    Task<IReadOnlyList<Robot>> ListByBotUserIdsAsync(IEnumerable<int> botUserIds, CancellationToken ct = default);

    /// <summary>我创建的机器人（按创建时间倒序）</summary>
    Task<IReadOnlyList<Robot>> ListByOwnerAsync(int ownerId, CancellationToken ct = default);

    /// <summary>管理后台：按名称/ID 关键词分页</summary>
    Task<(IReadOnlyList<Robot> Items, int Total)> SearchAsync(
        string? keyword, PageRequest page, CancellationToken ct = default);

    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>插入并回填自增主键</summary>
    Task AddAsync(Robot robot, CancellationToken ct = default);

    Task UpdateAsync(Robot robot, CancellationToken ct = default);

    Task DeleteAsync(long robotId, CancellationToken ct = default);
}

/// <summary>
/// 机器人对外令牌的加解密（AES-256-GCM 加密内部自增 ID）。
/// 算法属基础设施，接口留在领域层是因为「令牌 ↔ 机器人身份」是领域概念。
/// </summary>
public interface IRobotTokenCipher
{
    /// <summary>机器人 ID → URL 安全令牌</summary>
    string Encode(long robotId);

    /// <summary>令牌 → 机器人 ID；无效返回 0</summary>
    long Decode(string? token);
}
