namespace LHZ.OnlineChat.Server.Config;

/// <summary>
/// 应用配置类，映射 appsettings.json
/// </summary>
public class AppSettings
{
    public ConnectionStringsConfig ConnectionStrings { get; set; } = new();
    public RedisConfig Redis { get; set; } = new();
    public JwtConfig Jwt { get; set; } = new();
    public CorsConfig Cors { get; set; } = new();
    public SmtpConfig Smtp { get; set; } = new();
    public RobotConfig Robot { get; set; } = new();
}

public class ConnectionStringsConfig
{
    public string Default { get; set; } = string.Empty;
}

public class RedisConfig
{
    public string Connection { get; set; } = "127.0.0.1:6379";
}

public class JwtConfig
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "OnlineChat";
    public string Audience { get; set; } = "OnlineChat";
    public int ExpireMinutes { get; set; } = 1440;
}

public class CorsConfig
{
    /// <summary>
    /// 允许的来源，多个用逗号分隔；"*" 表示允许所有来源（开发环境默认）
    /// </summary>
    public string AllowedOrigins { get; set; } = "*";
}

public class SmtpConfig
{
    /// <summary>
    /// SMTP 服务器地址；为空表示未配置（验证码打印到控制台，开发模式）
    /// </summary>
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 465;
    public bool EnableSsl { get; set; } = true;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = "no-reply@onlinechat.local";
}

public class RobotConfig
{
    /// <summary>
    /// 机器人令牌加密密钥（AES-256-GCM 派生）。
    /// 生产环境务必通过环境变量 Robot__TokenKey 配置，未配置时使用内置开发密钥并打印警告。
    /// </summary>
    public string TokenKey { get; set; } = string.Empty;
}
