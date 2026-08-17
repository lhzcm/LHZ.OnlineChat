using FreeSql.DataAnnotations;

namespace LHZ.OnlineChat.Server.Models.Entities;

/// <summary>
/// 好友设置（备注、分类标签）
/// 以设置者视角存储，独立于好友关系记录的方向：
/// UserId = 设置者，FriendId = 被设置的好友
/// </summary>
[Table(Name = "FriendTag")]
public class FriendTag
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>
    /// 设置者账号 ID
    /// </summary>
    [Column(IsNullable = false)]
    public int UserId { get; set; }

    /// <summary>
    /// 被设置的好友账号 ID
    /// </summary>
    [Column(IsNullable = false)]
    public int FriendId { get; set; }

    /// <summary>
    /// 好友备注（可空，空表示使用对方昵称）
    /// </summary>
    [Column(StringLength = 50)]
    public string? Remark { get; set; }

    /// <summary>
    /// 分类标签（可空，空表示未分组）
    /// </summary>
    [Column(StringLength = 30)]
    public string? Category { get; set; }

    [Column(IsNullable = false)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
