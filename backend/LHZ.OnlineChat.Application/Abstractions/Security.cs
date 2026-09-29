using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Users;

namespace LHZ.OnlineChat.Application.Abstractions;

/// <summary>口令哈希与校验（BCrypt 在基础设施层）</summary>
public interface IPasswordHasher
{
    PasswordHash Hash(string rawPassword);

    /// <summary>校验明文是否匹配哈希；hash 为 null 时一律返回 false</summary>
    bool Verify(string? rawPassword, PasswordHash? hash);
}

/// <summary>JWT 签发（用户令牌带 sid 会话标识，管理员令牌带 role=admin）</summary>
public interface ITokenIssuer
{
    /// <summary>签发用户访问令牌</summary>
    string IssueUserToken(User user, string sessionId);

    /// <summary>签发管理员访问令牌</summary>
    string IssueAdminToken(Admin admin);

    /// <summary>生成高熵刷新令牌</summary>
    string GenerateRefreshToken();
}

/// <summary>
/// 登录会话存储（Redis）。
/// 多端登录的全部键位规则收敛在实现里：
/// token:refresh:{sid} / token:refresh:lookup:{hash} / sess:meta:{sid} / sess:{userId}
/// </summary>
public interface ISessionStore
{
    /// <summary>创建会话（写设备元数据 + 登记到用户会话集合）</summary>
    Task CreateAsync(int userId, string sessionId, string deviceName, string? ip, CancellationToken ct = default);

    /// <summary>保存刷新令牌 + 哈希反查索引（O(1) 反查 userId:sessionId）</summary>
    Task StoreRefreshTokenAsync(int userId, string sessionId, string refreshToken, CancellationToken ct = default);

    /// <summary>按刷新令牌反查所属会话</summary>
    Task<SessionLookupResult?> LookupByRefreshTokenAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>该会话当前有效的刷新令牌（用于二次校验，防止旧令牌复用）</summary>
    Task<string?> GetCurrentRefreshTokenAsync(string sessionId, CancellationToken ct = default);

    /// <summary>作废某个刷新令牌的反查索引（轮换时调用）</summary>
    Task RemoveRefreshLookupAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>会话是否仍然有效（JWT 校验与 WS 握手都用它判断是否已被踢下线）</summary>
    Task<bool> IsSessionValidAsync(string sessionId, CancellationToken ct = default);

    Task<bool> BelongsToUserAsync(int userId, string sessionId, CancellationToken ct = default);

    /// <summary>刷新最后活跃时间</summary>
    Task TouchAsync(string sessionId, CancellationToken ct = default);

    /// <summary>该用户的全部会话（含设备元数据，按最后活跃倒序）</summary>
    Task<IReadOnlyList<LoginSession>> ListSessionsAsync(int userId, CancellationToken ct = default);

    /// <summary>移除单个会话（令牌 + 元数据 + 集合成员）。不负责断开 WS，由调用方配合 IConnectionRegistry</summary>
    Task RemoveAsync(int userId, string sessionId, CancellationToken ct = default);

    /// <summary>该用户的全部会话 ID</summary>
    Task<IReadOnlyList<string>> ListSessionIdsAsync(int userId, CancellationToken ct = default);
}

/// <summary>刷新令牌反查结果</summary>
public sealed record SessionLookupResult(int UserId, string SessionId);

/// <summary>一个登录会话（设备）</summary>
public sealed class LoginSession
{
    public required string SessionId { get; init; }

    public required string DeviceName { get; init; }

    public required string Ip { get; init; }

    /// <summary>创建时间（Unix 毫秒）</summary>
    public required long CreatedAt { get; init; }

    /// <summary>最后活跃时间（Unix 毫秒）</summary>
    public required long LastActiveAt { get; init; }
}

/// <summary>
/// 会话终止服务：删除会话 + 断开对应 WS 连接。
/// 单独成一个端口，因为「改密/封禁/踢下线」三条链路都要这套组合动作，
/// 原先在 AuthService.RemoveSessionAsync 里，被 AdminService 反向依赖 AuthService 才能复用。
/// </summary>
public interface ISessionTerminator
{
    /// <summary>踢掉指定会话</summary>
    Task TerminateAsync(int userId, string sessionId, CancellationToken ct = default);

    /// <summary>踢掉该用户全部会话（封禁/改密/管理员强制下线）</summary>
    Task TerminateAllAsync(int userId, CancellationToken ct = default);

    /// <summary>踢掉除当前设备外的全部会话，返回被踢数量</summary>
    Task<int> TerminateOthersAsync(int userId, string currentSessionId, CancellationToken ct = default);
}
