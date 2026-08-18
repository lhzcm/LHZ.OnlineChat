using System.Text.Json;
using System.Text.Json.Serialization;

namespace LHZ.OnlineChat.Server.Models.DTOs;

/// <summary>
/// 认证相关 DTO
/// </summary>
public class SendCodeRequest
{
    public string Email { get; set; } = string.Empty;
}

public class SendCodeResponse
{
    /// <summary>
    /// 开发模式（未配置 SMTP）时返回验证码，便于本地调试；生产环境为 null
    /// </summary>
    public string? DevCode { get; set; }
    public int CooldownSeconds { get; set; } = 60;
}

public class RegisterRequest
{
    /// <summary>
    /// 昵称（可重复）
    /// </summary>
    public string Nickname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    /// <summary>
    /// 邮箱收到的 6 位数字验证码
    /// </summary>
    public string Code { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterResponse
{
    /// <summary>
    /// 注册成功自动分配的账号 ID（登录凭据）
    /// </summary>
    public int AccountId { get; set; }
}

public class LoginRequest
{
    /// <summary>
    /// 账号 ID 或邮箱（数字按账号 ID 查询，其余按邮箱查询）。
    /// 兼容数字输入（自动转字符串）。
    /// </summary>
    [JsonConverter(typeof(NumberToStringConverter))]
    public string Account { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// 允许 JSON 数字绑定到 string 属性（登录账号兼容传数字的旧调用）
/// </summary>
public class NumberToStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString("0");
        return reader.GetString() ?? string.Empty;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public UserInfo User { get; set; } = new();
}

public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class UserInfo
{
    public int Id { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// 修改昵称
/// </summary>
public class UpdateProfileRequest
{
    public string Nickname { get; set; } = string.Empty;
}

/// <summary>
/// 修改密码（需验证原密码）
/// </summary>
public class ChangePasswordRequest
{
    public string OldPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// 换绑邮箱（需新邮箱验证码，且新邮箱未被其他账号绑定）
/// </summary>
public class UpdateEmailRequest
{
    public string NewEmail { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public class AvatarResponse
{
    public string Avatar { get; set; } = string.Empty;
}

/// <summary>
/// 文件上传响应
/// </summary>
public class UploadResponse
{
    public string Url { get; set; } = string.Empty;
}

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "success")
        => new() { Success = true, Message = message, Data = data };

    public static ApiResponse<T> Fail(string message)
        => new() { Success = false, Message = message };
}

public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse Ok(string message = "success")
        => new() { Success = true, Message = message };

    public static new ApiResponse Fail(string message)
        => new() { Success = false, Message = message };
}
