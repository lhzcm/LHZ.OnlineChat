using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Robots;
using LHZ.OnlineChat.Domain.Users;

namespace LHZ.OnlineChat.Application.Abstractions;

/// <summary>邮件发送端口</summary>
public interface IEmailSender
{
    /// <summary>发送验证码邮件。返回 false 表示未配置 SMTP（开发模式，验证码打印到控制台）</summary>
    Task<bool> SendVerificationCodeAsync(Email to, string code, CancellationToken ct = default);
}

/// <summary>
/// 邮箱验证码存储（Redis，5 分钟有效 + 冷却）。
/// 注册 / 忘记密码 / 换绑邮箱三条链路共用。
/// </summary>
public interface IVerificationCodeStore
{
    /// <summary>是否存在未过期的验证码（发送冷却判断）</summary>
    Task<bool> HasPendingCodeAsync(Email email, CancellationToken ct = default);

    Task SaveAsync(Email email, string code, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>校验验证码；正确则消费掉（一次性使用）</summary>
    Task<bool> ValidateAndConsumeAsync(Email email, string? code, CancellationToken ct = default);
}

/// <summary>
/// 最近消息缓存（Redis List，每会话保留 50 条）。
/// 键位规则 chat:private:{小ID}:{大ID} / chat:group:{groupId} 收敛在实现里 ——
/// 原先这个键的拼法在 MessageService / WsMessageHandler / BotService 各写了一遍。
/// </summary>
public interface IRecentMessageCache
{
    /// <summary>追加一条到会话缓存头部并裁剪长度</summary>
    Task AppendAsync(ChatSessionType sessionType, long sessionKeyLeft, long sessionKeyRight,
        RealtimeMessage message, CancellationToken ct = default);

    /// <summary>读取私聊会话缓存</summary>
    Task<IReadOnlyList<CachedMessage>> GetPrivateAsync(int userId, int peerId, CancellationToken ct = default);

    /// <summary>从缓存中移除指定消息（撤回时）</summary>
    Task RemoveAsync(ChatSessionType sessionType, long sessionKeyLeft, long sessionKeyRight,
        string publicMessageId, long databaseId, CancellationToken ct = default);
}

/// <summary>缓存里的一条消息快照</summary>
public sealed class CachedMessage
{
    public required int SenderId { get; init; }

    public required string SenderName { get; init; }

    public string? SenderAvatar { get; init; }

    public required string Content { get; init; }

    public required MessageKind Kind { get; init; }

    public string? MessageId { get; init; }

    public required DateTime SentAt { get; init; }
}

/// <summary>在线状态存储（Redis ws:online:{userId}，供跨进程/重启后查询）</summary>
public interface IPresenceStore
{
    Task MarkOnlineAsync(int userId, CancellationToken ct = default);

    Task MarkOfflineAsync(int userId, CancellationToken ct = default);

    Task<bool> IsOnlineAsync(int userId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<int, bool>> GetStatesAsync(IEnumerable<int> userIds, CancellationToken ct = default);
}

/// <summary>文件存储端口（头像、聊天图片）</summary>
public interface IFileStorage
{
    /// <summary>保存文件，返回可公开访问的相对地址（如 /uploads/xxx.png）</summary>
    Task<string> SaveAsync(FileUpload upload, string subdirectory, CancellationToken ct = default);
}

/// <summary>待保存的上传文件（应用层不引用 IFormFile，避免依赖 ASP.NET）</summary>
public sealed class FileUpload
{
    public required string FileName { get; init; }

    public required long Length { get; init; }

    public required Stream Content { get; init; }

    /// <summary>扩展名（小写，含点）</summary>
    public string Extension => Path.GetExtension(FileName).ToLowerInvariant();
}

/// <summary>
/// 上传文件的校验规则（大小 + 扩展名白名单）。
/// 原先头像 2MB/聊天图片 5MB 两套规则分别写在 AuthService 和 UploadsController。
/// </summary>
public static class UploadRules
{
    public static readonly string[] AllowedImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

    public const long MaxAvatarBytes = 2 * 1024 * 1024;
    public const long MaxChatImageBytes = 5 * 1024 * 1024;

    public static void EnsureValidImage(FileUpload? upload, long maxBytes, string sizeHint)
    {
        Domain.Common.DomainException.Ensure(upload is not null && upload.Length > 0, "请选择图片文件");
        Domain.Common.DomainException.Ensure(upload!.Length <= maxBytes, $"图片大小不能超过 {sizeHint}");
        Domain.Common.DomainException.Ensure(
            AllowedImageExtensions.Contains(upload.Extension),
            "仅支持 jpg / png / gif / webp 格式图片");
    }
}

/// <summary>
/// 机器人 Webhook 调度端口（HTTP POST + HMAC 签名 + 超时重试）。
/// </summary>
public interface IWebhookDispatcher
{
    /// <summary>投递事件并解析同步回复</summary>
    Task<WebhookDispatchResult> DispatchAsync(Robot robot, WebhookEvent payload, CancellationToken ct = default);
}

/// <summary>Webhook 投递结果</summary>
public sealed class WebhookDispatchResult
{
    public required bool Success { get; init; }

    /// <summary>同步回复内容（200 + {"content":"..."}）；无回复为 null</summary>
    public string? Reply { get; init; }

    public string Message { get; init; } = string.Empty;
}

/// <summary>要投递给 Webhook 的事件（应用层中性模型，JSON 形状在基础设施层定型）</summary>
public sealed class WebhookEvent
{
    public required WebhookActor Robot { get; init; }

    public required WebhookSession Session { get; init; }

    public required WebhookActor From { get; init; }

    public required WebhookMessage Message { get; init; }

    public IReadOnlyList<int> Mentions { get; init; } = Array.Empty<int>();
}

public sealed class WebhookActor
{
    public required int UserId { get; init; }

    public required string Name { get; init; }

    public string? Avatar { get; init; }

    public bool IsBot { get; init; }
}

public sealed class WebhookSession
{
    public required ChatSessionType Type { get; init; }

    /// <summary>私聊为对方账号 ID，群聊为群 ID</summary>
    public required long Id { get; init; }

    public required string Name { get; init; }
}

public sealed class WebhookMessage
{
    public required string MessageId { get; init; }

    public required string Content { get; init; }

    public required MessageKind Kind { get; init; }

    /// <summary>Unix 毫秒</summary>
    public required long Timestamp { get; init; }
}

/// <summary>HMAC-SHA256 签名计算与恒定时间比较</summary>
public interface IWebhookSigner
{
    string Sign(string secret, string body);

    bool Verify(string secret, string body, string? signature);
}
