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
}

internal sealed class AdminLoginHandler : IRequestHandler<AdminLoginCommand, ApiResponse<AdminLoginResponse>>
{
    private const string BadCredentials = "账号或密码错误";

    private readonly IAdminRepository _admins;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenIssuer _tokens;
    private readonly IClock _clock;

    public AdminLoginHandler(
        IAdminRepository admins, IPasswordHasher hasher, ITokenIssuer tokens, IClock clock)
    {
        _admins = admins;
        _hasher = hasher;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<ApiResponse<AdminLoginResponse>> Handle(AdminLoginCommand command, CancellationToken ct)
    {
        var username = (command.Username ?? string.Empty).Trim();
        DomainException.Ensure(
            username.Length > 0 && !string.IsNullOrWhiteSpace(command.Password),
            "请输入账号和密码");

        var admin = await _admins.FindByUsernameAsync(username, ct).ConfigureAwait(false);
        DomainException.Ensure(
            admin is not null && _hasher.Verify(command.Password, admin.PasswordHash),
            BadCredentials);

        admin!.EnsureCanLogin();

        admin.RecordLogin(_clock.UtcNow);
        await _admins.UpdateAsync(admin, ct).ConfigureAwait(false);

        return ApiResponse<AdminLoginResponse>.Ok(new AdminLoginResponse
        {
            Token = _tokens.IssueAdminToken(admin),
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

    public ChangeAdminPasswordHandler(IAdminRepository admins, IPasswordHasher hasher)
    {
        _admins = admins;
        _hasher = hasher;
    }

    public async Task<ApiResponse> Handle(ChangeAdminPasswordCommand command, CancellationToken ct)
    {
        PasswordHash.EnsureRawPasswordValid(command.NewPassword);

        var admin = await _admins.GetRequiredAsync(command.AdminId, ct).ConfigureAwait(false);
        DomainException.Ensure(_hasher.Verify(command.OldPassword, admin.PasswordHash), "原密码错误");

        admin.SetPassword(_hasher.Hash(command.NewPassword));
        await _admins.UpdateAsync(admin, ct).ConfigureAwait(false);

        return ApiResponse.Ok("密码修改成功");
    }
}
