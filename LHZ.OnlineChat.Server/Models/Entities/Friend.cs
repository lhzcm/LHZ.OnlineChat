using FreeSql.DataAnnotations;

namespace LHZ.OnlineChat.Server.Models.Entities;

/// <summary>
/// 好友关系实体
/// Status: 0=待确认, 1=已接受, 2=已屏蔽
/// </summary>
[Table(Name = "Friend")]
public class Friend
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    [Column(IsNullable = false)]
    public long UserId { get; set; }

    [Column(IsNullable = false)]
    public long FriendId { get; set; }

    /// <summary>
    /// 0=待确认, 1=已接受, 2=已屏蔽
    /// </summary>
    [Column(IsNullable = false)]
    public int Status { get; set; }

    [Column(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
