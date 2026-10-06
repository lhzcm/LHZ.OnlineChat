using StackExchange.Redis;

namespace LHZ.OnlineChat.Infrastructure.Caching;

/// <summary>
/// Redis 连接持有者（单例）。
/// 全部 Redis 键位规则集中在 RedisKeys 里 —— 改造前这些键的拼法分散在
/// AuthService / WsConnectionManager / MessageService / BotService 各处。
///
/// 连接参数刻意不采用「连不上就抛」的默认行为：
///   AbortOnConnectFail=false —— Redis 暂时不可用时进程照常启动，
///     后台自动重连；否则 Redis 抖一下，整个应用在启动阶段直接起不来。
///   ConnectRetry / ConnectTimeout —— 首次连接失败的重试节奏，避免启动被拖住。
/// 代价是「Redis 真的挂了」时相关调用会抛错（会话校验、限流、在线状态），
/// 这属于依赖不可用，应由监控暴露，而不是让进程无法启动。
/// </summary>
internal sealed class RedisConnection : IDisposable
{
    private readonly ConnectionMultiplexer _multiplexer;

    public RedisConnection(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        options.ConnectRetry = 3;
        options.ConnectTimeout = 5000;
        options.KeepAlive = 60;

        _multiplexer = ConnectionMultiplexer.Connect(options);
        Database = _multiplexer.GetDatabase();
    }

    public IDatabase Database { get; }

    public void Dispose() => _multiplexer.Dispose();
}

/// <summary>Redis 键位约定（与改造前完全一致，保证灰度期间新旧版本可共存）</summary>
internal static class RedisKeys
{
    /// <summary>邮箱验证码</summary>
    internal static string EmailCode(string email) => $"email:code:{email}";

    /// <summary>该邮箱当前验证码的累计错误次数（超限即作废验证码，防爆破）</summary>
    internal static string EmailCodeAttempts(string email) => $"email:code:attempts:{email}";

    /// <summary>登录失败计数（按账号标识 + 来源 IP 两个维度分别限流）</summary>
    internal static string LoginFailures(string scope, string identifier)
        => $"login:fail:{scope}:{identifier}";

    /// <summary>某会话当前的刷新令牌</summary>
    internal static string RefreshToken(string sessionId) => $"token:refresh:{sessionId}";

    /// <summary>刷新令牌 → userId:sessionId 的反查索引（键名含令牌哈希）</summary>
    internal static string RefreshLookup(string tokenHash) => $"token:refresh:lookup:{tokenHash}";

    /// <summary>会话（设备）元数据</summary>
    internal static string SessionMeta(string sessionId) => $"sess:meta:{sessionId}";

    /// <summary>某用户的会话 ID 集合</summary>
    internal static string UserSessions(int userId) => $"sess:{userId}";

    /// <summary>在线状态</summary>
    internal static string Online(int userId) => $"ws:online:{userId}";

    /// <summary>私聊最近消息缓存（较小 ID 在前，保证双向同一个键）</summary>
    internal static string PrivateChat(int userId, int peerId)
        => $"chat:private:{Math.Min(userId, peerId)}:{Math.Max(userId, peerId)}";

    /// <summary>群聊最近消息缓存</summary>
    internal static string GroupChat(long groupId) => $"chat:group:{groupId}";
}
