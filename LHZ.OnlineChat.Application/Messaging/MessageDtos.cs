using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Application.Messaging;

/// <summary>私聊消息（字段与改造前一致）</summary>
public sealed class MessageDto
{
    public long Id { get; set; }

    public int SenderId { get; set; }

    public string SenderName { get; set; } = string.Empty;

    public string? SenderAvatar { get; set; }

    public string Content { get; set; } = string.Empty;

    public int MessageType { get; set; }

    public bool IsRead { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime SentAt { get; set; }

    /// <summary>客户端消息 ID（前端去重用）</summary>
    public string? MessageId { get; set; }

    public string? ReplyTo { get; set; }

    public string? ReplyContent { get; set; }

    public string? ReplySender { get; set; }
}

/// <summary>群聊消息</summary>
public sealed class GroupMessageDto
{
    public long Id { get; set; }

    public long GroupId { get; set; }

    public int SenderId { get; set; }

    public string SenderName { get; set; } = string.Empty;

    public string? SenderAvatar { get; set; }

    public string Content { get; set; } = string.Empty;

    public int MessageType { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime SentAt { get; set; }

    public string? MessageId { get; set; }

    public string? ReplyTo { get; set; }

    public string? ReplyContent { get; set; }

    public string? ReplySender { get; set; }

    public List<int> Mentions { get; set; } = new();
}

/// <summary>会话列表项（私聊/群聊聚合）</summary>
public sealed class SessionDto
{
    /// <summary>private | group</summary>
    public string Type { get; set; } = string.Empty;

    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    public string LastMessage { get; set; } = string.Empty;

    public DateTime LastTime { get; set; }

    public int UnreadCount { get; set; }

    public bool IsPinned { get; set; }

    public bool Muted { get; set; }

    public bool IsBot { get; set; }
}

/// <summary>消息搜索结果（私聊 + 群聊聚合）</summary>
public sealed class MessageSearchResultDto
{
    /// <summary>private | group</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>私聊为对方账号 ID，群聊为群 ID</summary>
    public long SessionId { get; set; }

    public string SessionName { get; set; } = string.Empty;

    public string SenderName { get; set; } = string.Empty;

    public string? SenderAvatar { get; set; }

    public string Content { get; set; } = string.Empty;

    public int MessageType { get; set; }

    public string? MessageId { get; set; }

    public DateTime SentAt { get; set; }
}

/// <summary>未读汇总</summary>
public sealed class UnreadCountDto
{
    public int PrivateUnread { get; set; }
}

/// <summary>领域实体 → DTO 映射</summary>
public static class MessageMapper
{
    public static MessageDto ToDto(this PrivateMessage m, Domain.Users.User? sender) => new()
    {
        Id = m.Id,
        SenderId = m.SenderId,
        SenderName = sender?.Nickname ?? "未知",
        SenderAvatar = sender?.Avatar,
        Content = m.Content,
        MessageType = (int)m.Kind,
        IsRead = m.IsRead,
        IsDeleted = m.IsDeleted,
        MessageId = m.ClientMessageId,
        ReplyTo = m.ReplyMessageId,
        ReplyContent = m.ReplyContent,
        ReplySender = m.ReplySenderName,
        SentAt = m.SentAtUtc
    };

    public static GroupMessageDto ToDto(this GroupMessage m, Domain.Users.User? sender) => new()
    {
        Id = m.Id,
        GroupId = m.GroupId,
        SenderId = m.SenderId,
        SenderName = sender?.Nickname ?? "未知",
        SenderAvatar = sender?.Avatar,
        Content = m.Content,
        MessageType = (int)m.Kind,
        IsDeleted = m.IsDeleted,
        MessageId = m.ClientMessageId,
        ReplyTo = m.ReplyMessageId,
        ReplyContent = m.ReplyContent,
        ReplySender = m.ReplySenderName,
        Mentions = m.MentionedUsers.UserIds.ToList(),
        SentAt = m.SentAtUtc
    };

    /// <summary>缓存快照 → DTO（Redis 命中路径）</summary>
    public static MessageDto ToDto(this Abstractions.CachedMessage m) => new()
    {
        SenderId = m.SenderId,
        SenderName = m.SenderName,
        SenderAvatar = m.SenderAvatar,
        Content = m.Content,
        MessageType = (int)m.Kind,
        IsRead = false,
        MessageId = m.MessageId,
        SentAt = m.SentAt
    };
}
