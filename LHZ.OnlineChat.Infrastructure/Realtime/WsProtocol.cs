using LHZ.FastJson.Json.Attributes;

namespace LHZ.OnlineChat.Infrastructure.Realtime;

/// <summary>
/// WebSocket 线上协议报文。
///
/// 这是基础设施层的「传输契约」，不是领域模型 ——
/// 改造前它放在 Models/DTOs 里并被业务服务直接构造，协议细节因此渗透到业务层。
/// 现在只有 RealtimeNotifier 和入站分发器碰它。
/// 键名由 [JsonProperty] 固定为 camelCase，与前端 JS 字段一致（LHZ.FastJson 双向匹配）。
/// </summary>
internal sealed class WsMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>发送者（用户 ID 字符串；部分通知里承载群 ID 等上下文）</summary>
    [JsonProperty("from")]
    public string From { get; set; } = string.Empty;

    /// <summary>接收者（用户 ID 或群 ID）</summary>
    [JsonProperty("to")]
    public string To { get; set; } = string.Empty;

    [JsonProperty("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>Unix 毫秒</summary>
    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("messageId")]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>0=文字 1=图片 2=文件</summary>
    [JsonProperty("messageType")]
    public int MessageType { get; set; }

    [JsonProperty("senderName")]
    public string SenderName { get; set; } = string.Empty;

    [JsonProperty("senderAvatar")]
    public string? SenderAvatar { get; set; }

    [JsonProperty("mentions")]
    public List<int> Mentions { get; set; } = new();

    [JsonProperty("replyTo")]
    public string ReplyTo { get; set; } = string.Empty;

    [JsonProperty("replyContent")]
    public string ReplyContent { get; set; } = string.Empty;

    [JsonProperty("replySender")]
    public string ReplySender { get; set; } = string.Empty;
}

/// <summary>WebSocket 消息类型常量（线上协议，不可随意更名）</summary>
internal static class WsMessageType
{
    public const string PrivateMessage = "private_message";
    public const string GroupMessage = "group_message";
    public const string Typing = "typing";
    public const string ReadReceipt = "read_receipt";
    public const string Heartbeat = "heartbeat";
    public const string Pong = "pong";
    public const string OnlineStatus = "online_status";
    public const string FriendRequest = "friend_request";
    public const string FriendAccepted = "friend_accepted";
    public const string FriendRejected = "friend_rejected";
    public const string GroupInvited = "group_invited";
    public const string MessageRecalled = "message_recalled";
    public const string Blocked = "blocked";

    /// <summary>该登录会话已被踢下线（随后连接关闭）</summary>
    public const string Kicked = "kicked";

    /// <summary>群消息被拒（禁言中），content 为原因</summary>
    public const string Muted = "muted";

    /// <summary>所在群被解散，to 为群 ID</summary>
    public const string GroupDissolved = "group_dissolved";
}
