namespace LHZ.OnlineChat.Server.Models.DTOs;

/// <summary>
/// 群组相关 DTO
/// </summary>
public class CreateGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Avatar { get; set; }
}

public class GroupInfo
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public long OwnerId { get; set; }
    public int MemberCount { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 群公告（可为空）
    /// </summary>
    public string? Announcement { get; set; }

    public DateTime? AnnouncementAt { get; set; }

    /// <summary>
    /// 当前用户在该群中的角色：0=群主, 1=管理员, 2=成员
    /// </summary>
    public int MyRole { get; set; } = 2;
}

public class GroupMemberInfo
{
    public int UserId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public int Role { get; set; }
    public bool IsOnline { get; set; }
}

public class InviteMembersRequest
{
    /// <summary>
    /// 要邀请的好友账号 ID 列表（仅限邀请者自己的好友）
    /// </summary>
    public List<int> UserIds { get; set; } = new();
}

public class SetAnnouncementRequest
{
    /// <summary>
    /// 群公告内容（空字符串表示清除公告）
    /// </summary>
    public string Announcement { get; set; } = string.Empty;
}

public class SetAdminRequest
{
    /// <summary>
    /// 目标成员账号 ID
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// true=设为管理员, false=取消管理员
    /// </summary>
    public bool IsAdmin { get; set; }
}
