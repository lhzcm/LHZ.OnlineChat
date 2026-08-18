using FreeSql.DataAnnotations;

namespace LHZ.OnlineChat.Server.Models.Entities;

/// <summary>
/// 群组实体
/// </summary>
[Table(Name = "Group_")]
public class Group_
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    [Column(IsNullable = false, StringLength = 100)]
    public string Name { get; set; } = string.Empty;

    [Column(StringLength = 500)]
    public string? Avatar { get; set; }

    [Column(IsNullable = false)]
    public int OwnerId { get; set; }

    /// <summary>
    /// 群公告（仅群主/管理员可编辑）
    /// </summary>
    [Column(StringLength = 2000)]
    public string? Announcement { get; set; }

    public DateTime? AnnouncementAt { get; set; }

    /// <summary>
    /// 最后编辑公告的成员 ID
    /// </summary>
    public int? AnnouncementBy { get; set; }

    [Column(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
