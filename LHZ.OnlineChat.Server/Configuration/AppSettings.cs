using LHZ.OnlineChat.Infrastructure;
using LHZ.OnlineChat.Infrastructure.Messaging;
using LHZ.OnlineChat.Infrastructure.Persistence;
using LHZ.OnlineChat.Infrastructure.Security;
using LHZ.OnlineChat.Infrastructure.Storage;

namespace LHZ.OnlineChat.Server.Configuration;

/// <summary>
/// appsettings / 环境变量的绑定目标。
/// 表现层只负责「读配置」，读完转换成 InfrastructureOptions 交给基础设施层，
/// 基础设施层因此不依赖 IConfiguration。
/// </summary>
public sealed class AppSettings
{
    public ConnectionStringsSection ConnectionStrings { get; set; } = new();

    public RedisSection Redis { get; set; } = new();

    public JwtSection Jwt { get; set; } = new();

    public CorsSection Cors { get; set; } = new();

    public SmtpSection Smtp { get; set; } = new();

    public RobotSection Robot { get; set; } = new();

    public AdminSection Admin { get; set; } = new();

    /// <summary>转换为基础设施层的配置对象</summary>
    public InfrastructureOptions ToInfrastructureOptions(string uploadsRootPath, bool isDevelopment)
        => new()
        {
            ConnectionString = ConnectionStrings.Default,
            RedisConnectionString = Redis.Connection,
            IsDevelopment = isDevelopment,
            Jwt = new JwtOptions
            {
                Secret = Jwt.Secret,
                Issuer = Jwt.Issuer,
                Audience = Jwt.Audience,
                ExpireMinutes = Jwt.ExpireMinutes
            },
            Smtp = new SmtpOptions
            {
                Host = Smtp.Host,
                Port = Smtp.Port,
                EnableSsl = Smtp.EnableSsl,
                User = Smtp.User,
                Password = Smtp.Password,
                From = Smtp.From
            },
            Robot = new RobotOptions { TokenKey = Robot.TokenKey },
            FileStorage = new FileStorageOptions { RootPath = uploadsRootPath },
            AdminBootstrap = new AdminBootstrapOptions
            {
                InitialUsername = Admin.InitialUsername,
                InitialPassword = Admin.InitialPassword
            }
        };
}

public sealed class ConnectionStringsSection
{
    public string Default { get; set; } = string.Empty;
}

public sealed class RedisSection
{
    public string Connection { get; set; } = "127.0.0.1:6379";
}

public sealed class JwtSection
{
    public string Secret { get; set; } = string.Empty;

    public string Issuer { get; set; } = "OnlineChat";

    public string Audience { get; set; } = "OnlineChat";

    public int ExpireMinutes { get; set; } = 1440;
}

public sealed class CorsSection
{
    /// <summary>允许的来源，多个用逗号分隔；"*" 表示允许所有（开发默认）</summary>
    public string AllowedOrigins { get; set; } = "*";
}

public sealed class SmtpSection
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 465;

    public bool EnableSsl { get; set; } = true;

    public string User { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string From { get; set; } = "no-reply@onlinechat.local";
}

public sealed class RobotSection
{
    /// <summary>机器人令牌加密密钥；生产务必通过 Robot__TokenKey 配置</summary>
    public string TokenKey { get; set; } = string.Empty;
}

public sealed class AdminSection
{
    public string InitialUsername { get; set; } = string.Empty;

    public string InitialPassword { get; set; } = string.Empty;
}
