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

    /// <summary>JWT 密钥最小长度（HS256 的密钥短于哈希输出长度会削弱安全性）</summary>
    private const int MinSecretLength = 32;

    /// <summary>
    /// 仓库里出现过的默认密钥。生产环境若仍是这些值，等同没有密钥 ——
    /// 任何拿到源码的人都能伪造令牌。
    /// </summary>
    private static readonly HashSet<string> KnownDevelopmentSecrets = new(StringComparer.Ordinal)
    {
        "dev-only-insecure-signing-key-change-me-in-production",
        "OnlineChat-SuperSecret-Key-AtLeast32Characters!",
        "change-me-to-a-random-secret-at-least-32-chars"
    };

    /// <summary>CORS 是否允许所有来源（生产环境会据此告警）</summary>
    public bool AllowsAnyOrigin
        => string.IsNullOrWhiteSpace(Cors.AllowedOrigins) || Cors.AllowedOrigins == "*";

    /// <summary>
    /// 生产环境配置自检：不合格就拒绝启动。
    ///
    /// 做成「启动即失败」而不是打条警告，是因为这几项都写在仓库里：
    /// JWT 密钥泄露可以伪造任意用户/管理员令牌，Robot:TokenKey 为空会退回内置开发密钥
    /// （那个密钥同样在源码里，等于机器人令牌可被任意伪造）。
    /// 静默降级等于没有防护，而「部署时立刻报错」是最便宜的发现方式。
    /// </summary>
    public void EnsureProductionReady()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Jwt.Secret) || Jwt.Secret.Length < MinSecretLength)
        {
            problems.Add($"Jwt:Secret 未配置或短于 {MinSecretLength} 个字符");
        }
        else if (KnownDevelopmentSecrets.Contains(Jwt.Secret))
        {
            problems.Add("Jwt:Secret 仍是仓库默认值，必须换成随机串（可用 openssl rand -base64 48）");
        }

        if (string.IsNullOrWhiteSpace(ConnectionStrings.Default))
            problems.Add("ConnectionStrings:Default 未配置");

        if (string.IsNullOrWhiteSpace(Robot.TokenKey))
        {
            problems.Add(
                "Robot:TokenKey 未配置（未配置会退回内置开发密钥，机器人令牌可被伪造）");
        }

        if (problems.Count == 0) return;

        throw new InvalidOperationException(
            "生产环境配置未通过自检，已拒绝启动："
            + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Select(p => "  - " + p))
            + Environment.NewLine
            + "请通过环境变量（Jwt__Secret / ConnectionStrings__Default / Robot__TokenKey）配置。");
    }
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
