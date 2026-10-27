using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Messaging;

/// <summary>消息内容类型（替代魔法数 0/1/2；数值与既有库表一致）</summary>
public enum MessageKind
{
    Text = 0,
    Image = 1,
    File = 2,

    /// <summary>
    /// Markdown 源文本，由客户端渲染（3 是新增值，历史库表里不存在）。
    /// 存的是**源文本**而不是渲染后的 HTML：渲染规则与 XSS 消毒属于表现层，
    /// 入库 HTML 会让转义口径同时散落在服务端与客户端，且改一次渲染规则就要洗历史数据。
    /// </summary>
    Markdown = 3,

    /// <summary>
    /// 语音消息（5 是新增值，历史库表里不存在）。
    /// content 是**音频地址**（与图片一致，不塞 JSON）：服务端只负责存取与转发，
    /// 时长由客户端从音频元数据读取 —— 落库时长要多一个字段、多一次协议变更，
    /// 而列表预览只需要「[语音]」这个占位。
    /// </summary>
    Voice = 5,

    /// <summary>
    /// 「聊天记录」合并转发卡片（4 是新增值，历史库表里不存在）。
    /// content 是一段 JSON 快照（结构见 Application/Messaging/ForwardBundle.cs）：
    /// 一次转发 = 一条消息 = 一个卡片，里面最多 20 条只读明细。
    /// 快照而不是引用：原消息可能被撤回/删除，且引用会让历史第一页多出一次 JOIN；
    /// 快照也让 Redis 最近消息缓存无需改动（缓存存的就是 content 原样）。
    /// </summary>
    Forward = 4
}

/// <summary>
/// 消息内容与类型的校验规则（私聊 / 群聊共用一份，避免两处各写一套后漂移）。
///
/// 之前两者都不校验，代价是：
///   - 空串能入库，前端渲染出空气泡；
///   - 超长内容能撑爆数据库列与推送报文（WS 帧大小不是业务约束）；
///   - MessageKind 在 WS 入口是 (MessageKind)整数 的未检查转换，
///     攻击者可以写入协议未定义的类型，前端渲染行为未定义。
/// </summary>
public static class MessageContentRules
{
    /// <summary>
    /// 单条消息内容上限（与机器人推送 / 回复的公开文档一致）。
    /// 50000 字是为了长 Markdown（任务报告、整段代码、日志）：DB 列是 text、WS 单帧上限 4MiB，
    /// 真正的边界只剩这一处，所以前端提示与机器人文档都以它为准。
    /// 注意：这是**字符**数，UTF-8 中文约 3 字节/字（50000 字 ≈ 150KB，JSON 里再转义会更长）。
    /// </summary>
    public const int MaxLength = 50000;

    /// <summary>
    /// 聊天图片 / 语音的站内地址前缀（与 LocalFileStorage 的默认公开前缀 + 子目录一致）。
    /// Domain 不能引用 Infrastructure，所以这里是协议常量；上传目录改名时两处要一起改。
    /// </summary>
    public const string ImagePathPrefix = "/uploads/images/";

    public const string VoicePathPrefix = "/uploads/voices/";

    /// <summary>站内媒体文件名长度上限（服务端生成的是 32 位 GUID + 扩展名，留足余量即可）</summary>
    private const int MaxMediaFileNameLength = 100;

    public static void EnsureValid(string? content, MessageKind kind)
    {
        DomainException.Ensure(Enum.IsDefined(kind), "不支持的消息类型");

        var normalized = (content ?? string.Empty).Trim();
        DomainException.Ensure(normalized.Length > 0, "消息内容不能为空");
        DomainException.Ensure(
            normalized.Length <= MaxLength,
            $"消息内容不能超过 {MaxLength} 字");
        EnsureLocalMedia(normalized, kind);
    }

    /// <summary>
    /// 图片 / 语音的 content 必须是本站上传接口返回的地址。
    /// 放行任意 URL 会让每个接收者的浏览器去请求发送者指定的外链（泄露 IP、已读状态，还可做跟踪像素），
    /// 而机器人只能发文字 / Markdown，所以没有任何合法来源需要外链。其它类型不受影响。
    /// </summary>
    public static void EnsureLocalMedia(string? content, MessageKind kind)
    {
        var prefix = kind switch
        {
            MessageKind.Image => ImagePathPrefix,
            MessageKind.Voice => VoicePathPrefix,
            _ => null
        };
        if (prefix is null) return;

        DomainException.Ensure(IsLocalMediaPath(content, prefix), "图片或语音地址不合法");
    }

    /// <summary>前缀之后只能是一个文件名（字母数字 . _ -），不允许子目录、查询串与 ".."</summary>
    private static bool IsLocalMediaPath(string? content, string prefix)
    {
        if (content is null || !content.StartsWith(prefix, StringComparison.Ordinal)) return false;

        var fileName = content.AsSpan(prefix.Length);
        if (fileName.Length == 0 || fileName.Length > MaxMediaFileNameLength) return false;
        if (fileName.Contains("..", StringComparison.Ordinal)) return false;

        foreach (var ch in fileName)
        {
            var allowed = ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-';
            if (!allowed) return false;
        }

        return true;
    }
}

/// <summary>
/// @ 提及列表值对象。
/// 库里存逗号分隔字符串（如 "10000,10002"），原先 ParseMentions 在
/// MessageService 和 WsMessageHandler 各写了一份一模一样的实现。
/// </summary>
public sealed class MentionList : ValueObject
{
    public const int MaxStorageLength = 500;

    private static readonly MentionList EmptyInstance = new(Array.Empty<int>());

    private MentionList(IReadOnlyList<int> userIds) => UserIds = userIds;

    public IReadOnlyList<int> UserIds { get; }

    public static MentionList Empty => EmptyInstance;

    public bool IsEmpty => UserIds.Count == 0;

    public bool Mentions(int userId) => UserIds.Contains(userId);

    /// <summary>从账号 ID 集合创建（去重、剔除非法值）</summary>
    public static MentionList From(IEnumerable<int>? userIds)
    {
        if (userIds is null) return Empty;
        var normalized = userIds.Where(id => id > 0).Distinct().ToArray();
        return normalized.Length == 0 ? Empty : new MentionList(normalized);
    }

    /// <summary>从库里的逗号分隔字符串解析</summary>
    public static MentionList Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Empty;
        var ids = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
        return ids.Length == 0 ? Empty : new MentionList(ids);
    }

    /// <summary>序列化为库里的存储形式；空列表存 null</summary>
    public string? ToStorage() => IsEmpty ? null : string.Join(',', UserIds);

    protected override IEnumerable<object?> GetEqualityComponents() => UserIds.Select(id => (object?)id);
}

/// <summary>
/// 引用回复值对象：被引用消息的 ID + 原文预览 + 发送者昵称。
/// 实体上仍是三列（沿用既有表结构），通过只读属性聚合成一个整体。
/// </summary>
public sealed class MessageReply : ValueObject
{
    public const int MaxPreviewLength = 200;

    private MessageReply(string messageId, string? preview, string? senderName)
    {
        MessageId = messageId;
        Preview = preview;
        SenderName = senderName;
    }

    public string MessageId { get; }

    public string? Preview { get; }

    public string? SenderName { get; }

    /// <summary>创建引用；被引用 ID 为空则返回 null（表示不是引用回复）</summary>
    public static MessageReply? Create(string? messageId, string? preview, string? senderName)
    {
        if (string.IsNullOrWhiteSpace(messageId)) return null;
        var trimmedPreview = string.IsNullOrWhiteSpace(preview) ? null : preview.Trim();
        if (trimmedPreview is { Length: > MaxPreviewLength })
            trimmedPreview = trimmedPreview[..MaxPreviewLength];
        return new MessageReply(
            messageId.Trim(),
            trimmedPreview,
            string.IsNullOrWhiteSpace(senderName) ? null : senderName.Trim());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MessageId;
        yield return Preview;
        yield return SenderName;
    }
}

/// <summary>
/// 消息撤回规则：仅本人、仅 2 分钟内。
/// 原先这个时间窗硬编码在 WsMessageHandler 的 SQL 谓词里（AddMinutes(-2)），
/// 规则本身不可见也无法复用。
/// </summary>
public static class RecallPolicy
{
    /// <summary>可撤回时间窗</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

    /// <summary>可撤回的最早发送时间</summary>
    public static DateTime EarliestSentAt(DateTime now) => now - Window;

    public static bool IsWithinWindow(DateTime sentAt, DateTime now)
        => UtcTime.Normalize(sentAt) >= EarliestSentAt(now);
}

/// <summary>会话类型（私聊 / 群聊）。库里存 "private" / "group" 字符串</summary>
public enum ChatSessionType
{
    Private = 0,
    Group = 1
}

/// <summary>会话类型与库/协议里的字符串表示互转</summary>
public static class ChatSessionTypeNames
{
    public const string Private = "private";
    public const string Group = "group";

    public static string ToStorage(this ChatSessionType type)
        => type == ChatSessionType.Private ? Private : Group;

    public static ChatSessionType Parse(string? raw) => raw switch
    {
        Private => ChatSessionType.Private,
        Group => ChatSessionType.Group,
        _ => throw new DomainException("无效的会话类型")
    };

    public static ChatSessionType? TryParse(string? raw) => raw switch
    {
        Private => ChatSessionType.Private,
        Group => ChatSessionType.Group,
        _ => null
    };
}
