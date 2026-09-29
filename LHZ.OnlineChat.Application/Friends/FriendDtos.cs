namespace LHZ.OnlineChat.Application.Friends;

/// <summary>好友信息（字段与改造前一致）</summary>
public sealed class FriendInfo
{
    public int UserId { get; set; }

    public string Nickname { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    public bool IsOnline { get; set; }

    /// <summary>好友关系状态（已接受恒为 1，保留字段以兼容前端）</summary>
    public int Status { get; set; }

    public bool IsBot { get; set; }

    /// <summary>我给他设的备注（空表示显示对方昵称）</summary>
    public string? Remark { get; set; }

    /// <summary>我给他设的分类（空表示未分组）</summary>
    public string? Category { get; set; }
}

/// <summary>待处理的好友申请</summary>
public sealed class FriendRequestInfo
{
    public long Id { get; set; }

    public int UserId { get; set; }

    public string Nickname { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>黑名单条目</summary>
public sealed class BlacklistUserDto
{
    public int UserId { get; set; }

    public string Nickname { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    public DateTime BlockedAt { get; set; }
}
