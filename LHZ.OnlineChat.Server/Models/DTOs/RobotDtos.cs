using LHZ.FastJson.Json.Attributes;

namespace LHZ.OnlineChat.Server.Models.DTOs;

/// <summary>
/// 创建机器人请求
/// </summary>
public class CreateRobotRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public string WebhookUrl { get; set; } = string.Empty;
    public string? WebhookSecret { get; set; }
    public int? TimeoutMs { get; set; }
}

/// <summary>
/// 更新机器人请求（字段为空表示不修改）
/// </summary>
public class UpdateRobotRequest
{
    public string? Name { get; set; }
    public string? Avatar { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookSecret { get; set; }
    public int? TimeoutMs { get; set; }
    public bool? Enabled { get; set; }
}

/// <summary>
/// 机器人信息（响应）
/// </summary>
public class RobotInfo
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public string WebhookUrl { get; set; } = string.Empty;
    public string? WebhookSecret { get; set; }
    public int TimeoutMs { get; set; }
    public bool Enabled { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 测试触发机器人请求
/// </summary>
public class TestRobotRequest
{
    /// <summary>
    /// 模拟用户发送的消息内容
    /// </summary>
    public string Content { get; set; } = "你好";
}

/// <summary>
/// 异步回复请求（由 Webhook 服务方调用，需携带签名）
/// </summary>
public class BotReplyRequest
{
    /// <summary>
    /// 会话类型: private | group
    /// </summary>
    [JsonProperty("sessionType")]
    public string SessionType { get; set; } = "private";

    /// <summary>
    /// 会话 ID：私聊为对方账号 ID，群聊为群 ID
    /// </summary>
    [JsonProperty("sessionId")]
    public long SessionId { get; set; }

    [JsonProperty("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 被引用消息的 messageId（可选）
    /// </summary>
    [JsonProperty("replyTo")]
    public string? ReplyTo { get; set; }
}

/// <summary>
/// 机器人测试结果
/// </summary>
public class RobotTestResult
{
    public bool Success { get; set; }
    public string? Reply { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 群机器人操作请求
/// </summary>
public class AddGroupRobotRequest
{
    /// <summary>
    /// 机器人账号 ID（User.Id，IsBot=true）
    /// </summary>
    public int UserId { get; set; }
}

// ==================== Webhook 事件协议（出站，POST 到 WebhookUrl） ====================

/// <summary>
/// Webhook 事件：消息触发
/// </summary>
public class BotWebhookEvent
{
    [JsonProperty("event")]
    public string Event { get; set; } = "message";

    [JsonProperty("robot")]
    public BotWebhookActor Robot { get; set; } = new();

    /// <summary>
    /// 会话信息
    /// </summary>
    [JsonProperty("session")]
    public BotWebhookSession Session { get; set; } = new();

    [JsonProperty("from")]
    public BotWebhookActor From { get; set; } = new();

    [JsonProperty("message")]
    public BotWebhookMessage Message { get; set; } = new();

    /// <summary>
    /// 被 @ 的成员账号 ID 列表（群聊）
    /// </summary>
    [JsonProperty("mentions")]
    public List<int> Mentions { get; set; } = new();

    /// <summary>
    /// 被引用消息（回复引用）
    /// </summary>
    [JsonProperty("replyTo")]
    public BotWebhookMessage? ReplyTo { get; set; }
}

public class BotWebhookActor
{
    [JsonProperty("userId")]
    public int UserId { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("avatar")]
    public string? Avatar { get; set; }

    [JsonProperty("isBot")]
    public bool IsBot { get; set; }
}

public class BotWebhookSession
{
    /// <summary>
    /// private | group
    /// </summary>
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("id")]
    public long Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
}

public class BotWebhookMessage
{
    [JsonProperty("messageId")]
    public string MessageId { get; set; } = string.Empty;

    [JsonProperty("content")]
    public string Content { get; set; } = string.Empty;

    [JsonProperty("messageType")]
    public int MessageType { get; set; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }
}

/// <summary>
/// Webhook 同步响应：200 + {"content":"回复文本"}
/// </summary>
public class BotWebhookResponse
{
    [JsonProperty("content")]
    public string? Content { get; set; }
}
