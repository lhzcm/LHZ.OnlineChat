using System.Net;
using System.Net.Mail;
using LHZ.OnlineChat.Server.Config;

namespace LHZ.OnlineChat.Server.Services;

/// <summary>
/// 邮件发送服务。
/// 未配置 SMTP 时（开发/演示模式），验证码打印到控制台，SendCodeAsync 返回 false。
/// </summary>
public class EmailService
{
    private readonly SmtpConfig _smtp;

    public EmailService(AppSettings appSettings)
    {
        _smtp = appSettings.Smtp;
    }

    /// <summary>
    /// 发送 6 位验证码邮件。返回 true 表示已通过 SMTP 实际发送。
    /// </summary>
    public async Task<bool> SendCodeAsync(string to, string code)
    {
        if (string.IsNullOrWhiteSpace(_smtp.Host))
        {
            Console.WriteLine($"[MAIL] SMTP 未配置，验证码仅打印到控制台（开发模式）: {to} -> {code}");
            return false;
        }

        try
        {
#pragma warning disable SYSLIB0037 // System.Net.Mail.SmtpClient 已过时，但保持零额外依赖
            using var client = new SmtpClient(_smtp.Host, _smtp.Port)
            {
                EnableSsl = _smtp.EnableSsl,
                Timeout = 10000
            };
#pragma warning restore SYSLIB0037

            if (!string.IsNullOrWhiteSpace(_smtp.User))
            {
                client.Credentials = new NetworkCredential(_smtp.User, _smtp.Password);
            }

            var message = new MailMessage(_smtp.From, to)
            {
                Subject = "OnlineChat 注册验证码",
                Body = $"【OnlineChat】您的注册验证码是 {code}，5 分钟内有效，请勿泄露给他人。"
            };

            await client.SendMailAsync(message);
            Console.WriteLine($"[MAIL] 验证码已发送至 {to}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MAIL] 发送失败: {ex.Message}");
            return false;
        }
    }
}
