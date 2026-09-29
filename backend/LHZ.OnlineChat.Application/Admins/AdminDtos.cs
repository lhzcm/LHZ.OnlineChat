using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Application.Admins;

/// <summary>管理员信息</summary>
public sealed class AdminInfo
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    /// <summary>0=超级管理员 1=运营管理员</summary>
    public int Role { get; set; }

    /// <summary>0=停用 1=启用</summary>
    public int Status { get; set; }

    public DateTime? LastLoginAt { get; set; }
}

public sealed class AdminLoginResponse
{
    public string Token { get; set; } = string.Empty;

    public AdminInfo Admin { get; set; } = new();
}

/// <summary>管理后台用户列表项</summary>
public sealed class AdminUserDto
{
    public int Id { get; set; }

    public string Nickname { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Avatar { get; set; }

    public bool IsBot { get; set; }

    public bool IsBanned { get; set; }

    public string? BanReason { get; set; }

    public DateTime? BannedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public bool IsOnline { get; set; }

    public int FriendCount { get; set; }

    public int GroupCount { get; set; }

    /// <summary>消息总数（私聊 + 群聊）</summary>
    public long MessageCount { get; set; }
}

/// <summary>用户详情（含登录设备）</summary>
public sealed class AdminUserDetailDto
{
    public AdminUserDto User { get; set; } = new();

    public List<Users.SessionInfoDto> Sessions { get; set; } = new();
}

/// <summary>仪表盘概览</summary>
public sealed class DashboardOverviewDto
{
    public int OnlineUsers { get; set; }

    public int WsConnections { get; set; }

    public int TotalUsers { get; set; }

    public int BannedUsers { get; set; }

    public int TotalGroups { get; set; }

    public int TotalRobots { get; set; }

    public long TotalMessages { get; set; }

    public long PrivateMessageTotal { get; set; }

    public long GroupMessageTotal { get; set; }

    public long TodayMessages { get; set; }

    public long TodayPrivateMessages { get; set; }

    public long TodayGroupMessages { get; set; }

    public int TodayRegistrations { get; set; }

    public int TodayNewGroups { get; set; }

    /// <summary>今日活跃用户（今日发过消息的去重用户数）</summary>
    public int TodayActiveUsers { get; set; }

    public List<TrendPointDto> RegisterTrend { get; set; } = new();

    public List<TrendPointDto> MessageTrend { get; set; } = new();

    /// <summary>近 24 小时消息分布</summary>
    public List<HourPointDto> MessageHourTrend { get; set; } = new();

    public List<TopUserDto> TopUsers { get; set; } = new();

    public List<TopGroupDto> TopGroups { get; set; } = new();
}

public sealed class TrendPointDto
{
    public string Date { get; set; } = string.Empty;

    public long Count { get; set; }
}

public sealed class HourPointDto
{
    public string Hour { get; set; } = string.Empty;

    public long Count { get; set; }
}

public sealed class TopUserDto
{
    public int UserId { get; set; }

    public string Nickname { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    public long Count { get; set; }
}

public sealed class TopGroupDto
{
    public long GroupId { get; set; }

    public string Name { get; set; } = string.Empty;

    public long Count { get; set; }
}

/// <summary>审计日志项</summary>
public sealed class AdminLogDto
{
    public long Id { get; set; }

    public string AdminName { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string TargetType { get; set; } = string.Empty;

    public string? TargetId { get; set; }

    public string? Detail { get; set; }

    public string? Ip { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class AdminGroupDto
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    public int OwnerId { get; set; }

    public string OwnerName { get; set; } = string.Empty;

    public int MemberCount { get; set; }

    public long MessageCount { get; set; }

    public string? Announcement { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class AdminGroupMemberDto
{
    public int UserId { get; set; }

    public string Nickname { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    public int Role { get; set; }

    public bool IsOnline { get; set; }

    public bool IsBot { get; set; }

    public DateTime? MutedUntil { get; set; }
}

public sealed class AdminGroupDetailDto
{
    public AdminGroupDto Group { get; set; } = new();

    public List<AdminGroupMemberDto> Members { get; set; } = new();
}

public sealed class AdminMessageDto
{
    public long Id { get; set; }

    public string MessageId { get; set; } = string.Empty;

    /// <summary>private / group</summary>
    public string Type { get; set; } = string.Empty;

    public int SenderId { get; set; }

    public string SenderName { get; set; } = string.Empty;

    public string? SenderAvatar { get; set; }

    public string Content { get; set; } = string.Empty;

    public int MessageType { get; set; }

    /// <summary>私聊=对方账号 ID；群聊=群 ID</summary>
    public long SessionId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime SentAt { get; set; }
}

public sealed class AdminRobotDto
{
    public long Id { get; set; }

    public int UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int OwnerId { get; set; }

    public string OwnerName { get; set; } = string.Empty;

    public string WebhookUrl { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public long PushCount { get; set; }

    public long CallbackFailCount { get; set; }

    public DateTime CreatedAt { get; set; }
}

public static class AdminMapper
{
    public static AdminInfo ToInfo(this Admin admin) => new()
    {
        Id = admin.Id,
        Username = admin.Username,
        Role = (int)admin.Role,
        Status = (int)admin.Status,
        LastLoginAt = UtcTime.Normalize(admin.LastLoginAt)
    };

    public static AdminLogDto ToDto(this AdminAuditLog log) => new()
    {
        Id = log.Id,
        AdminName = log.AdminName,
        Action = log.Action,
        TargetType = log.TargetType,
        TargetId = log.TargetId,
        Detail = log.Detail,
        Ip = log.Ip,
        CreatedAt = UtcTime.Normalize(log.CreatedAt)
    };
}
