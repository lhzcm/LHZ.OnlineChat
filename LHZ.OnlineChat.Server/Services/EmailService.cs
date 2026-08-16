using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using LHZ.OnlineChat.Server.Config;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace LHZ.OnlineChat.Server.Services;

/// <summary>
/// 邮件发送服务（基于 MailKit）。
/// 未配置 SMTP 时（开发/演示模式），验证码打印到控制台，SendCodeAsync 返回 false。
/// 部分网络环境 IPv6 路由不通会导致连接超时，因此优先解析 IPv4 建立连接，
/// 同时以配置域名作为 TLS 证书校验目标（IP 直连会导致证书主机名校验失败）。
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
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(_smtp.From));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = "OnlineChat 注册验证码";
            message.Body = new TextPart("plain")
            {
                Text = $"【OnlineChat】您的注册验证码是 {code}，5 分钟内有效，请勿泄露给他人。"
            };

            using var client = new SmtpClient();

            // 优先用 IPv4 地址建立 TCP 连接，再用配置域名完成 TLS（证书校验按域名）
            var ipv4 = (await Dns.GetHostAddressesAsync(_smtp.Host))
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

            if (ipv4 != null)
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(ipv4, _smtp.Port);

                var sslStream = new SslStream(new NetworkStream(socket, ownsSocket: true));
                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = _smtp.Host,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                });

                // 流已完成 TLS，通知 MailKit 无需再握手
                await client.ConnectAsync(sslStream, _smtp.Host, _smtp.Port, SecureSocketOptions.None);
            }
            else
            {
                // 兜底：按域名连接
                await client.ConnectAsync(_smtp.Host, _smtp.Port, SecureSocketOptions.SslOnConnect);
            }

            if (!string.IsNullOrWhiteSpace(_smtp.User))
            {
                await client.AuthenticateAsync(_smtp.User, _smtp.Password);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
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
