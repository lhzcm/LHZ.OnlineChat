using LHZ.FastJson.Json.Attributes;

namespace LHZ.OnlineChat.Server.Models.DTOs;

/// <summary>
/// WebSocket 消息协议
/// 属性通过 [JsonProperty] 固定为 camelCase 键名，与前端 JS 字段一致
/// （LHZ.FastJson 序列化与反序列化均按该键名匹配）
/// </summary>
public class WsMessage
{
    /// <summary>
    /// 消息类型: private_message, group_message, typing, read_receipt, heartbeat, online_status
    /// </summary>
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 发送者用户ID
    /// </summary>
    [JsonProperty("from")]
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// 接收者ID（用户ID 或 群组ID）
    /// </summary>
    [JsonProperty("to")]
    public string To { get; set; } = string.Empty;

    /// <summary>
    /// 消息内容
    /// </summary>
    [JsonProperty("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 消息时间戳（毫秒）
    /// </summary>
    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    /// <summary>
    /// 消息唯一ID
    /// </summary>
    [JsonProperty("messageId")]
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 消息内容类型: 0=文字, 1=图片, 2=文件
    /// </summary>
    [JsonProperty("messageType")]
    public int MessageType { get; set; }

    /// <summary>
    /// 发送者昵称
    /// </summary>
    [JsonProperty("senderName")]
    public string SenderName { get; set; } = string.Empty;

    /// <summary>
    /// 发送者头像
    /// </summary>
    [JsonProperty("senderAvatar")]
    public string? SenderAvatar { get; set; }

    /// <summary>
    /// 被 @ 的成员账号 ID 列表（群聊提及）
    /// </summary>
    [JsonProperty("mentions")]
    public List<int> Mentions { get; set; } = new();

    /// <summary>
    /// 被引用消息的 messageId（引用回复）
    /// </summary>
    [JsonProperty("replyTo")]
    public string ReplyTo { get; set; } = string.Empty;

    /// <summary>
    /// 被引用消息的原文预览
    /// </summary>
    [JsonProperty("replyContent")]
    public string ReplyContent { get; set; } = string.Empty;

    /// <summary>
    /// 被引用消息的发送者昵称
    /// </summary>
    [JsonProperty("replySender")]
    public string ReplySender { get; set; } = string.Empty;
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
    public const string FriendAccepted = "friend_accepted";
    public const string FriendRejected = "friend_rejected";
    public const string GroupInvited = "group_invited";
    public const string MessageRecalled = "message_recalled";
    public const string Blocked = "blocked";
    /// <summary>
    /// 服务端 → 客户端：该登录会话已被踢下线（随后连接被关闭）
    /// </summary>
    public const string Kicked = "kicked";
}
