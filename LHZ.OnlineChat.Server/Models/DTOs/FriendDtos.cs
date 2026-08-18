namespace LHZ.OnlineChat.Server.Models.DTOs;

/// <summary>
/// 好友相关 DTO
/// </summary>
public class AddFriendRequest
{
    /// <summary>
    /// 要添加的好友的账号 ID
    /// </summary>
    public int AccountId { get; set; }
}

public class FriendRequestResponse
{
    public long RequestId { get; set; }
    public bool Accept { get; set; }
}

public class FriendInfo
{
    public int UserId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public bool IsOnline { get; set; }
    public int Status { get; set; } // 好友关系状态
    /// <summary>
    /// 是否机器人账号
    /// </summary>
    public bool IsBot { get; set; }
    /// <summary>
    /// 我给他设置的备注（可空，空表示显示对方昵称）
    /// </summary>
    public string? Remark { get; set; }
    /// <summary>
    /// 我给他设置的分类标签（可空，空表示未分组）
    /// </summary>
    public string? Category { get; set; }
}

public class SetFriendRemarkRequest
{
    /// <summary>
    /// 备注名；空字符串表示清除备注
    /// </summary>
    public string? Remark { get; set; }
}

public class SetFriendCategoryRequest
{
    /// <summary>
    /// 分类标签；空字符串表示清除分类（未分组）
    /// </summary>
    public string? Category { get; set; }
}

public class FriendRequestInfo
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 黑名单用户信息
/// </summary>
public class BlacklistUserDto
{
    public int UserId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public DateTime BlockedAt { get; set; }
}
