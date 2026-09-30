using System.Globalization;
using LHZ.OnlineChat.Application.Abstractions;
using StackExchange.Redis;

namespace LHZ.OnlineChat.Infrastructure.Caching;

/// <summary>
/// 登录失败限流（Redis 固定窗口计数）。
///
/// 阈值取值的考虑：账号维度 10 次足够覆盖真人反复记错密码，
/// 又把单账号的穷举速率压到每 15 分钟 10 次（10⁶ 空间要跑几十年）；
/// IP 维度放宽到 50 次，因为公司/校园出口 NAT 后面可能有很多正常用户共享一个 IP，
/// 定得太严会把整栋楼一起挡在门外。
/// </summary>
internal sealed class LoginThrottle : ILoginThrottle
{
    /// <summary>账号维度：窗口内允许的失败次数</summary>
    private const int MaxAccountFailures = 10;

    /// <summary>IP 维度：窗口内允许的失败次数（NAT 共享出口，需留余量）</summary>
    private const int MaxIpFailures = 50;

    /// <summary>计数窗口 = 锁定时长</summary>
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private const string AccountScope = "account";
    private const string IpScope = "ip";

    private readonly IDatabase _db;

    public LoginThrottle(RedisConnection redis) => _db = redis.Database;

    public async Task<int> GetLockoutSecondsAsync(
        string accountKey, string? ip, CancellationToken ct = default)
    {
        var accountLock = await RemainingLockoutAsync(AccountScope, accountKey, MaxAccountFailures)
            .ConfigureAwait(false);

        if (accountLock > 0) return accountLock;

        return string.IsNullOrWhiteSpace(ip)
            ? 0
            : await RemainingLockoutAsync(IpScope, ip, MaxIpFailures).ConfigureAwait(false);
    }

    public async Task RecordFailureAsync(string accountKey, string? ip, CancellationToken ct = default)
    {
        await IncrementAsync(AccountScope, accountKey).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(ip)) await IncrementAsync(IpScope, ip).ConfigureAwait(false);
    }

    public async Task ResetAsync(string accountKey, string? ip, CancellationToken ct = default)
    {
        // 只清账号维度：IP 维度保留，否则攻击者只要中间猜对一个弱密码账号
        // 就能把自己的 IP 计数清零，继续喷洒
        await _db.KeyDeleteAsync(RedisKeys.LoginFailures(AccountScope, accountKey)).ConfigureAwait(false);
    }

    private async Task<int> RemainingLockoutAsync(string scope, string identifier, int threshold)
    {
        var key = RedisKeys.LoginFailures(scope, identifier);

        var value = await _db.StringGetAsync(key).ConfigureAwait(false);
        if (value.IsNullOrEmpty) return 0;

        if (!long.TryParse(value.ToString(), CultureInfo.InvariantCulture, out var failures)
            || failures < threshold)
        {
            return 0;
        }

        var ttl = await _db.KeyTimeToLiveAsync(key).ConfigureAwait(false);

        // 没有 TTL（理论上不该发生）时按整窗上报，宁可多锁一会儿也不要变成永久锁
        return ttl.HasValue
            ? Math.Max(1, (int)Math.Ceiling(ttl.Value.TotalSeconds))
            : (int)Window.TotalSeconds;
    }

    private async Task IncrementAsync(string scope, string identifier)
    {
        var key = RedisKeys.LoginFailures(scope, identifier);
        var failures = await _db.StringIncrementAsync(key).ConfigureAwait(false);

        // 首次失败才设 TTL：后续失败不续期，否则持续攻击会让窗口永不结束，
        // 把真实用户一起永久锁死
        if (failures == 1) await _db.KeyExpireAsync(key, Window).ConfigureAwait(false);
    }
}
