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

    /// <summary>
    /// 客户端生成的消息 ID（乐观发送去重键）；为空时使用数据库 ID
    /// </summary>
    [Column(StringLength = 64)]
    public string? ClientMessageId { get; set; }

    [Column(IsNullable = false, StringLength = -2)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 0=文字, 1=图片, 2=文件
    /// </summary>
    [Column(IsNullable = false)]
    public int MessageType { get; set; }

    [Column(IsNullable = false)]
    public bool IsRead { get; set; }

    /// <summary>
    /// 是否已撤回（撤回后消息内容不再展示）
    /// </summary>
    [Column(IsNullable = false)]
    public bool IsDeleted { get; set; }

    /// <summary>
    /// 被引用消息的 messageId（客户端 ID 或数据库 ID）
    /// </summary>
    [Column(StringLength = 64)]
    public string? ReplyMessageId { get; set; }

    /// <summary>
    /// 被引用消息的原文预览
    /// </summary>
    [Column(StringLength = 200)]
    public string? ReplyContent { get; set; }

    /// <summary>
    /// 被引用消息的发送者昵称
    /// </summary>
    [Column(StringLength = 50)]
    public string? ReplySenderName { get; set; }

    [Column(IsNullable = false)]
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
