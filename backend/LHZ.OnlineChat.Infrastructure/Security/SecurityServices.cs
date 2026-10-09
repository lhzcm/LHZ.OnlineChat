using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Robots;
using LHZ.OnlineChat.Domain.Users;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace LHZ.OnlineChat.Infrastructure.Security;

/// <summary>BCrypt 口令哈希</summary>
internal sealed class BCryptPasswordHasher : IPasswordHasher
{
    public PasswordHash Hash(string rawPassword)
        => PasswordHash.FromHash(BCrypt.Net.BCrypt.HashPassword(rawPassword));

    public bool Verify(string? rawPassword, PasswordHash? hash)
    {
        if (string.IsNullOrEmpty(rawPassword) || hash is null) return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(rawPassword, hash.Value);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // 库里存了非 BCrypt 格式的历史值时不抛，按校验失败处理
            return false;
        }
    }
}

/// <summary>JWT 签发</summary>
internal sealed class JwtTokenIssuer : ITokenIssuer
{
    /// <summary>刷新令牌熵</summary>
    private const int RefreshTokenBytes = 64;

    private readonly JwtOptions _options;

    public JwtTokenIssuer(JwtOptions options) => _options = options;

    /// <summary>用户令牌携带 sid（会话标识）：踢下线时按会话精确失效，WS 连接也据此关联</summary>
    public string IssueUserToken(User user, string sessionId)
        => Write(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.Nickname),
            new Claim("nickname", user.Nickname),
            new Claim("sid", sessionId)
        });

    /// <summary>
    /// 管理员令牌带 role=admin + arole（角色等级）+ sid（会话标识）。
    ///
    /// sid 是「停用管理员后立刻失效」的前提：没有它，令牌在有效期内无法吊销，
    /// 停用操作要等最长 ExpireMinutes 才真正生效。
    /// 校验走 IAdminSessionStore（键位与用户会话独立）。
    /// </summary>
    public string IssueAdminToken(Admin admin, string sessionId)
        => Write(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, admin.Username),
            new Claim("role", "admin"),
            new Claim("arole", ((int)admin.Role).ToString(CultureInfo.InvariantCulture)),
            new Claim("sid", sessionId)
        });

    public string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(RefreshTokenBytes));

    private string Write(Claim[] claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.ExpireMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>JWT 配置（由宿主绑定后注入）</summary>
public sealed class JwtOptions
{
    public string Secret { get; set; } = string.Empty;

    public string Issuer { get; set; } = "OnlineChat";

    public string Audience { get; set; } = "OnlineChat";

    public int ExpireMinutes { get; set; } = 1440;
}

/// <summary>
/// 机器人对外令牌的加解密：AES-256-GCM 加密内部自增 ID。
/// 布局 nonce(12) + cipher(8) + tag(16)，base64url 输出，对外不泄露内部 ID。
/// </summary>
internal sealed class RobotTokenCipher : IRobotTokenCipher
{
    private const int NonceSize = 12;
    private const int PlainSize = 8;   // long
    private const int TagSize = 16;

    /// <summary>未配置 Robot__TokenKey 时使用的内置开发密钥</summary>
    private static readonly byte[] DevelopmentKey =
        SHA256.HashData(Encoding.UTF8.GetBytes("lhz-onlinechat-dev-robot-token-key-2026"));

    private readonly byte[] _key;

    public RobotTokenCipher(RobotOptions options, ILogger<RobotTokenCipher> logger)
    {
        if (string.IsNullOrWhiteSpace(options.TokenKey))
        {
            logger.LogWarning(
                "未配置 Robot__TokenKey，机器人令牌使用内置开发密钥（生产环境务必配置环境变量）");
            _key = DevelopmentKey;
        }
        else
        {
            _key = SHA256.HashData(Encoding.UTF8.GetBytes(options.TokenKey));
        }
    }

    public string Encode(long robotId)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = BitConverter.GetBytes(robotId);
        var cipher = new byte[PlainSize];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var raw = new byte[NonceSize + PlainSize + TagSize];
        Buffer.BlockCopy(nonce, 0, raw, 0, NonceSize);
        Buffer.BlockCopy(cipher, 0, raw, NonceSize, PlainSize);
        Buffer.BlockCopy(tag, 0, raw, NonceSize + PlainSize, TagSize);

        return Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public long Decode(string? token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token)) return 0;

            var base64 = token.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');

            var raw = Convert.FromBase64String(base64);
            if (raw.Length != NonceSize + PlainSize + TagSize) return 0;

            var plain = new byte[PlainSize];
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(
                raw.AsSpan(0, NonceSize),
                raw.AsSpan(NonceSize, PlainSize),
                raw.AsSpan(NonceSize + PlainSize, TagSize),
                plain);

            return BitConverter.ToInt64(plain);
        }
        catch (Exception)
        {
            // 令牌被篡改/格式非法：一律当作无效，不区分原因（避免信息泄露）
            return 0;
        }
    }
}

/// <summary>机器人配置</summary>
public sealed class RobotOptions
{
    /// <summary>令牌加密密钥；生产环境务必通过 Robot__TokenKey 配置</summary>
    public string TokenKey { get; set; } = string.Empty;

    /// <summary>
    /// 是否允许 Webhook 指向内网/本机地址。默认关闭（SSRF 防护）。
    ///
    /// 打开后，任意用户都能让服务端去 POST 内网地址（云元数据 169.254.169.254、
    /// 内网管理端口等），只应在「Webhook 确实是自建的、且部署方清楚这一点」时开启。
    /// </summary>
    public bool AllowPrivateWebhookTargets { get; set; }
}

/// <summary>Webhook HMAC-SHA256 签名</summary>
internal sealed class HmacWebhookSigner : IWebhookSigner
{
    public string Sign(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    /// <summary>恒定时间比较，避免时序侧信道</summary>
    public bool Verify(string secret, string body, string? signature)
    {
        if (string.IsNullOrEmpty(signature)) return false;

        var expected = Sign(secret, body);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signature),
            Encoding.UTF8.GetBytes(expected));
    }
}
