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
    public long GroupId { get; set; }
    public List<long> UserIds { get; set; } = new();
}
