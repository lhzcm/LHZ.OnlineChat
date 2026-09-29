using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Users.Commands;

/// <summary>
/// 登录（账号 ID 或邮箱 + 密码）。
/// 每台设备一个独立会话（sid），可单独管理/踢下线。
/// </summary>
public sealed class LoginCommand : ICommand<ApiResponse<LoginResponse>>
{
    /// <summary>账号 ID 或邮箱</summary>
    public string Account { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>设备名（多端登录管理展示用）</summary>
    public string? DeviceName { get; set; }

    /// <summary>请求来源 IP（由表现层填入）</summary>
    public string? Ip { get; set; }
}

internal sealed class LoginHandler : IRequestHandler<LoginCommand, ApiResponse<LoginResponse>>
{
    private const string UnknownDevice = "未知设备";

    /// <summary>账号或密码错误统一用同一句提示，避免账号枚举</summary>
    private const string BadCredentials = "账号或密码错误";

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenIssuer _tokens;
    private readonly ISessionStore _sessions;

    public LoginHandler(
        IUserRepository users,
        IPasswordHasher hasher,
        ITokenIssuer tokens,
        ISessionStore sessions)
    {
        _users = users;
        _hasher = hasher;
        _tokens = tokens;
        _sessions = sessions;
    }

    public async Task<ApiResponse<LoginResponse>> Handle(LoginCommand command, CancellationToken ct)
    {
        var account = (command.Account ?? string.Empty).Trim();
        DomainException.Ensure(
            account.Length > 0 && !string.IsNullOrWhiteSpace(command.Password),
            "请输入账号和密码");

        var user = await FindByAccountAsync(account, ct).ConfigureAwait(false);
        DomainException.Ensure(user is not null, BadCredentials);

        // 机器人不可登录 / 封禁拦截（带原因）—— 规则在聚合根上
        user!.EnsureCanLogin();

        DomainException.Ensure(_hasher.Verify(command.Password, user.PasswordHash), BadCredentials);

        var sessionId = Guid.NewGuid().ToString("N");
        var deviceName = string.IsNullOrWhiteSpace(command.DeviceName) ? UnknownDevice : command.DeviceName.Trim();
        await _sessions.CreateAsync(user.Id, sessionId, deviceName, command.Ip, ct).ConfigureAwait(false);

        var token = _tokens.IssueUserToken(user, sessionId);
        var refreshToken = _tokens.GenerateRefreshToken();
        await _sessions.StoreRefreshTokenAsync(user.Id, sessionId, refreshToken, ct).ConfigureAwait(false);

        return ApiResponse<LoginResponse>.Ok(new LoginResponse
        {
            Token = token,
            RefreshToken = refreshToken,
            User = user.ToInfo()
        });
    }

    /// <summary>纯数字按账号 ID 查，否则按邮箱查</summary>
    private async Task<User?> FindByAccountAsync(string account, CancellationToken ct)
    {
        if (int.TryParse(account, System.Globalization.CultureInfo.InvariantCulture, out var accountId))
            return await _users.FindByIdAsync(accountId, ct).ConfigureAwait(false);

        var email = Email.TryParse(account);
        return email is null ? null : await _users.FindByEmailAsync(email, ct).ConfigureAwait(false);
    }
}
