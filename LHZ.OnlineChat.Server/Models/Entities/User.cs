using FreeSql.DataAnnotations;

namespace LHZ.OnlineChat.Server.Models.Entities;

/// <summary>
/// 用户实体
/// </summary>
[Table(Name = "User_")]
public class User
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    [Column(IsNullable = false, StringLength = 50)]
    public string Username { get; set; } = string.Empty;

    [Column(IsNullable = false, StringLength = 200)]
    public string PasswordHash { get; set; } = string.Empty;

    [Column(IsNullable = false, StringLength = 50)]
    public string Nickname { get; set; } = string.Empty;

    [Column(StringLength = 500)]
    public string? Avatar { get; set; }

    [Column(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(IsNullable = false)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
