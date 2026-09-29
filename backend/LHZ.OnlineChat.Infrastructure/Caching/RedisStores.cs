using LHZ.FastJson;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Users;
using StackExchange.Redis;

namespace LHZ.OnlineChat.Infrastructure.Caching;

/// <summary>邮箱验证码存储（Redis）</summary>
internal sealed class VerificationCodeStore : IVerificationCodeStore
{
    private readonly IDatabase _db;

    public VerificationCodeStore(RedisConnection redis) => _db = redis.Database;

    public Task<bool> HasPendingCodeAsync(Email email, CancellationToken ct = default)
        => _db.KeyExistsAsync(RedisKeys.EmailCode(email.Value));

    public Task SaveAsync(Email email, string code, TimeSpan ttl, CancellationToken ct = default)
        => _db.StringSetAsync(RedisKeys.EmailCode(email.Value), code, ttl);

    /// <summary>校验通过即删除（一次性使用）</summary>
    public async Task<bool> ValidateAndConsumeAsync(
        Email email, string? code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        var key = RedisKeys.EmailCode(email.Value);
        var stored = await _db.StringGetAsync(key).ConfigureAwait(false);
        if (stored.IsNullOrEmpty || stored.ToString() != code) return false;

        await _db.KeyDeleteAsync(key).ConfigureAwait(false);
        return true;
    }
}

/// <summary>在线状态存储（Redis）</summary>
internal sealed class PresenceStore : IPresenceStore
{
    private readonly IDatabase _db;

    public PresenceStore(RedisConnection redis) => _db = redis.Database;

    public Task MarkOnlineAsync(int userId, CancellationToken ct = default)
        => _db.StringSetAsync(
            RedisKeys.Online(userId),
            JsonConvert.Serialize(new
            {
                UserId = userId,
                ConnectedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }));

    public Task MarkOfflineAsync(int userId, CancellationToken ct = default)
        => _db.KeyDeleteAsync(RedisKeys.Online(userId));

    public Task<bool> IsOnlineAsync(int userId, CancellationToken ct = default)
        => _db.KeyExistsAsync(RedisKeys.Online(userId));

    /// <summary>
    /// 批量查在线状态。
    /// 原实现是在 foreach 里逐个 await KeyExists（好友多时是 N 次 Redis 往返），
    /// 这里改成一次批量 StringGet。
    /// </summary>
    public async Task<IReadOnlyDictionary<int, bool>> GetStatesAsync(
        IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, bool>();

        var keys = ids.Select(id => (RedisKey)RedisKeys.Online(id)).ToArray();
        var values = await _db.StringGetAsync(keys).ConfigureAwait(false);

        var result = new Dictionary<int, bool>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            result[ids[i]] = !values[i].IsNullOrEmpty;
        }

        return result;
    }
}

/// <summary>
/// 最近消息缓存（Redis List，每会话保留 50 条）。
/// 缓存的是 WS 协议报文快照，与推送给客户端的内容一致。
/// </summary>
internal sealed class RecentMessageCache : IRecentMessageCache
{
    /// <summary>每会话保留条数</summary>
    private const int RetainCount = 50;

    private readonly IDatabase _db;

    public RecentMessageCache(RedisConnection redis) => _db = redis.Database;

    public async Task AppendAsync(
        ChatSessionType sessionType, long sessionKeyLeft, long sessionKeyRight,
        RealtimeMessage message, CancellationToken ct = default)
    {
        var key = ResolveKey(sessionType, sessionKeyLeft, sessionKeyRight);
        var payload = JsonConvert.Serialize(CachedPayload.Create(message));

        await _db.ListLeftPushAsync(key, payload).ConfigureAwait(false);
        await _db.ListTrimAsync(key, 0, RetainCount - 1).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CachedMessage>> GetPrivateAsync(
        int userId, int peerId, CancellationToken ct = default)
    {
        var items = await _db.ListRangeAsync(RedisKeys.PrivateChat(userId, peerId)).ConfigureAwait(false);
        if (items.Length == 0) return Array.Empty<CachedMessage>();

        var result = new List<CachedMessage>(items.Length);
        foreach (var item in items)
        {
            var parsed = TryParse(item.ToString());
            if (parsed is not null) result.Add(parsed);
        }

        return result;
    }

    /// <summary>
    /// 从缓存移除指定消息（撤回时）。
    /// Redis List 不支持按内容删除中间元素，只能读出、过滤、重建。
    /// </summary>
    public async Task RemoveAsync(
        ChatSessionType sessionType, long sessionKeyLeft, long sessionKeyRight,
        string publicMessageId, long databaseId, CancellationToken ct = default)
    {
        var key = ResolveKey(sessionType, sessionKeyLeft, sessionKeyRight);
        var items = await _db.ListRangeAsync(key).ConfigureAwait(false);
        if (items.Length == 0) return;

        var databaseIdText = databaseId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var keep = items
            .Select(i => i.ToString())
            .Where(json => !MatchesMessageId(json, publicMessageId) && !MatchesMessageId(json, databaseIdText))
            .ToList();

        if (keep.Count == items.Length) return; // 缓存里没有这条

        await _db.KeyDeleteAsync(key).ConfigureAwait(false);
        // 原列表是「新消息在前」，逆序 LeftPush 才能恢复顺序
        for (var i = keep.Count - 1; i >= 0; i--)
        {
            await _db.ListLeftPushAsync(key, keep[i]).ConfigureAwait(false);
        }
    }

    private static bool MatchesMessageId(string json, string messageId)
        => json.Contains($"\"messageId\":\"{messageId}\"", StringComparison.Ordinal);

    private static string ResolveKey(ChatSessionType sessionType, long left, long right)
        => sessionType == ChatSessionType.Private
            ? RedisKeys.PrivateChat((int)left, (int)right)
            : RedisKeys.GroupChat(left);

    private static CachedMessage? TryParse(string json)
    {
        try
        {
            var payload = JsonConvert.Deserialize<CachedPayload>(json);
            if (payload is null) return null;

            return new CachedMessage
            {
                SenderId = int.TryParse(
                    payload.From, System.Globalization.CultureInfo.InvariantCulture, out var senderId)
                    ? senderId
                    : 0,
                SenderName = payload.SenderName,
                SenderAvatar = payload.SenderAvatar,
                Content = payload.Content,
                Kind = (MessageKind)payload.MessageType,
                MessageId = payload.MessageId,
                SentAt = payload.Timestamp > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(payload.Timestamp).UtcDateTime
                    : DateTime.MinValue
            };
        }
        catch (Exception)
        {
            // 单条缓存解析失败不影响整体（与改造前一致）
            return null;
        }
    }

    /// <summary>
    /// 缓存载荷。字段名与 WS 协议报文一致（camelCase），
    /// 这样缓存内容可以直接当成推送报文理解，也便于按 messageId 做文本匹配删除。
    /// </summary>
    private sealed class CachedPayload
    {
        [LHZ.FastJson.Json.Attributes.JsonProperty("from")]
        public string From { get; set; } = string.Empty;

        [LHZ.FastJson.Json.Attributes.JsonProperty("content")]
        public string Content { get; set; } = string.Empty;

        [LHZ.FastJson.Json.Attributes.JsonProperty("messageType")]
        public int MessageType { get; set; }

        [LHZ.FastJson.Json.Attributes.JsonProperty("messageId")]
        public string MessageId { get; set; } = string.Empty;

        [LHZ.FastJson.Json.Attributes.JsonProperty("senderName")]
        public string SenderName { get; set; } = string.Empty;

        [LHZ.FastJson.Json.Attributes.JsonProperty("senderAvatar")]
        public string? SenderAvatar { get; set; }

        [LHZ.FastJson.Json.Attributes.JsonProperty("timestamp")]
        public long Timestamp { get; set; }

        internal static CachedPayload Create(RealtimeMessage message) => new()
        {
            From = message.SenderId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Content = message.Content,
            MessageType = (int)message.Kind,
            MessageId = message.MessageId,
            SenderName = message.SenderName,
            SenderAvatar = message.SenderAvatar,
            Timestamp = UtcTime.ToUnixMilliseconds(message.SentAt)
        };
    }
}
