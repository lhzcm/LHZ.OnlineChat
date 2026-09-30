using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Admins.Commands;

/// <summary>管理员登录（独立于用户体系）</summary>
public sealed class AdminLoginCommand : ICommand<ApiResponse<AdminLoginResponse>>
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>请求来源 IP（由表现层填入，用于限流）</summary>
    public string? Ip { get; set; }
}

internal sealed class AdminLoginHandler : IRequestHandler<AdminLoginCommand, ApiResponse<AdminLoginResponse>>
{
    private const string BadCredentials = "账号或密码错误";

    /// <summary>限流的账号维度前缀：管理员与普通用户的命名空间必须分开，否则会互相影响</summary>
    private const string ThrottleScope = "admin:";

    private readonly IAdminRepository _admins;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenIssuer _tokens;
    private readonly IAdminSessionStore _sessions;
    private readonly ILoginThrottle _throttle;
    private readonly IClock _clock;

    public AdminLoginHandler(
        IAdminRepository admins,
        IPasswordHasher hasher,
        ITokenIssuer tokens,
        IAdminSessionStore sessions,
        ILoginThrottle throttle,
        IClock clock)
    {
        _admins = admins;
        _hasher = hasher;
        _tokens = tokens;
        _sessions = sessions;
        _throttle = throttle;
        _clock = clock;
    }

    public async Task<ApiResponse<AdminLoginResponse>> Handle(AdminLoginCommand command, CancellationToken ct)
    {
        var username = (command.Username ?? string.Empty).Trim();
        DomainException.Ensure(
            username.Length > 0 && !string.IsNullOrWhiteSpace(command.Password),
            "请输入账号和密码");

        // 管理后台是权限最高的入口，限流比普通用户登录更重要
        var throttleKey = ThrottleScope + username;
        var lockoutSeconds = await _throttle
            .GetLockoutSecondsAsync(throttleKey, command.Ip, ct)
            .ConfigureAwait(false);

        DomainException.Ensure(lockoutSeconds == 0, LoginThrottleMessages.Describe(lockoutSeconds));

        var admin = await _admins.FindByUsernameAsync(username, ct).ConfigureAwait(false);
        if (admin is null || !_hasher.Verify(command.Password, admin.PasswordHash))
        {
            await _throttle.RecordFailureAsync(throttleKey, command.Ip, ct).ConfigureAwait(false);
            throw new DomainException(BadCredentials);
        }

        admin.EnsureCanLogin();

        await _throttle.ResetAsync(throttleKey, command.Ip, ct).ConfigureAwait(false);

        admin.RecordLogin(_clock.UtcNow);
        await _admins.UpdateAsync(admin, ct).ConfigureAwait(false);

        // 会话登记后令牌才可被吊销（停用/删除/改密时 RevokeAllAsync）
        var sessionId = Guid.NewGuid().ToString("N");
        await _sessions.CreateAsync(admin.Id, sessionId, ct).ConfigureAwait(false);

        return ApiResponse<AdminLoginResponse>.Ok(new AdminLoginResponse
        {
            Token = _tokens.IssueAdminToken(admin, sessionId),
            Admin = admin.ToInfo()
        }, "登录成功");
    }
}

/// <summary>当前管理员信息</summary>
public sealed class GetAdminProfileQuery : IQuery<ApiResponse<AdminInfo>>
{
    public int AdminId { get; set; }
}

internal sealed class GetAdminProfileHandler : IRequestHandler<GetAdminProfileQuery, ApiResponse<AdminInfo>>
{
    private readonly IAdminRepository _admins;

    public GetAdminProfileHandler(IAdminRepository admins) => _admins = admins;

    public async Task<ApiResponse<AdminInfo>> Handle(GetAdminProfileQuery query, CancellationToken ct)
    {
        var admin = await _admins.GetRequiredAsync(query.AdminId, ct).ConfigureAwait(false);
        return ApiResponse<AdminInfo>.Ok(admin.ToInfo());
    }
}

/// <summary>管理员修改自己的密码</summary>
public sealed class ChangeAdminPasswordCommand : ICommand<ApiResponse>
{
    public int AdminId { get; set; }

    public string OldPassword { get; set; } = string.Empty;

    public string NewPassword { get; set; } = string.Empty;
}

internal sealed class ChangeAdminPasswordHandler : IRequestHandler<ChangeAdminPasswordCommand, ApiResponse>
{
    private readonly IAdminRepository _admins;
    private readonly IPasswordHasher _hasher;
    private readonly IAdminSessionStore _sessions;

    public ChangeAdminPasswordHandler(
        IAdminRepository admins, IPasswordHasher hasher, IAdminSessionStore sessions)
    {
        _admins = admins;
        _hasher = hasher;
        _sessions = sessions;
    }

    public async Task<ApiResponse> Handle(ChangeAdminPasswordCommand command, CancellationToken ct)
    {
        PasswordHash.EnsureRawPasswordValid(command.NewPassword);

        var admin = await _admins.GetRequiredAsync(command.AdminId, ct).ConfigureAwait(false);
        DomainException.Ensure(_hasher.Verify(command.OldPassword, admin.PasswordHash), "原密码错误");

        admin.SetPassword(_hasher.Hash(command.NewPassword));
        await _admins.UpdateAsync(admin, ct).ConfigureAwait(false);

        // 与用户改密一致：改完踢掉全部会话（含本次），迫使用新口令重新登录。
        // 改密的常见动机就是「怀疑口令泄露」，留着旧令牌可用等于没改
        await _sessions.RevokeAllAsync(command.AdminId, ct).ConfigureAwait(false);

        return ApiResponse.Ok("密码修改成功，请重新登录");
    }
}
