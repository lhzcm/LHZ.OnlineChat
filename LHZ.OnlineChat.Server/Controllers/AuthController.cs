using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Users.Commands;
using LHZ.OnlineChat.Application.Users.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LHZ.OnlineChat.Server.Controllers;

/// <summary>用户认证与个人信息</summary>
[Route("api/[controller]")]
public sealed class AuthController : ApiControllerBase
{
    /// <summary>发送邮箱验证码（6 位数字）</summary>
    [HttpPost("send-code")]
    public Task<IActionResult> SendCode([FromBody] SendVerificationCodeCommand command, CancellationToken ct)
        => Send(command, ct);

    /// <summary>用户注册</summary>
    [HttpPost("register")]
    public Task<IActionResult> Register([FromBody] RegisterUserCommand command, CancellationToken ct)
        => Send(command, ct);

    /// <summary>用户登录（账号 ID 或邮箱）</summary>
    [HttpPost("login")]
    public Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken ct)
    {
        // IP 由服务端从连接解析，不信任客户端传值
        command.Ip = CurrentUser.ClientIp;
        return Send(command, ct);
    }

    /// <summary>刷新访问令牌</summary>
    [HttpPost("refresh")]
    public Task<IActionResult> RefreshToken([FromBody] RefreshTokenCommand command, CancellationToken ct)
        => Send(command, ct);

    /// <summary>忘记密码（邮箱验证码重置）</summary>
    [HttpPost("forgot-password")]
    public Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordCommand command, CancellationToken ct)
        => Send(command, ct);

    /// <summary>当前用户信息</summary>
    [HttpGet("me")]
    [Authorize]
    public Task<IActionResult> GetCurrentUser(CancellationToken ct)
        => Send(new GetCurrentUserQuery { UserId = UserId }, ct);

    /// <summary>当前账号的全部登录设备</summary>
    [HttpGet("sessions")]
    [Authorize]
    public Task<IActionResult> GetSessions(CancellationToken ct)
        => Send(new GetLoginSessionsQuery { UserId = UserId, CurrentSessionId = SessionId }, ct);

    /// <summary>踢下线指定设备</summary>
    [HttpDelete("sessions/{sessionId}")]
    [Authorize]
    public Task<IActionResult> KickSession(string sessionId, CancellationToken ct)
        => Send(new KickSessionCommand { UserId = UserId, SessionId = sessionId }, ct);

    /// <summary>退出其他所有设备</summary>
    [HttpPost("sessions/logout-others")]
    [Authorize]
    public Task<IActionResult> LogoutOtherSessions(CancellationToken ct)
        => Send(new LogoutOtherSessionsCommand { UserId = UserId, CurrentSessionId = SessionId }, ct);

    /// <summary>修改密码（验证原密码）</summary>
    [HttpPut("password")]
    [Authorize]
    public Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand command, CancellationToken ct)
    {
        command.UserId = UserId;
        return Send(command, ct);
    }

    /// <summary>修改昵称</summary>
    [HttpPut("profile")]
    [Authorize]
    public Task<IActionResult> UpdateProfile([FromBody] UpdateNicknameCommand command, CancellationToken ct)
    {
        command.UserId = UserId;
        return Send(command, ct);
    }

    /// <summary>换绑邮箱（需新邮箱验证码）</summary>
    [HttpPut("email")]
    [Authorize]
    public Task<IActionResult> UpdateEmail([FromBody] ChangeEmailCommand command, CancellationToken ct)
    {
        command.UserId = UserId;
        return Send(command, ct);
    }

    /// <summary>上传头像</summary>
    [HttpPost("avatar")]
    [Authorize]
    public async Task<IActionResult> UploadAvatar(IFormFile? file, CancellationToken ct)
    {
        await using var upload = file.ToFileUpload();
        return await Send(new UploadAvatarCommand { UserId = UserId, File = upload.Value }, ct);
    }
}

/// <summary>
/// IFormFile → 应用层的 FileUpload。
/// 应用层不引用 ASP.NET 类型，所以在表现层做这层转换；
/// 流的生命周期由 using 管到用例执行结束。
/// </summary>
internal static class FormFileExtensions
{
    internal static FileUploadScope ToFileUpload(this IFormFile? file)
        => file is null || file.Length == 0
            ? new FileUploadScope(null, null)
            : new FileUploadScope(
                new FileUpload
                {
                    FileName = file.FileName,
                    Length = file.Length,
                    Content = file.OpenReadStream()
                },
                null);
}

/// <summary>承载 FileUpload 并负责释放底层流</summary>
internal sealed class FileUploadScope : IAsyncDisposable
{
    private readonly Stream? _stream;

    internal FileUploadScope(FileUpload? value, Stream? stream)
    {
        Value = value;
        _stream = stream ?? value?.Content;
    }

    internal FileUpload? Value { get; }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null) await _stream.DisposeAsync();
    }
}
