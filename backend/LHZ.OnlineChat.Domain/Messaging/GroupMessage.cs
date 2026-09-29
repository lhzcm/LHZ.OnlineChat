using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Messaging;

/// <summary>
/// 群聊消息聚合根。
/// 群消息没有 IsRead 字段，未读靠 GroupMember.LastReadMessageId 游标计算。
/// </summary>
public sealed class GroupMessage : AggregateRoot<long>
{
    private GroupMessage() { }

    public long GroupId { get; private set; }

    public int SenderId { get; private set; }

    /// <summary>客户端生成的消息 ID（乐观发送去重键）</summary>
    public string? ClientMessageId { get; private set; }

    /// <summary>被 @ 的成员账号 ID，逗号分隔存储</summary>
    public string? Mentions { get; private set; }

    /// <summary>@ 提及列表视图</summary>
    public MentionList MentionedUsers => MentionList.Parse(Mentions);

    public string Content { get; private set; } = string.Empty;

    public MessageKind Kind { get; private set; }

    /// <summary>已撤回</summary>
    public bool IsDeleted { get; private set; }

    public string? ReplyMessageId { get; private set; }

    public string? ReplyContent { get; private set; }

    public string? ReplySenderName { get; private set; }

    public MessageReply? Reply => MessageReply.Create(ReplyMessageId, ReplyContent, ReplySenderName);

    public DateTime SentAt { get; private set; }

    public DateTime SentAtUtc => UtcTime.Normalize(SentAt);

    public string PublicMessageId
        => string.IsNullOrWhiteSpace(ClientMessageId)
            ? Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : ClientMessageId;

    public static GroupMessage Send(
        long groupId,
        int senderId,
        string content,
        MessageKind kind,
        string? clientMessageId,
        MentionList mentions,
        MessageReply? reply,
        DateTime now)
        => new()
        {
            GroupId = groupId,
            SenderId = senderId,
            Content = content ?? string.Empty,
            Kind = kind,
            ClientMessageId = string.IsNullOrWhiteSpace(clientMessageId) ? null : clientMessageId,
            Mentions = mentions.ToStorage(),
            IsDeleted = false,
            ReplyMessageId = reply?.MessageId,
            ReplyContent = reply?.Preview,
            ReplySenderName = reply?.SenderName,
            SentAt = now
        };

    public bool HasPublicId(string messageId)
        => ClientMessageId == messageId
           || Id.ToString(System.Globalization.CultureInfo.InvariantCulture) == messageId;

    /// <summary>撤回：仅本人、仅 2 分钟内</summary>
    public void Recall(int operatorId, DateTime now)
    {
        DomainException.Ensure(SenderId == operatorId, "只能撤回自己发送的消息");
        DomainException.Ensure(!IsDeleted, "该消息已撤回");
        DomainException.Ensure(RecallPolicy.IsWithinWindow(SentAt, now), "超过可撤回时间");
        IsDeleted = true;
    }

    /// <summary>管理后台强制删除</summary>
    public void ForceDelete() => IsDeleted = true;
}

/// <summary>
/// 会话设置聚合根（置顶 / 免打扰），按 用户 × 会话 维度。
/// </summary>
public sealed class SessionSetting : AggregateRoot<long>
{
    private SessionSetting() { }

    public int UserId { get; private set; }

    /// <summary>"private" / "group"（沿用既有表结构的字符串存储）</summary>
    public string SessionType { get; private set; } = string.Empty;

    /// <summary>私聊为对方账号 ID，群聊为群 ID</summary>
    public long SessionId { get; private set; }

    public bool IsPinned { get; private set; }

    public bool Muted { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>会话类型视图</summary>
    public ChatSessionType Type => ChatSessionTypeNames.Parse(SessionType);

    public static SessionSetting Create(
        int userId, ChatSessionType type, long sessionId, bool isPinned, bool muted, DateTime now)
    {
        DomainException.Ensure(sessionId > 0, "无效的会话 ID");
        return new SessionSetting
        {
            UserId = userId,
            SessionType = type.ToStorage(),
            SessionId = sessionId,
            IsPinned = isPinned,
            Muted = muted,
            UpdatedAt = now
        };
    }

    /// <summary>局部更新：null 表示该项不变</summary>
    public void Update(bool? isPinned, bool? muted, DateTime now)
    {
        DomainException.Ensure(isPinned.HasValue || muted.HasValue, "没有需要更新的设置");
        if (isPinned.HasValue) IsPinned = isPinned.Value;
        if (muted.HasValue) Muted = muted.Value;
        UpdatedAt = now;
    }
}
