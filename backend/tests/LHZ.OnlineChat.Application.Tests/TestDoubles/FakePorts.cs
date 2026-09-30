using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Application.Messaging.Commands;
using LHZ.OnlineChat.Application.Robots.EventHandlers;
using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Robots;
using LHZ.OnlineChat.Domain.Users;

namespace LHZ.OnlineChat.Application.Tests.TestDoubles;

/// <summary>固定时钟</summary>
internal sealed class FakeClock : IClock
{
    internal static readonly DateTime Default = new(2026, 3, 14, 10, 30, 0, DateTimeKind.Utc);

    public DateTime UtcNow { get; set; } = Default;

    internal void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>
/// 口令哈希替身：用可读的前缀代替真实 BCrypt，让断言能直接比对。
/// 真实 BCrypt 的正确性由 Infrastructure 测试覆盖，这里只关心用例编排。
/// </summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    private const string Prefix = "hashed:";

    public PasswordHash Hash(string rawPassword) => PasswordHash.FromHash(Prefix + rawPassword);

    public bool Verify(string? rawPassword, PasswordHash? hash)
        => rawPassword is not null && hash is not null && hash.Value == Prefix + rawPassword;

    internal static PasswordHash Of(string rawPassword) => PasswordHash.FromHash(Prefix + rawPassword);
}

/// <summary>令牌签发替身：产出可预测的串，便于断言「签发了谁的令牌」</summary>
internal sealed class FakeTokenIssuer : ITokenIssuer
{
    private int _refreshCounter;

    public string IssueUserToken(User user, string sessionId) => $"user-token:{user.Id}:{sessionId}";

    public string GenerateRefreshToken() => $"refresh-{++_refreshCounter}";

    public string IssueAdminToken(Admin admin, string sessionId) =>  $"admin-token:{admin.Id}:{(int)admin.Role}";
}

/// <summary>
/// 登录会话存储替身：真实维护「会话集合 + 刷新令牌 + 反查索引」三者关系，
/// 这样令牌轮换、防复用这类顺序敏感的逻辑才测得出来。
/// </summary>
internal sealed class FakeSessionStore : ISessionStore
{
    private readonly Dictionary<int, HashSet<string>> _userSessions = new();
    private readonly Dictionary<string, string> _refreshBySession = new();
    private readonly Dictionary<string, SessionLookupResult> _lookupByToken = new();
    private readonly Dictionary<string, LoginSession> _meta = new();

    internal List<string> TouchedSessions { get; } = new();

    public Task CreateAsync(
        int userId, string sessionId, string deviceName, string? ip, CancellationToken ct = default)
    {
        if (!_userSessions.TryGetValue(userId, out var set))
        {
            set = new HashSet<string>();
            _userSessions[userId] = set;
        }

        set.Add(sessionId);
        var now = new DateTimeOffset(FakeClock.Default).ToUnixTimeMilliseconds();
        _meta[sessionId] = new LoginSession
        {
            SessionId = sessionId,
            DeviceName = deviceName,
            Ip = ip ?? string.Empty,
            CreatedAt = now,
            LastActiveAt = now
        };
        return Task.CompletedTask;
    }

    public Task StoreRefreshTokenAsync(
        int userId, string sessionId, string refreshToken, CancellationToken ct = default)
    {
        _refreshBySession[sessionId] = refreshToken;
        _lookupByToken[refreshToken] = new SessionLookupResult(userId, sessionId);
        return Task.CompletedTask;
    }

    public Task<SessionLookupResult?> LookupByRefreshTokenAsync(
        string refreshToken, CancellationToken ct = default)
        => Task.FromResult(_lookupByToken.GetValueOrDefault(refreshToken));

    public Task<string?> GetCurrentRefreshTokenAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(_refreshBySession.GetValueOrDefault(sessionId));

    public Task RemoveRefreshLookupAsync(string refreshToken, CancellationToken ct = default)
    {
        _lookupByToken.Remove(refreshToken);
        return Task.CompletedTask;
    }

    public Task<bool> IsSessionValidAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(_refreshBySession.ContainsKey(sessionId));

    public Task<bool> BelongsToUserAsync(int userId, string sessionId, CancellationToken ct = default)
        => Task.FromResult(_userSessions.TryGetValue(userId, out var set) && set.Contains(sessionId));

    public Task TouchAsync(string sessionId, CancellationToken ct = default)
    {
        TouchedSessions.Add(sessionId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LoginSession>> ListSessionsAsync(
        int userId, CancellationToken ct = default)
    {
        var ids = _userSessions.GetValueOrDefault(userId) ?? new HashSet<string>();
        IReadOnlyList<LoginSession> items = ids
            .Where(_meta.ContainsKey)
            .Select(id => _meta[id])
            .ToList();
        return Task.FromResult(items);
    }

    public Task RemoveAsync(int userId, string sessionId, CancellationToken ct = default)
    {
        if (_refreshBySession.Remove(sessionId, out var token)) _lookupByToken.Remove(token);
        _meta.Remove(sessionId);
        _userSessions.GetValueOrDefault(userId)?.Remove(sessionId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListSessionIdsAsync(
        int userId, CancellationToken ct = default)
    {
        IReadOnlyList<string> items = (_userSessions.GetValueOrDefault(userId) ?? new HashSet<string>())
            .ToList();
        return Task.FromResult(items);
    }

    /// <summary>测试辅助：为某用户预置一个已登录会话</summary>
    internal async Task SeedSessionAsync(int userId, string sessionId, string deviceName = "设备")
    {
        await CreateAsync(userId, sessionId, deviceName, "1.2.3.4");
        await StoreRefreshTokenAsync(userId, sessionId, $"refresh-for-{sessionId}");
    }
}

/// <summary>会话终止替身：记录被终止的会话，供断言「封禁/改密确实踢了人」</summary>
internal sealed class FakeSessionTerminator : ISessionTerminator
{
    private readonly FakeSessionStore? _store;

    internal FakeSessionTerminator(FakeSessionStore? store = null) => _store = store;

    internal List<(int UserId, string SessionId)> Terminated { get; } = new();

    internal List<int> TerminatedAllFor { get; } = new();

    public async Task TerminateAsync(int userId, string sessionId, CancellationToken ct = default)
    {
        Terminated.Add((userId, sessionId));
        if (_store is not null) await _store.RemoveAsync(userId, sessionId, ct);
    }

    public async Task TerminateAllAsync(int userId, CancellationToken ct = default)
    {
        TerminatedAllFor.Add(userId);
        if (_store is null) return;

        foreach (var sessionId in await _store.ListSessionIdsAsync(userId, ct))
        {
            await TerminateAsync(userId, sessionId, ct);
        }
    }

    public async Task<int> TerminateOthersAsync(
        int userId, string currentSessionId, CancellationToken ct = default)
    {
        if (_store is null) return 0;

        var ids = (await _store.ListSessionIdsAsync(userId, ct))
            .Where(id => id != currentSessionId).ToList();

        foreach (var id in ids) await TerminateAsync(userId, id, ct);
        return ids.Count;
    }
}

/// <summary>验证码存储替身：可控制「当前有效码」，并记录消费情况</summary>
internal sealed class FakeVerificationCodeStore : IVerificationCodeStore
{
    private readonly Dictionary<string, string> _codes = new(StringComparer.Ordinal);

    internal List<(string Email, string Code, TimeSpan Ttl)> Saved { get; } = new();

    public Task<bool> HasPendingCodeAsync(Email email, CancellationToken ct = default)
        => Task.FromResult(_codes.ContainsKey(email.Value));

    public Task SaveAsync(Email email, string code, TimeSpan ttl, CancellationToken ct = default)
    {
        _codes[email.Value] = code;
        Saved.Add((email.Value, code, ttl));
        return Task.CompletedTask;
    }

    /// <summary>校验通过即消费掉，用于验证「验证码一次性」</summary>
    public Task<bool> ValidateAndConsumeAsync(
        Email email, string? code, CancellationToken ct = default)
    {
        if (code is null || !_codes.TryGetValue(email.Value, out var stored) || stored != code)
            return Task.FromResult(false);

        _codes.Remove(email.Value);
        return Task.FromResult(true);
    }

    internal void Seed(string email, string code) => _codes[email] = code;
}

/// <summary>邮件发送替身；SmtpConfigured=false 模拟未配置 SMTP 的开发模式</summary>
internal sealed class FakeEmailSender : IEmailSender
{
    internal bool SmtpConfigured { get; set; } = true;

    internal List<(string Email, string Code)> Sent { get; } = new();

    public Task<bool> SendVerificationCodeAsync(
        Email to, string code, CancellationToken ct = default)
    {
        Sent.Add((to.Value, code));
        return Task.FromResult(SmtpConfigured);
    }
}

/// <summary>在线状态替身</summary>
internal sealed class FakePresenceStore : IPresenceStore
{
    private readonly HashSet<int> _online = new();

    public Task MarkOnlineAsync(int userId, CancellationToken ct = default)
    {
        _online.Add(userId);
        return Task.CompletedTask;
    }

    public Task MarkOfflineAsync(int userId, CancellationToken ct = default)
    {
        _online.Remove(userId);
        return Task.CompletedTask;
    }

    public Task<bool> IsOnlineAsync(int userId, CancellationToken ct = default)
        => Task.FromResult(_online.Contains(userId));

    public Task<IReadOnlyDictionary<int, bool>> GetStatesAsync(
        IEnumerable<int> userIds, CancellationToken ct = default)
    {
        IReadOnlyDictionary<int, bool> result = userIds.Distinct()
            .ToDictionary(id => id, id => _online.Contains(id));
        return Task.FromResult(result);
    }

    internal void SetOnline(params int[] userIds)
    {
        foreach (var id in userIds) _online.Add(id);
    }
}

/// <summary>最近消息缓存替身：记录追加与移除，供断言缓存被正确维护</summary>
internal sealed class FakeRecentMessageCache : IRecentMessageCache
{
    private readonly Dictionary<string, List<RealtimeMessage>> _cache = new(StringComparer.Ordinal);

    internal List<(ChatSessionType Type, long Left, long Right, RealtimeMessage Message)> Appended { get; } = new();

    internal List<(ChatSessionType Type, string PublicId, long DatabaseId)> Removed { get; } = new();

    public Task AppendAsync(
        ChatSessionType sessionType, long sessionKeyLeft, long sessionKeyRight,
        RealtimeMessage message, CancellationToken ct = default)
    {
        Appended.Add((sessionType, sessionKeyLeft, sessionKeyRight, message));

        var key = Key(sessionType, sessionKeyLeft, sessionKeyRight);
        if (!_cache.TryGetValue(key, out var list))
        {
            list = new List<RealtimeMessage>();
            _cache[key] = list;
        }
        list.Insert(0, message);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CachedMessage>> GetPrivateAsync(
        int userId, int peerId, CancellationToken ct = default)
    {
        var key = Key(ChatSessionType.Private, userId, peerId);
        IReadOnlyList<CachedMessage> items = (_cache.GetValueOrDefault(key) ?? new())
            .Select(m => new CachedMessage
            {
                SenderId = m.SenderId,
                SenderName = m.SenderName,
                SenderAvatar = m.SenderAvatar,
                Content = m.Content,
                Kind = m.Kind,
                MessageId = m.MessageId,
                SentAt = m.SentAt
            })
            .ToList();
        return Task.FromResult(items);
    }

    public Task RemoveAsync(
        ChatSessionType sessionType, long sessionKeyLeft, long sessionKeyRight,
        string publicMessageId, long databaseId, CancellationToken ct = default)
    {
        Removed.Add((sessionType, publicMessageId, databaseId));

        var key = Key(sessionType, sessionKeyLeft, sessionKeyRight);
        _cache.GetValueOrDefault(key)?.RemoveAll(m => m.MessageId == publicMessageId);
        return Task.CompletedTask;
    }

    /// <summary>与真实实现一致：私聊按「小 ID:大 ID」归一化，保证双向同一个键</summary>
    private static string Key(ChatSessionType type, long left, long right)
        => type == ChatSessionType.Private
            ? $"private:{Math.Min(left, right)}:{Math.Max(left, right)}"
            : $"group:{left}";
}

/// <summary>文件存储替身</summary>
internal sealed class FakeFileStorage : IFileStorage
{
    internal List<(string FileName, string Subdirectory)> Saved { get; } = new();

    public Task<string> SaveAsync(
        FileUpload upload, string subdirectory, CancellationToken ct = default)
    {
        Saved.Add((upload.FileName, subdirectory));
        var prefix = string.IsNullOrEmpty(subdirectory) ? "/uploads" : $"/uploads/{subdirectory}";
        return Task.FromResult($"{prefix}/stored{upload.Extension}");
    }
}

/// <summary>Webhook 调度替身：可编排成功/失败与回复内容</summary>
internal sealed class FakeWebhookDispatcher : IWebhookDispatcher
{
    internal WebhookDispatchResult NextResult { get; set; } = new() { Success = true };

    internal List<WebhookEvent> Dispatched { get; } = new();

    public Task<WebhookDispatchResult> DispatchAsync(
        Robot robot, WebhookEvent payload, CancellationToken ct = default)
    {
        Dispatched.Add(payload);
        return Task.FromResult(NextResult);
    }
}

/// <summary>HMAC 验签替身：默认放行，可切换为拒绝以测试验签分支</summary>
internal sealed class FakeWebhookSigner : IWebhookSigner
{
    internal bool VerifyResult { get; set; } = true;

    public string Sign(string secret, string body) => $"sig({secret}:{body.Length})";

    public bool Verify(string secret, string body, string? signature) => VerifyResult;
}

/// <summary>机器人令牌加解密替身：可逆的明文编码，便于构造与断言</summary>
internal sealed class FakeRobotTokenCipher : IRobotTokenCipher
{
    private const string Prefix = "tok-";

    public string Encode(long robotId) => Prefix + robotId.ToString(
        System.Globalization.CultureInfo.InvariantCulture);

    public long Decode(string? token)
        => token is not null
           && token.StartsWith(Prefix, StringComparison.Ordinal)
           && long.TryParse(token[Prefix.Length..], out var id)
            ? id
            : 0;
}

/// <summary>禁言提示语格式化替身（固定格式，避免依赖宿主时区库）</summary>
internal sealed class FakeMuteMessageFormatter : IMuteMessageFormatter
{
    public string Format(DateTime mutedUntilUtc)
        => $"你已被禁言至 {mutedUntilUtc:MM-dd HH:mm}，期间无法在群里发言";
}

/// <summary>审计日志替身：记录写入内容，供断言「关键操作都留痕」</summary>
internal sealed class FakeAuditLogger : IAuditLogger
{
    internal List<(int AdminId, string Action, string TargetType, string? TargetId, string? Detail)>
        Records { get; } = new();

    public Task RecordAsync(
        int adminId, string action, string targetType, string? targetId, string? detail,
        CancellationToken ct = default)
    {
        Records.Add((adminId, action, targetType, targetId, detail));
        return Task.CompletedTask;
    }

    internal bool Has(string action) => Records.Exists(r => r.Action == action);
}

/// <summary>当前调用者替身</summary>
internal sealed class FakeCurrentUser : ICurrentUser
{
    public int UserId { get; set; }

    public string SessionId { get; set; } = "session-1";

    public int AdminId { get; set; }

    public string? ClientIp { get; set; } = "1.2.3.4";
}

/// <summary>机器人会话服务替身：记录后台调度请求（真实实现是 fire-and-forget）</summary>
internal sealed class FakeRobotConversationService : IRobotConversationService
{
    internal List<(long RobotId, WebhookEvent Payload, RobotReplyTarget Target)> Dispatched { get; } = new();

    public void DispatchInBackground(
        long robotId, WebhookEvent payload, RobotReplyTarget replyTarget)
        => Dispatched.Add((robotId, payload, replyTarget));
}
