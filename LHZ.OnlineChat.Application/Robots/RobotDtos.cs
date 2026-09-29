using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Robots;

namespace LHZ.OnlineChat.Application.Robots;

/// <summary>机器人信息（字段与改造前一致）</summary>
public sealed class RobotInfo
{
    public long Id { get; set; }

    public int UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    public string WebhookUrl { get; set; } = string.Empty;

    public string? WebhookSecret { get; set; }

    public int TimeoutMs { get; set; }

    public bool Enabled { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>对外令牌（加密 ID）：第三方用 /api/robots/{Token}/reply 推送</summary>
    public string Token { get; set; } = string.Empty;
}

/// <summary>测试触发结果</summary>
public sealed class RobotTestResult
{
    public bool Success { get; set; }

    public string? Reply { get; set; }

    public string Message { get; set; } = string.Empty;
}

/// <summary>第三方主动推送的请求体（camelCase，由基础设施反序列化后传入）</summary>
public sealed class RobotReplyPayload
{
    /// <summary>private | group</summary>
    public string SessionType { get; set; } = "private";

    /// <summary>私聊为对方账号 ID，群聊为群 ID</summary>
    public long SessionId { get; set; }

    public string Content { get; set; } = string.Empty;

    public string? ReplyTo { get; set; }
}

public static class RobotMapper
{
    public static RobotInfo ToInfo(this Robot robot) => new()
    {
        Id = robot.Id,
        UserId = robot.UserId,
        Name = robot.Name,
        Avatar = robot.Avatar,
        WebhookUrl = robot.WebhookUrlValue,
        WebhookSecret = robot.WebhookSecret,
        TimeoutMs = robot.TimeoutMs,
        Enabled = robot.Enabled,
        CreatedAt = UtcTime.Normalize(robot.CreatedAt),
        Token = robot.Token ?? string.Empty
    };
}
