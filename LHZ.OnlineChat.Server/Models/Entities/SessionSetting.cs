using FreeSql.DataAnnotations;

namespace LHZ.OnlineChat.Server.Models.Entities;

/// <summary>
/// 会话设置（置顶 / 免打扰），按 用户 × 会话 维度
/// </summary>
[Table(Name = "SessionSetting")]
public class SessionSetting
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    [Column(IsNullable = false)]
    public int UserId { get; set; }

    /// <summary>
    /// private | group
    /// </summary>
    [Column(IsNullable = false, StringLength = 10)]
    public string SessionType { get; set; } = string.Empty;

    [Column(IsNullable = false)]
    public long SessionId { get; set; }

    [Column(IsNullable = false)]
    public bool IsPinned { get; set; }

    [Column(IsNullable = false)]
    public bool Muted { get; set; }

    [Column(IsNullable = false)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
