using FreeSql.DataAnnotations;

namespace LHZ.OnlineChat.Server.Models.Entities;

/// <summary>
/// 群成员实体
/// Role: 0=群主, 1=管理员, 2=成员
/// </summary>
[Table(Name = "GroupMember")]
public class GroupMember
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    [Column(IsNullable = false)]
    public long GroupId { get; set; }

    [Column(IsNullable = false)]
    public int UserId { get; set; }

    /// <summary>
    /// 0=群主, 1=管理员, 2=成员
    /// </summary>
    [Column(IsNullable = false)]
    public int Role { get; set; } = 2;

    /// <summary>
    /// 已读游标：该成员最后已读的群消息ID（0=从未同步过，跳过离线补发）
    /// </summary>
    [Column(IsNullable = false)]
    public long LastReadMessageId { get; set; }

    [Column(IsNullable = false)]
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
