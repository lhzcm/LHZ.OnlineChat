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
    private readonly IHostEnvironmentInfo _env;

    public SendVerificationCodeHandler(
        IUserRepository users,
        IVerificationCodeStore codes,
        IEmailSender email,
        IHostEnvironmentInfo env)
    {
        _users = users;
        _codes = codes;
        _email = email;
        _env = env;
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

        if (sent)
        {
            return ApiResponse<SendCodeResponse>.Ok(
                new SendCodeResponse { CooldownSeconds = CooldownSeconds }, "验证码已发送");
        }

        if (!_email.IsConfigured)
        {
            // 未配置 SMTP：验证码只落服务器日志，由运维取码完成注册/改密。
            //
            // 这里唯一必须守住的是「不回传验证码」：/send-code 与 /forgot-password 都是匿名接口，
            // 一旦把码放进响应体，任何人用受害者邮箱请求一次就能读到验证码并重置其密码 ——
            // 这正是修复前的账号接管路径。开发环境才回传，方便本地调试。
            //
            // 但绝不能删除验证码：运维正是靠日志里的这串码操作，
            // 删掉会让它永远校验不过，表现为「验证码错误或已过期」（这个 bug 真实发生过）。
            return ApiResponse<SendCodeResponse>.Ok(
                new SendCodeResponse
                {
                    DevCode = _env.IsDevelopment ? code : null,
                    CooldownSeconds = CooldownSeconds
                },
                _env.IsDevelopment
                    ? "验证码已生成（未配置邮件服务，见响应与服务器日志）"
                    : "验证码已生成，但当前未配置邮件服务，请联系管理员从服务器日志获取");
        }

        // 已配置 SMTP 却发送失败（凭据过期、额度耗尽、被反垃圾拦截……）：
        // 这一次确实没有送达渠道，作废验证码以释放冷却窗口，
        // 让用户能立刻重试，而不是白等 60 秒再拿到一个同样收不到的码。
        await _codes.RemoveAsync(email, ct).ConfigureAwait(false);
        throw new DomainException("验证码发送失败，请稍后重试");
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
