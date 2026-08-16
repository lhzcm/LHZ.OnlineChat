using FreeSql.DataAnnotations;

namespace LHZ.OnlineChat.Server.Models.Entities;

/// <summary>
/// 单聊消息实体
/// MessageType: 0=文字, 1=图片, 2=文件
/// </summary>
[Table(Name = "PrivateMessage")]
public class PrivateMessage
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    [Column(IsNullable = false)]
    public int SenderId { get; set; }

    [Column(IsNullable = false)]
    public int ReceiverId { get; set; }

    [Column(IsNullable = false, StringLength = -2)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 0=文字, 1=图片, 2=文件
    /// </summary>
    [Column(IsNullable = false)]
    public int MessageType { get; set; }

    [Column(IsNullable = false)]
    public bool IsRead { get; set; }

    [Column(IsNullable = false)]
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
