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
    private readonly ILoginThrottle _throttle;

    public LoginHandler(
        IUserRepository users,
        IPasswordHasher hasher,
        ITokenIssuer tokens,
        ISessionStore sessions,
        ILoginThrottle throttle)
    {
        _users = users;
        _hasher = hasher;
        _tokens = tokens;
        _sessions = sessions;
        _throttle = throttle;
    }

    public async Task<ApiResponse<LoginResponse>> Handle(LoginCommand command, CancellationToken ct)
    {
        var account = (command.Account ?? string.Empty).Trim();
        DomainException.Ensure(
            account.Length > 0 && !string.IsNullOrWhiteSpace(command.Password),
            "请输入账号和密码");

        var lockoutSeconds = await _throttle
            .GetLockoutSecondsAsync(account, command.Ip, ct)
            .ConfigureAwait(false);

        DomainException.Ensure(lockoutSeconds == 0, LoginThrottleMessages.Describe(lockoutSeconds));

        var user = await FindByAccountAsync(account, ct).ConfigureAwait(false);

        // 口令校验放在账号状态校验之前。
        //
        // 反过来（先 EnsureCanLogin 再验密）会让「机器人账号不能登录」「账号已被封禁」
        // 这些提示语在密码错误时也照样返回 —— 任何人都能用任意密码探测账号状态，
        // 上面统一 BadCredentials 的设计就被抵消了。
        // 顺序调整后，只有已经知道正确口令的人才会看到具体状态。
        var passwordOk = user is not null && _hasher.Verify(command.Password, user.PasswordHash);
        if (!passwordOk)
        {
            await _throttle.RecordFailureAsync(account, command.Ip, ct).ConfigureAwait(false);
            throw new DomainException(BadCredentials);
        }

        // 机器人不可登录 / 封禁拦截（带原因）—— 规则在聚合根上
        user!.EnsureCanLogin();

        await _throttle.ResetAsync(account, command.Ip, ct).ConfigureAwait(false);

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
