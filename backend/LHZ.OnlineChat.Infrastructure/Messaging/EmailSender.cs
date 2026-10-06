using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Domain.Users;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace LHZ.OnlineChat.Infrastructure.Messaging;

/// <summary>SMTP 配置</summary>
public sealed class SmtpOptions
{
    /// <summary>为空表示未配置 —— 验证码打印到日志（开发/演示模式）</summary>
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 465;

    public bool EnableSsl { get; set; } = true;

    public string User { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string From { get; set; } = "no-reply@onlinechat.local";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}

/// <summary>
/// MailKit 邮件发送。
///
/// 保留改造前的一个关键处理：部分网络环境 IPv6 路由不通会导致连接超时，
/// 因此优先解析 IPv4 建立 TCP 连接，同时以配置域名作为 TLS 证书校验目标
/// （直接用 IP 连接会导致证书主机名校验失败）。
/// </summary>
internal sealed class MailKitEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<MailKitEmailSender> _logger;

    public MailKitEmailSender(SmtpOptions options, ILogger<MailKitEmailSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>SMTP 是否已配置（未配置时验证码只落服务器日志）</summary>
    public bool IsConfigured => _options.IsConfigured;

    public async Task<bool> SendVerificationCodeAsync(
        Email to, string code, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
        {
            // 未配置 SMTP 是受支持的运行模式：验证码只落服务器日志，由运维取码完成注册/重置。
            // 措辞刻意不写「开发模式」—— 生产环境同样会走这条路径（例如刚从 .env.example 起的环境），
            // 写成开发模式会把排查方向带偏。
            _logger.LogWarning(
                "SMTP 未配置，验证码仅输出到服务器日志（需要发邮件请配置 Smtp:Host）：{Email} -> {Code}",
                to.Value, code);
            return false;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(_options.From));
            message.To.Add(MailboxAddress.Parse(to.Value));
            message.Subject = "OnlineChat 验证码";
            message.Body = new TextPart("plain")
            {
                Text = $"【OnlineChat】您的验证码是 {code}，5 分钟内有效，请勿泄露给他人。"
            };

            using var client = new SmtpClient();
            await ConnectAsync(client, ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(_options.User))
                await client.AuthenticateAsync(_options.User, _options.Password, ct).ConfigureAwait(false);

            await client.SendAsync(message, ct).ConfigureAwait(false);
            await client.DisconnectAsync(true, ct).ConfigureAwait(false);

            _logger.LogInformation("验证码已发送至 {Email}", to.Value);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 发信失败不应让注册流程整体失败：返回 false，调用方会回传 DevCode
            _logger.LogError(ex, "验证码邮件发送失败：{Email}", to.Value);
            return false;
        }
    }

    private async Task ConnectAsync(SmtpClient client, CancellationToken ct)
    {
        var ipv4 = (await Dns.GetHostAddressesAsync(_options.Host, ct).ConfigureAwait(false))
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

        if (ipv4 is null)
        {
            // 兜底：按域名直连
            await client
                .ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.SslOnConnect, ct)
                .ConfigureAwait(false);
            return;
        }

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(ipv4, _options.Port, ct).ConfigureAwait(false);

        var sslStream = new SslStream(new NetworkStream(socket, ownsSocket: true));
        await sslStream.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = _options.Host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            },
            ct).ConfigureAwait(false);

        // 流上 TLS 已就绪，告知 MailKit 不要再握手
        await client
            .ConnectAsync(sslStream, _options.Host, _options.Port, SecureSocketOptions.None, ct)
            .ConfigureAwait(false);
    }
}
