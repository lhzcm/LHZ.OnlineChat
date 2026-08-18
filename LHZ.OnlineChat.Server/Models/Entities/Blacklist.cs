using FreeSql.DataAnnotations;

namespace LHZ.OnlineChat.Server.Models.Entities;

/// <summary>
/// 黑名单实体（拉黑关系）
/// </summary>
[Table(Name = "Blacklist")]
public class Blacklist
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>
    /// 拉黑者账号 ID
    /// </summary>
    [Column(IsNullable = false)]
    public int UserId { get; set; }

    /// <summary>
    /// 被拉黑者账号 ID
    /// </summary>
    [Column(IsNullable = false)]
    public int BlockedUserId { get; set; }

    [Column(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
