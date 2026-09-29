using System.Security.Cryptography;
using System.Text;
using LHZ.FastJson;
using LHZ.OnlineChat.Application.Abstractions;
using StackExchange.Redis;

namespace LHZ.OnlineChat.Infrastructure.Caching;

/// <summary>
/// 登录会话存储（Redis）。多端登录的键位与过期策略全部收敛在这里。
/// </summary>
internal sealed class SessionStore : ISessionStore
{
    /// <summary>会话与刷新令牌的有效期</summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

    private readonly IDatabase _db;

    public SessionStore(RedisConnection redis) => _db = redis.Database;

    public async Task CreateAsync(
        int userId, string sessionId, string deviceName, string? ip, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var meta = new SessionMetadata
        {
            DeviceName = deviceName,
            Ip = ip ?? string.Empty,
            CreatedAt = now,
            LastActiveAt = now
        };

        await _db.StringSetAsync(
            RedisKeys.SessionMeta(sessionId), JsonConvert.Serialize(meta), Ttl).ConfigureAwait(false);
        await _db.SetAddAsync(RedisKeys.UserSessions(userId), sessionId).ConfigureAwait(false);
    }

    public async Task StoreRefreshTokenAsync(
        int userId, string sessionId, string refreshToken, CancellationToken ct = default)
    {
        await _db.StringSetAsync(RedisKeys.RefreshToken(sessionId), refreshToken, Ttl).ConfigureAwait(false);
        await _db.StringSetAsync(
            RedisKeys.RefreshLookup(HashToken(refreshToken)),
            $"{userId}:{sessionId}",
            Ttl).ConfigureAwait(false);
    }

    public async Task<SessionLookupResult?> LookupByRefreshTokenAsync(
        string refreshToken, CancellationToken ct = default)
    {
        var raw = await _db.StringGetAsync(RedisKeys.RefreshLookup(HashToken(refreshToken)))
            .ConfigureAwait(false);
        if (raw.IsNullOrEmpty) return null;

        var value = raw.ToString();
        var separator = value.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1) return null;

        if (!int.TryParse(
                value.AsSpan(0, separator),
                System.Globalization.CultureInfo.InvariantCulture,
                out var userId))
        {
            return null;
        }

        var sessionId = value[(separator + 1)..];
        return sessionId.Length == 0 ? null : new SessionLookupResult(userId, sessionId);
    }

    public async Task<string?> GetCurrentRefreshTokenAsync(string sessionId, CancellationToken ct = default)
    {
        var value = await _db.StringGetAsync(RedisKeys.RefreshToken(sessionId)).ConfigureAwait(false);
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    public Task RemoveRefreshLookupAsync(string refreshToken, CancellationToken ct = default)
        => _db.KeyDeleteAsync(RedisKeys.RefreshLookup(HashToken(refreshToken)));

    /// <summary>
    /// 会话是否仍有效。
    /// 必须走异步 API：同步 KeyExists 在高并发下会因 Redis 命令堆积触发 5s 超时，
    /// 表现为空 500（压测时踩过）。
    /// </summary>
    public Task<bool> IsSessionValidAsync(string sessionId, CancellationToken ct = default)
        => _db.KeyExistsAsync(RedisKeys.RefreshToken(sessionId));

    public Task<bool> BelongsToUserAsync(int userId, string sessionId, CancellationToken ct = default)
        => _db.SetContainsAsync(RedisKeys.UserSessions(userId), sessionId);

    public async Task TouchAsync(string sessionId, CancellationToken ct = default)
    {
        var key = RedisKeys.SessionMeta(sessionId);
        var raw = await _db.StringGetAsync(key).ConfigureAwait(false);
        if (raw.IsNullOrEmpty) return;

        var meta = JsonConvert.Deserialize<SessionMetadata>(raw.ToString());
        if (meta is null) return;

        meta.LastActiveAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.StringSetAsync(key, JsonConvert.Serialize(meta), Ttl).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LoginSession>> ListSessionsAsync(
        int userId, CancellationToken ct = default)
    {
        var sessionIds = await ListSessionIdsAsync(userId, ct).ConfigureAwait(false);
        if (sessionIds.Count == 0) return Array.Empty<LoginSession>();

        // 批量取元数据，避免逐个往返
        var keys = sessionIds.Select(id => (RedisKey)RedisKeys.SessionMeta(id)).ToArray();
        var values = await _db.StringGetAsync(keys).ConfigureAwait(false);

        var result = new List<LoginSession>(sessionIds.Count);
        for (var i = 0; i < sessionIds.Count; i++)
        {
            if (values[i].IsNullOrEmpty) continue; // 元数据已过期，忽略

            var meta = JsonConvert.Deserialize<SessionMetadata>(values[i].ToString());
            if (meta is null) continue;

            result.Add(new LoginSession
            {
                SessionId = sessionIds[i],
                DeviceName = meta.DeviceName,
                Ip = meta.Ip,
                CreatedAt = meta.CreatedAt,
                LastActiveAt = meta.LastActiveAt
            });
        }

        return result;
    }

    public async Task RemoveAsync(int userId, string sessionId, CancellationToken ct = default)
    {
        // 刷新令牌与反查索引
        var refreshToken = await GetCurrentRefreshTokenAsync(sessionId, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(refreshToken))
            await RemoveRefreshLookupAsync(refreshToken, ct).ConfigureAwait(false);

        await _db.KeyDeleteAsync(RedisKeys.RefreshToken(sessionId)).ConfigureAwait(false);
        await _db.KeyDeleteAsync(RedisKeys.SessionMeta(sessionId)).ConfigureAwait(false);
        await _db.SetRemoveAsync(RedisKeys.UserSessions(userId), sessionId).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListSessionIdsAsync(int userId, CancellationToken ct = default)
    {
        var members = await _db.SetMembersAsync(RedisKeys.UserSessions(userId)).ConfigureAwait(false);
        return members.Select(m => m.ToString()).ToList();
    }

    /// <summary>刷新令牌的 SHA256（作为反查索引键，避免把令牌原文写进键名）</summary>
    private static string HashToken(string refreshToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken))).ToLowerInvariant();

    /// <summary>Redis 里存的会话元数据</summary>
    private sealed class SessionMetadata
    {
        public string DeviceName { get; set; } = string.Empty;

        public string Ip { get; set; } = string.Empty;

        public long CreatedAt { get; set; }

        public long LastActiveAt { get; set; }
    }
}

/// <summary>
/// 会话终止：删 Redis 会话 + 断开对应 WS 连接。
/// 改造前这套组合动作在 AuthService 里，导致 AdminService 为了复用它反向依赖 AuthService
/// （管理后台依赖用户认证服务，层次颠倒）。现在它是一个独立的端口实现。
/// </summary>
internal sealed class SessionTerminator : ISessionTerminator
{
    private readonly ISessionStore _sessions;
    private readonly IConnectionRegistry _connections;

    public SessionTerminator(ISessionStore sessions, IConnectionRegistry connections)
    {
        _sessions = sessions;
        _connections = connections;
    }

    public async Task TerminateAsync(int userId, string sessionId, CancellationToken ct = default)
    {
        await _sessions.RemoveAsync(userId, sessionId, ct).ConfigureAwait(false);
        _connections.CloseSession(sessionId);
    }

    public async Task TerminateAllAsync(int userId, CancellationToken ct = default)
    {
        var sessionIds = await _sessions.ListSessionIdsAsync(userId, ct).ConfigureAwait(false);
        foreach (var sessionId in sessionIds)
        {
            await TerminateAsync(userId, sessionId, ct).ConfigureAwait(false);
        }
    }

    public async Task<int> TerminateOthersAsync(
        int userId, string currentSessionId, CancellationToken ct = default)
    {
        var sessionIds = await _sessions.ListSessionIdsAsync(userId, ct).ConfigureAwait(false);
        var kicked = 0;

        foreach (var sessionId in sessionIds)
        {
            if (sessionId == currentSessionId) continue;
            await TerminateAsync(userId, sessionId, ct).ConfigureAwait(false);
            kicked++;
        }

        return kicked;
    }
}
