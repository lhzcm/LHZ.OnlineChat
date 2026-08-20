using FreeSql.DataAnnotations;

namespace LHZ.OnlineChat.Server.Models.Entities;

/// <summary>
/// 用户实体
/// Id 即登录账号：int 自增，起始 10000（启动时迁移序列）
/// </summary>
[Table(Name = "User_")]
public class User
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 邮箱（机器人账号为空）
    /// </summary>
    [Column(IsNullable = true, StringLength = 200)]
    public string? Email { get; set; }

    /// <summary>
    /// 密码哈希（机器人账号为空，且禁止登录）
    /// </summary>
    [Column(IsNullable = true, StringLength = 200)]
    public string? PasswordHash { get; set; }

    /// <summary>
    /// 昵称（可重复）
    /// </summary>
    [Column(IsNullable = false, StringLength = 50)]
    public string Nickname { get; set; } = string.Empty;

    [Column(StringLength = 500)]
    public string? Avatar { get; set; }

    /// <summary>
    /// 是否机器人账号（不能登录；由 Webhook 驱动回复）
    /// </summary>
    [Column(IsNullable = false)]
    public bool IsBot { get; set; }

    /// <summary>
    /// 是否被封禁（管理后台操作；封禁后不能登录，会话立即失效）
    /// </summary>
    [Column(IsNullable = false)]
    public bool IsBanned { get; set; }

    /// <summary>
    /// 封禁原因
    /// </summary>
    [Column(IsNullable = true, StringLength = 500)]
    public string? BanReason { get; set; }

    [Column(IsNullable = true)]
    public DateTime? BannedAt { get; set; }

    [Column(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(IsNullable = false)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
