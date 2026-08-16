namespace LHZ.OnlineChat.Server.Models.DTOs;

/// <summary>
/// WebSocket 消息协议
/// </summary>
public class WsMessage
{
    /// <summary>
    /// 消息类型: private_message, group_message, typing, read_receipt, heartbeat, online_status
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 发送者用户ID
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// 接收者ID（用户ID 或 群组ID）
    /// </summary>
    public string To { get; set; } = string.Empty;

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 消息时间戳（毫秒）
    /// </summary>
    public long Timestamp { get; set; }

    /// <summary>
    /// 消息唯一ID
    /// </summary>
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 消息内容类型: 0=文字, 1=图片, 2=文件
    /// </summary>
    public int MessageType { get; set; }

    /// <summary>
    /// 发送者昵称
    /// </summary>
    public string SenderName { get; set; } = string.Empty;

    /// <summary>
    /// 发送者头像
    /// </summary>
    public string? SenderAvatar { get; set; }
}

/// <summary>
/// WebSocket 消息类型常量
/// </summary>
public static class WsMessageType
{
    public const string PrivateMessage = "private_message";
    public const string GroupMessage = "group_message";
    public const string Typing = "typing";
    public const string ReadReceipt = "read_receipt";
    public const string Heartbeat = "heartbeat";
    public const string OnlineStatus = "online_status";
    public const string FriendRequest = "friend_request";
}
