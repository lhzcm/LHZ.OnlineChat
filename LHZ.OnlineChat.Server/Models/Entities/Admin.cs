using System;
using FreeSql.DataAnnotations;

namespace LHZ.OnlineChat.Server.Models.Entities;

/// <summary>
/// 管理员账号（独立于用户体系；User 是被管理对象，Admin 是管理者）
/// Role: 0=超级管理员（可管理管理员/审计） 1=运营管理员
/// Status: 0=停用 1=启用
/// </summary>
public class Admin
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int Role { get; set; } = 1;
    public int Status { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>
/// 管理操作审计日志
/// </summary>
public class AdminLog
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }
    public int AdminId { get; set; }
    public string AdminName { get; set; } = string.Empty;
    /// <summary>操作类型，如 user.ban / user.kick / user.reset_password / admin.create / group.delete</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>目标类型：user / group / message / robot / admin</summary>
    public string TargetType { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public string? Detail { get; set; }
    public string? Ip { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
