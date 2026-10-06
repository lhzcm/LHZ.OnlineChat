using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Messaging;

/// <summary>消息内容类型（替代魔法数 0/1/2；数值与既有库表一致）</summary>
public enum MessageKind
{
    Text = 0,
    Image = 1,
    File = 2
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
    /// <summary>单条消息内容上限（与机器人推送接口的公开文档一致）</summary>
    public const int MaxLength = 5000;

    public static void EnsureValid(string? content, MessageKind kind)
    {
        DomainException.Ensure(Enum.IsDefined(kind), "不支持的消息类型");

        var normalized = (content ?? string.Empty).Trim();
        DomainException.Ensure(normalized.Length > 0, "消息内容不能为空");
        DomainException.Ensure(
            normalized.Length <= MaxLength,
            $"消息内容不能超过 {MaxLength} 字");
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
