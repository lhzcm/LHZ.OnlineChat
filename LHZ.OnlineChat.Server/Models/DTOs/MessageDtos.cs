namespace LHZ.OnlineChat.Server.Models.DTOs;

/// <summary>
/// 消息相关 DTO
/// </summary>
public class MessageDto
{
    public long Id { get; set; }
    public long SenderId { get; set; }
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
    public long SenderId { get; set; }
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
