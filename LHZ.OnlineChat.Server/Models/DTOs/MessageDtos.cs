namespace LHZ.OnlineChat.Server.Models.DTOs;

/// <summary>
/// 消息相关 DTO
/// </summary>
public class MessageDto
{
    public long Id { get; set; }
    public int SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string? SenderAvatar { get; set; }
    public string Content { get; set; } = string.Empty;
    public int MessageType { get; set; }
    public bool IsRead { get; set; }
    public DateTime SentAt { get; set; }
}

public class GroupMessageDto
{
    public long Id { get; set; }
    public long GroupId { get; set; }
    public int SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string? SenderAvatar { get; set; }
    public string Content { get; set; } = string.Empty;
    public int MessageType { get; set; }
    public DateTime SentAt { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>
/// 会话列表项（私聊/群聊聚合）
/// </summary>
public class SessionDto
{
    /// <summary>
    /// private | group
    /// </summary>
    public string Type { get; set; } = string.Empty;
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public string LastMessage { get; set; } = string.Empty;
    public DateTime LastTime { get; set; }
    public int UnreadCount { get; set; }
}
