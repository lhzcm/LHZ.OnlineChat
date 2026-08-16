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

    [Column(IsNullable = false, StringLength = 200)]
    public string Email { get; set; } = string.Empty;

    [Column(IsNullable = false, StringLength = 200)]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// 昵称（可重复）
    /// </summary>
    [Column(IsNullable = false, StringLength = 50)]
    public string Nickname { get; set; } = string.Empty;

    [Column(StringLength = 500)]
    public string? Avatar { get; set; }

    [Column(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(IsNullable = false)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
