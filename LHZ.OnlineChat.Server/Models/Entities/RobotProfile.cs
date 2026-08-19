using FreeSql.DataAnnotations;

namespace LHZ.OnlineChat.Server.Models.Entities;

/// <summary>
/// 机器人配置实体（机器人账号 = User 表 IsBot=true 的行）
/// </summary>
[Table(Name = "RobotProfile")]
public class RobotProfile
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>
    /// 机器人账号 ID（对应 User.Id，IsBot=true）
    /// </summary>
    [Column(IsNullable = false)]
    public int UserId { get; set; }

    /// <summary>
    /// 创建者账号 ID
    /// </summary>
    [Column(IsNullable = false)]
    public int OwnerId { get; set; }

    /// <summary>
    /// 机器人显示名（与 User.Nickname 同步）
    /// </summary>
    [Column(IsNullable = false, StringLength = 50)]
    public string Name { get; set; } = string.Empty;

    [Column(StringLength = 500)]
    public string? Avatar { get; set; }

    /// <summary>
    /// Webhook 回调地址（收到消息时 POST 事件）
    /// </summary>
    [Column(IsNullable = false, StringLength = 500)]
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>
    /// Webhook 签名密钥（HMAC-SHA256(rawBody)，请求/响应双向验签）
    /// </summary>
    [Column(StringLength = 200)]
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// 同步回调超时（毫秒）
    /// </summary>
    [Column(IsNullable = false)]
    public int TimeoutMs { get; set; } = 10000;

    /// <summary>
    /// 对外令牌（加密 ID，创建时生成并持久化，稳定不变）
    /// </summary>
    [Column(StringLength = 100)]
    public string? Token { get; set; }

    [Column(IsNullable = false)]
    public bool Enabled { get; set; } = true;

    [Column(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
