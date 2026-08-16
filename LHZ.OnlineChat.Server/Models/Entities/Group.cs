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

    [Column(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
