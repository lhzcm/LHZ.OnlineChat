using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Messaging;

/// <summary>
/// 私聊消息聚合根。
/// Content 保持 string 而非值对象：管理后台与全局搜索要对它做 SQL 的 LIKE '%关键词%'
/// （走 pg_trgm GIN 索引），而值对象列无法出现在 FreeSql 的查询谓词中。
/// </summary>
public sealed class PrivateMessage : AggregateRoot<long>
{
    private PrivateMessage() { }

    public int SenderId { get; private set; }

    public int ReceiverId { get; private set; }

    /// <summary>客户端生成的消息 ID（乐观发送去重键）；为空时前端回退用数据库 ID</summary>
    public string? ClientMessageId { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public MessageKind Kind { get; private set; }

    public bool IsRead { get; private set; }

    /// <summary>已撤回（内容不再展示）</summary>
    public bool IsDeleted { get; private set; }

    // ===== 引用回复的三个持久化列（沿用既有表结构）=====

    public string? ReplyMessageId { get; private set; }

    public string? ReplyContent { get; private set; }

    public string? ReplySenderName { get; private set; }

    /// <summary>引用回复视图</summary>
    public MessageReply? Reply => MessageReply.Create(ReplyMessageId, ReplyContent, ReplySenderName);

    public DateTime SentAt { get; private set; }

    /// <summary>规范化后的发送时间（Kind 一定为 Utc，避免序列化丢 Z 后缀）</summary>
    public DateTime SentAtUtc => UtcTime.Normalize(SentAt);

    /// <summary>对外消息标识：优先客户端 ID，否则数据库 ID</summary>
    public string PublicMessageId
        => string.IsNullOrWhiteSpace(ClientMessageId)
            ? Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : ClientMessageId;

    public static PrivateMessage Send(
        int senderId,
        int receiverId,
        string content,
        MessageKind kind,
        string? clientMessageId,
        MessageReply? reply,
        DateTime now)
    {
        MessageContentRules.EnsureValid(content, kind);

        return new PrivateMessage
        {
            SenderId = senderId,
            ReceiverId = receiverId,
            Content = content ?? string.Empty,
            Kind = kind,
            ClientMessageId = string.IsNullOrWhiteSpace(clientMessageId) ? null : clientMessageId,
            IsRead = false,
            IsDeleted = false,
            ReplyMessageId = reply?.MessageId,
            ReplyContent = reply?.Preview,
            ReplySenderName = reply?.SenderName,
            SentAt = now
        };
    }

    /// <summary>标记已读：只有接收方能标记</summary>
    public void MarkAsRead(int operatorId)
    {
        DomainException.Ensure(ReceiverId == operatorId, "消息不存在或无权操作");
        IsRead = true;
    }

    /// <summary>该消息的 messageId 是否匹配给定标识（客户端 ID 或数据库 ID 都算）</summary>
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

    /// <summary>管理后台强制删除（不受时间窗与本人限制）</summary>
    public void ForceDelete() => IsDeleted = true;
}
