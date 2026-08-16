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
}

public class FriendRequestInfo
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public DateTime CreatedAt { get; set; }
}
