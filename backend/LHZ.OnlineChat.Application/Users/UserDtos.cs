namespace LHZ.OnlineChat.Application.Users;

/// <summary>
/// 用户基本信息。字段与改造前的 Models/DTOs/AuthDtos.UserInfo 完全一致，前端无需改动。
/// </summary>
public sealed class UserInfo
{
    public int Id { get; set; }

    public string Nickname { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    public string? Email { get; set; }
}

/// <summary>发送验证码的响应</summary>
public sealed class SendCodeResponse
{
    /// <summary>未配置 SMTP 的开发模式下回传验证码，便于本地调试；生产为 null</summary>
    public string? DevCode { get; set; }

    public int CooldownSeconds { get; set; } = 60;
}

/// <summary>注册成功响应</summary>
public sealed class RegisterResponse
{
    /// <summary>自动分配的账号 ID（登录凭据）</summary>
    public int AccountId { get; set; }
}

/// <summary>登录/刷新令牌响应</summary>
public sealed class LoginResponse
{
    public string Token { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public UserInfo User { get; set; } = new();
}

/// <summary>登录会话（设备）信息</summary>
public sealed class SessionInfoDto
{
    public string SessionId { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string Ip { get; set; } = string.Empty;

    public long CreatedAt { get; set; }

    public long LastActiveAt { get; set; }

    /// <summary>是否当前请求所在的设备</summary>
    public bool IsCurrent { get; set; }
}

/// <summary>头像上传响应</summary>
public sealed class AvatarResponse
{
    public string Avatar { get; set; } = string.Empty;
}

/// <summary>通用文件上传响应</summary>
public sealed class UploadResponse
{
    public string Url { get; set; } = string.Empty;
}

/// <summary>领域实体 → DTO 的映射（集中一处，避免各用例重复拼装）</summary>
public static class UserMapper
{
    public static UserInfo ToInfo(this Domain.Users.User user) => new()
    {
        Id = user.Id,
        Nickname = user.Nickname,
        Avatar = user.Avatar,
        Email = user.Email?.Value
    };

    public static SessionInfoDto ToDto(this Abstractions.LoginSession session, string currentSessionId) => new()
    {
        SessionId = session.SessionId,
        DeviceName = session.DeviceName,
        Ip = session.Ip,
        CreatedAt = session.CreatedAt,
        LastActiveAt = session.LastActiveAt,
        IsCurrent = session.SessionId == currentSessionId
    };
}
