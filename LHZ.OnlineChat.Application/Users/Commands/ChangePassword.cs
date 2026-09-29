using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Users.Commands;

/// <summary>
/// 修改密码（验证原密码）。
/// 改后所有会话失效 —— 但这件事不在这里做，而是由 UserPasswordChanged 的订阅方统一处理，
/// 于是「自助改密 / 忘记密码 / 管理员重置」三条链路自动获得一致行为。
/// </summary>
public sealed class ChangePasswordCommand : ICommand<ApiResponse>
{
    public int UserId { get; set; }

    public string OldPassword { get; set; } = string.Empty;

    public string NewPassword { get; set; } = string.Empty;
}

internal sealed class ChangePasswordHandler : IRequestHandler<ChangePasswordCommand, ApiResponse>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public ChangePasswordHandler(
        IUserRepository users, IPasswordHasher hasher, IDomainEventDispatcher events, IClock clock)
    {
        _users = users;
        _hasher = hasher;
        _events = events;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(ChangePasswordCommand command, CancellationToken ct)
    {
        PasswordHash.EnsureRawPasswordValid(command.NewPassword);

        var user = await _users.FindByIdAsync(command.UserId, ct).ConfigureAwait(false)
                   ?? throw new EntityNotFoundException("用户不存在");

        DomainException.Ensure(_hasher.Verify(command.OldPassword, user.PasswordHash), "原密码错误");

        user.SetPassword(_hasher.Hash(command.NewPassword), PasswordChangeReason.SelfService, _clock.UtcNow);
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);
        await _events.DispatchEventsOfAsync(user, ct).ConfigureAwait(false);

        return ApiResponse.Ok("密码修改成功，其他设备已下线，请重新登录");
    }
}

/// <summary>忘记密码：邮箱验证码重置，成功后该账号所有会话失效</summary>
public sealed class ForgotPasswordCommand : ICommand<ApiResponse>
{
    public string Email { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string NewPassword { get; set; } = string.Empty;
}

internal sealed class ForgotPasswordHandler : IRequestHandler<ForgotPasswordCommand, ApiResponse>
{
    private readonly IUserRepository _users;
    private readonly IVerificationCodeStore _codes;
    private readonly IPasswordHasher _hasher;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public ForgotPasswordHandler(
        IUserRepository users,
        IVerificationCodeStore codes,
        IPasswordHasher hasher,
        IDomainEventDispatcher events,
        IClock clock)
    {
        _users = users;
        _codes = codes;
        _hasher = hasher;
        _events = events;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(ForgotPasswordCommand command, CancellationToken ct)
    {
        var email = Email.Parse(command.Email);
        PasswordHash.EnsureRawPasswordValid(command.NewPassword);

        var user = await _users.FindByEmailAsync(email, ct).ConfigureAwait(false);
        DomainException.Ensure(user is not null, "该邮箱未注册");

        var codeOk = await _codes.ValidateAndConsumeAsync(email, command.Code, ct).ConfigureAwait(false);
        DomainException.Ensure(codeOk, "验证码错误或已过期");

        user!.SetPassword(_hasher.Hash(command.NewPassword), PasswordChangeReason.ForgotPassword, _clock.UtcNow);
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);
        await _events.DispatchEventsOfAsync(user, ct).ConfigureAwait(false);

        return ApiResponse.Ok("密码重置成功，请使用新密码登录");
    }
}
