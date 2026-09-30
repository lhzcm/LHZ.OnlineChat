using System.Globalization;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Infrastructure.Security;
using StackExchange.Redis;

namespace LHZ.OnlineChat.Infrastructure.Caching;

/// <summary>
/// 管理员会话存储（Redis）。
///
/// 结构与用户会话一致但键位独立：
///   admin:sess:{sid}        → adminId（存在即有效，TTL = 令牌有效期）
///   admin:sess:of:{adminId} → 该管理员的 sid 集合（用于一次性全部吊销）
/// </summary>
internal sealed class AdminSessionStore : IAdminSessionStore
{
    private readonly IDatabase _db;
    private readonly TimeSpan _ttl;

    public AdminSessionStore(RedisConnection redis, JwtOptions jwt)
    {
        _db = redis.Database;

        // 会话记录不必比令牌活得更久：令牌过期后这条记录已无意义。
        // 留 5 分钟余量，避免时钟误差导致「令牌还没过期但会话已消失」。
        _ttl = TimeSpan.FromMinutes(Math.Max(1, jwt.ExpireMinutes) + 5);
    }

    public async Task CreateAsync(int adminId, string sessionId, CancellationToken ct = default)
    {
        await _db
            .StringSetAsync(
                AdminSessionKey(sessionId),
                adminId.ToString(CultureInfo.InvariantCulture),
                _ttl)
            .ConfigureAwait(false);

        var setKey = AdminSessionSetKey(adminId);
        await _db.SetAddAsync(setKey, sessionId).ConfigureAwait(false);

        // 集合本身也要有 TTL，否则过期 sid 会永久堆积（用户会话集合曾踩过这个坑）。
        // 每次新会话都续期，集合的寿命 = 最后一次登录 + TTL
        await _db.KeyExpireAsync(setKey, _ttl).ConfigureAwait(false);
    }

    public Task<bool> IsValidAsync(string sessionId, CancellationToken ct = default)
        => string.IsNullOrEmpty(sessionId)
            ? Task.FromResult(false)
            : _db.KeyExistsAsync(AdminSessionKey(sessionId));

    public async Task RevokeAllAsync(int adminId, CancellationToken ct = default)
    {
        var setKey = AdminSessionSetKey(adminId);
        var sessionIds = await _db.SetMembersAsync(setKey).ConfigureAwait(false);

        foreach (var sessionId in sessionIds)
        {
            await _db.KeyDeleteAsync(AdminSessionKey(sessionId.ToString())).ConfigureAwait(false);
        }

        await _db.KeyDeleteAsync(setKey).ConfigureAwait(false);
    }

    private static string AdminSessionKey(string sessionId) => $"admin:sess:{sessionId}";

    private static string AdminSessionSetKey(int adminId) => $"admin:sess:of:{adminId}";
}
