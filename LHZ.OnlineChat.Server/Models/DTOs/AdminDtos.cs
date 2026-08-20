namespace LHZ.OnlineChat.Server.Models.DTOs;

/// <summary>
/// 管理后台 DTO
/// </summary>

public class AdminLoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AdminInfo
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    /// <summary>0=超级管理员 1=运营管理员</summary>
    public int Role { get; set; }
    /// <summary>0=停用 1=启用</summary>
    public int Status { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class AdminLoginResponse
{
    public string Token { get; set; } = string.Empty;
    public AdminInfo Admin { get; set; } = new();
}

public class AdminCreateRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int Role { get; set; } = 1;
}

public class AdminUpdateRequest
{
    public int? Role { get; set; }
    public int? Status { get; set; }
}

public class AdminChangePasswordRequest
{
    public string OldPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>管理后台用户列表项</summary>
public class AdminUserDto
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
    /// <summary>好友数</summary>
    public int FriendCount { get; set; }
    /// <summary>群数</summary>
    public int GroupCount { get; set; }
    /// <summary>消息总数（私聊+群聊）</summary>
    public long MessageCount { get; set; }
}

/// <summary>用户详情（含登录设备）</summary>
public class AdminUserDetailDto
{
    public AdminUserDto User { get; set; } = new();
    public List<SessionInfoDto> Sessions { get; set; } = new();
}

public class AdminBanRequest
{
    public bool Banned { get; set; }
    public string? Reason { get; set; }
}

public class AdminResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>仪表盘概览</summary>
public class DashboardOverviewDto
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
    /// <summary>近 24 小时消息分布（24 个点，按小时）</summary>
    public List<HourPointDto> MessageHourTrend { get; set; } = new();
    /// <summary>最活跃用户 TOP10（消息数）</summary>
    public List<TopUserDto> TopUsers { get; set; } = new();
    /// <summary>最活跃群 TOP10（群消息数）</summary>
    public List<TopGroupDto> TopGroups { get; set; } = new();
}

public class TrendPointDto
{
    public string Date { get; set; } = string.Empty;
    public long Count { get; set; }
}

public class HourPointDto
{
    public string Hour { get; set; } = string.Empty;
    public long Count { get; set; }
}

public class TopUserDto
{
    public int UserId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public long Count { get; set; }
}

public class TopGroupDto
{
    public long GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Count { get; set; }
}

/// <summary>审计日志项</summary>
public class AdminLogDto
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

// ==================== P1：群 / 消息 / 机器人管理 ====================

public class AdminGroupDto
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

public class AdminGroupMemberDto
{
    public int UserId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public int Role { get; set; }
    public bool IsOnline { get; set; }
    public bool IsBot { get; set; }
    public DateTime? MutedUntil { get; set; }
}

public class AdminGroupDetailDto
{
    public AdminGroupDto Group { get; set; } = new();
    public List<AdminGroupMemberDto> Members { get; set; } = new();
}

public class AdminMuteRequest
{
    /// <summary>禁言截止时间（UTC）；null 或过去时间 = 解除禁言</summary>
    public DateTime? MutedUntil { get; set; }
}

public class AdminTransferOwnerRequest
{
    public int NewOwnerId { get; set; }
}

public class AdminMessageDto
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

public class AdminRobotDto
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
