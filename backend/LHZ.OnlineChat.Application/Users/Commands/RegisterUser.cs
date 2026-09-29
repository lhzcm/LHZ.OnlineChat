using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Users.Commands;

/// <summary>用户注册（昵称 + 邮箱验证码 + 密码），成功后自动分配账号 ID</summary>
public sealed class RegisterUserCommand : ICommand<ApiResponse<RegisterResponse>>
{
    public string Nickname { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    /// <summary>邮箱收到的 6 位验证码</summary>
    public string Code { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

internal sealed class RegisterUserHandler : IRequestHandler<RegisterUserCommand, ApiResponse<RegisterResponse>>
{
    private readonly IUserRepository _users;
    private readonly IVerificationCodeStore _codes;
    private readonly IPasswordHasher _hasher;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public RegisterUserHandler(
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

    public async Task<ApiResponse<RegisterResponse>> Handle(RegisterUserCommand command, CancellationToken ct)
    {
        // 校验规则全部落在值对象里（格式、长度、口令强度）
        var email = Email.Parse(command.Email);
        var nickname = NicknameRules.Normalize(command.Nickname);
        PasswordHash.EnsureRawPasswordValid(command.Password);

        var codeOk = await _codes.ValidateAndConsumeAsync(email, command.Code, ct).ConfigureAwait(false);
        DomainException.Ensure(codeOk, "验证码错误或已过期");

        var taken = await _users.EmailExistsAsync(email, ct: ct).ConfigureAwait(false);
        DomainException.Ensure(!taken, "该邮箱已注册");

        var now = _clock.UtcNow;
        var user = User.Register(nickname, email, _hasher.Hash(command.Password), now);

        await _users.AddAsync(user, ct).ConfigureAwait(false);

        // 自增主键已回填，此时才发注册事件（携带真实账号 ID）
        user.ConfirmRegistration(now);
        await _events.DispatchEventsOfAsync(user, ct).ConfigureAwait(false);

        return ApiResponse<RegisterResponse>.Ok(
            new RegisterResponse { AccountId = user.Id },
            $"注册成功，你的账号是 {user.Id}，请牢记");
    }
}
