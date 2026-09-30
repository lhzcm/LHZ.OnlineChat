using System.Globalization;
using System.Security.Cryptography;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Users.Commands;

/// <summary>
/// 发送邮箱验证码（6 位数字，5 分钟有效，60 秒冷却）。
/// Purpose=forgot 时要求邮箱已注册，避免向任意邮箱发码。
/// </summary>
public sealed class SendVerificationCodeCommand : ICommand<ApiResponse<SendCodeResponse>>
{
    public string Email { get; set; } = string.Empty;

    /// <summary>register（默认）/ forgot</summary>
    public string Purpose { get; set; } = VerificationPurposes.Register;
}

/// <summary>验证码用途</summary>
public static class VerificationPurposes
{
    public const string Register = "register";
    public const string Forgot = "forgot";
}

internal sealed class SendVerificationCodeHandler
    : IRequestHandler<SendVerificationCodeCommand, ApiResponse<SendCodeResponse>>
{
    private static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(5);
    private const int CooldownSeconds = 60;

    private readonly IUserRepository _users;
    private readonly IVerificationCodeStore _codes;
    private readonly IEmailSender _email;

    public SendVerificationCodeHandler(
        IUserRepository users, IVerificationCodeStore codes, IEmailSender email)
    {
        _users = users;
        _codes = codes;
        _email = email;
    }

    public async Task<ApiResponse<SendCodeResponse>> Handle(
        SendVerificationCodeCommand command, CancellationToken ct)
    {
        var email = Email.Parse(command.Email);

        // 忘记密码：邮箱必须已注册
        if (command.Purpose == VerificationPurposes.Forgot)
        {
            var registered = await _users.EmailExistsAsync(email, ct: ct).ConfigureAwait(false);
            DomainException.Ensure(registered, "该邮箱未注册");
        }

        // 冷却：已有未过期验证码则拒绝重复发送
        var pending = await _codes.HasPendingCodeAsync(email, ct).ConfigureAwait(false);
        DomainException.Ensure(!pending, "验证码已发送，请稍后再试");

        var code = GenerateCode();
        await _codes.SaveAsync(email, code, CodeTtl, ct).ConfigureAwait(false);

        var sent = await _email.SendVerificationCodeAsync(email, code, ct).ConfigureAwait(false);

        return ApiResponse<SendCodeResponse>.Ok(new SendCodeResponse
        {
            // 未真正发出（未配置 SMTP）时回传验证码，保持原有本地调试体验
            DevCode = sent ? null : code,
            CooldownSeconds = CooldownSeconds
        }, "验证码已发送");
    }

    /// <summary>
    /// 6 位数字验证码，用密码学安全随机源。
    ///
    /// 不能用 Random.Shared：它是 xoshiro256**，攻击者只要向自己的邮箱多要几次码
    /// 就能观测到足够输出来还原内部状态，进而预测别人的验证码 ——
    /// 而验证码是「忘记密码」链路上唯一的凭据。
    /// </summary>
    private static string GenerateCode()
        => RandomNumberGenerator
            .GetInt32(100000, 1000000)
            .ToString(CultureInfo.InvariantCulture);
}
