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
