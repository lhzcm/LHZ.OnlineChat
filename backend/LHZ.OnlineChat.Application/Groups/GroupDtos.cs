namespace LHZ.OnlineChat.Application.Groups;

/// <summary>群组信息（字段与改造前一致）</summary>
public sealed class GroupInfo
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    public long OwnerId { get; set; }

    public int MemberCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? Announcement { get; set; }

    public DateTime? AnnouncementAt { get; set; }

    /// <summary>当前用户在该群的角色：0=群主 1=管理员 2=成员</summary>
    public int MyRole { get; set; } = 2;

    /// <summary>入群方式：0=仅限邀请（默认）1=开放加入</summary>
    public int JoinPolicy { get; set; }

    /// <summary>是否允许自行加入 —— 前端据此决定是否显示「加入群组」入口</summary>
    public bool IsOpenToJoin { get; set; }
}

/// <summary>群成员信息</summary>
public sealed class GroupMemberInfo
{
    public int UserId { get; set; }

    public string Nickname { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    public int Role { get; set; }

    public bool IsOnline { get; set; }

    public bool IsBot { get; set; }
}
/// <summary>群邀请链接（仅群主/管理员可见；token 是能力凭证，别写日志）</summary>
public sealed class GroupInviteLinkDto
{
    /// <summary>当前有效的 token；null = 未开启或已过期</summary>
    public string? Token { get; set; }

    public DateTime? ExpiresAt { get; set; }

    /// <summary>凭链接入群是否需要管理员审批</summary>
    public bool RequiresApproval { get; set; }
}

/// <summary>待审批的入群申请（管理端列表项）</summary>
public sealed class GroupJoinRequestInfo
{
    public long Id { get; set; }

    public int UserId { get; set; }

    public string Nickname { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    /// <summary>申请附言（可选）</summary>
    public string? Message { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>邀请页预览（凭 token，不暴露群是否存在以外的信息）</summary>
public sealed class GroupInvitePreview
{
    public long GroupId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    public int MemberCount { get; set; }

    /// <summary>凭该链接入群是否需要审批</summary>
    public bool RequiresApproval { get; set; }

    public bool AlreadyMember { get; set; }

    /// <summary>我已经提交过申请（等待审批）</summary>
    public bool HasPendingRequest { get; set; }
}

/// <summary>凭链接加入或申请的结果</summary>
public sealed class JoinGroupResult
{
    public long GroupId { get; set; }

    public string GroupName { get; set; } = string.Empty;

    /// <summary>true=已直接加入；false=已提交申请，等待管理员审批</summary>
    public bool Joined { get; set; }
}