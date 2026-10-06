using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Users;
using LHZ.OnlineChat.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace LHZ.OnlineChat.Infrastructure.Tests.Security;

/// <summary>真实 BCrypt 的性质：加盐、不落明文、可校验</summary>
public class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_结果不包含明文()
    {
        var hash = _hasher.Hash("pass123456");

        Assert.DoesNotContain("pass123456", hash.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Hash_同一口令两次结果不同_说明加了随机盐()
    {
        var first = _hasher.Hash("pass123456");
        var second = _hasher.Hash("pass123456");

        Assert.NotEqual(first.Value, second.Value);
    }

    [Fact]
    public void Verify_正确口令通过()
    {
        var hash = _hasher.Hash("pass123456");

        Assert.True(_hasher.Verify("pass123456", hash));
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("pass12345")]      // 少一位
    [InlineData("PASS123456")]     // 大小写敏感
    [InlineData("")]
    [InlineData(null)]
    public void Verify_错误口令被拒(string? password)
    {
        var hash = _hasher.Hash("pass123456");

        Assert.False(_hasher.Verify(password, hash));
    }

    [Fact]
    public void Verify_哈希为null时返回false_而非抛异常()
    {
        // 机器人账号的 PasswordHash 就是 null，登录路径会走到这里
        Assert.False(_hasher.Verify("anything", null));
    }

    [Fact]
    public void Verify_库里存了非BCrypt格式的历史值时返回false_不抛异常()
    {
        var malformed = PasswordHash.FromHash("plain-text-from-ancient-data");

        Assert.False(_hasher.Verify("anything", malformed));
    }

    [Fact]
    public void Hash_同一口令的不同哈希都能校验通过()
    {
        var first = _hasher.Hash("pass123456");
        var second = _hasher.Hash("pass123456");

        Assert.True(_hasher.Verify("pass123456", first));
        Assert.True(_hasher.Verify("pass123456", second));
    }
}

public class JwtTokenIssuerTests
{
    private static readonly JwtOptions Options = new()
    {
        Secret = "OnlineChat-Test-Secret-Key-AtLeast32Characters!",
        Issuer = "OnlineChat",
        Audience = "OnlineChat",
        ExpireMinutes = 60
    };

    private readonly JwtTokenIssuer _issuer = new(Options);

    private static User AnyUser(int id = 10001, string nickname = "张三")
    {
        var user = User.Register(
            nickname, Email.Parse("a@test.local"),
            PasswordHash.FromHash("$2a$11$x"), DateTime.UtcNow);
        user.AssignPersistedId(id);
        return user;
    }

    private static Admin AnyAdmin(int id = 1, AdminRole role = AdminRole.Super)
    {
        var admin = Admin.Create("admin", PasswordHash.FromHash("$2a$11$x"), role, DateTime.UtcNow);
        admin.AssignPersistedId(id);
        return admin;
    }

    private static System.IdentityModel.Tokens.Jwt.JwtSecurityToken Parse(string token)
        => new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);

    [Fact]
    public void 用户令牌携带账号ID与会话标识()
    {
        var token = Parse(_issuer.IssueUserToken(AnyUser(), "session-abc"));

        Assert.Equal("10001",
            token.Claims.First(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier).Value);
        Assert.Equal("session-abc", token.Claims.First(c => c.Type == "sid").Value);
    }

    [Fact]
    public void 用户令牌携带昵称()
    {
        var token = Parse(_issuer.IssueUserToken(AnyUser(nickname: "李四"), "s1"));

        Assert.Equal("李四", token.Claims.First(c => c.Type == "nickname").Value);
    }

    [Fact]
    public void 用户令牌不带管理员角色_普通令牌无法访问管理接口()
    {
        var token = Parse(_issuer.IssueUserToken(AnyUser(), "s1"));

        Assert.DoesNotContain(token.Claims, c => c.Type == "role");
        Assert.DoesNotContain(token.Claims, c => c.Type == "arole");
    }

    [Fact]
    public void 管理员令牌携带role与arole()
    {
        var token = Parse(_issuer.IssueAdminToken(AnyAdmin(role: AdminRole.Super), "sess-1"));

        Assert.Equal("admin", token.Claims.First(c => c.Type == "role").Value);
        Assert.Equal("0", token.Claims.First(c => c.Type == "arole").Value);
    }

    [Fact]
    public void 运营管理员的arole为1()
    {
        var token = Parse(_issuer.IssueAdminToken(AnyAdmin(role: AdminRole.Operator), "sess-2"));

        Assert.Equal("1", token.Claims.First(c => c.Type == "arole").Value);
    }

    [Fact]
    public void 管理员令牌带sid_以便停用或删除后立即失效()
    {
        var token = Parse(_issuer.IssueAdminToken(AnyAdmin(), "sess-admin"));

        // sid 是管理员会话可吊销的前提：鉴权管道会拿它去 IAdminSessionStore 校验。
        // 没有 sid 的旧令牌在新管道里一律被拒（无法吊销的令牌不能放行）。
        Assert.Equal("sess-admin", token.Claims.First(c => c.Type == "sid").Value);
    }

    [Fact]
    public void 令牌带正确的签发方与受众()
    {
        var token = Parse(_issuer.IssueUserToken(AnyUser(), "s1"));

        Assert.Equal("OnlineChat", token.Issuer);
        Assert.Contains("OnlineChat", token.Audiences);
    }

    [Fact]
    public void 令牌过期时间按配置()
    {
        var before = DateTime.UtcNow;

        var token = Parse(_issuer.IssueUserToken(AnyUser(), "s1"));

        var expected = before.AddMinutes(Options.ExpireMinutes);
        Assert.InRange(token.ValidTo, expected.AddMinutes(-1), expected.AddMinutes(1));
    }

    [Fact]
    public void 刷新令牌足够长且每次不同()
    {
        var first = _issuer.GenerateRefreshToken();
        var second = _issuer.GenerateRefreshToken();

        Assert.NotEqual(first, second);
        // 64 字节 base64 → 88 字符
        Assert.True(first.Length >= 80, $"长度仅 {first.Length}");
    }

    [Fact]
    public void 刷新令牌是合法base64()
    {
        var token = _issuer.GenerateRefreshToken();

        Assert.Equal(64, Convert.FromBase64String(token).Length);
    }
}

public class RobotTokenCipherTests
{
    private static RobotTokenCipher Cipher(string key = "test-robot-key")
        => new(new RobotOptions { TokenKey = key }, NullLogger<RobotTokenCipher>.Instance);

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(long.MaxValue)]
    public void 加解密往返一致(long robotId)
    {
        var cipher = Cipher();

        Assert.Equal(robotId, cipher.Decode(cipher.Encode(robotId)));
    }

    [Fact]
    public void 令牌是URL安全的base64url()
    {
        var token = Cipher().Encode(12345);

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void 同一ID每次加密结果不同_随机nonce()
    {
        var cipher = Cipher();

        var first = cipher.Encode(42);
        var second = cipher.Encode(42);

        Assert.NotEqual(first, second);
        // 但都能解回同一个 ID
        Assert.Equal(42, cipher.Decode(first));
        Assert.Equal(42, cipher.Decode(second));
    }

    [Fact]
    public void 相邻ID的令牌不可互相推测()
    {
        var cipher = Cipher();

        var first = cipher.Encode(1);
        var second = cipher.Encode(2);

        var sharedPrefix = first.Zip(second).TakeWhile(p => p.First == p.Second).Count();
        Assert.True(sharedPrefix <= 2, $"共同前缀过长（{sharedPrefix} 字符）");
    }

    [Fact]
    public void 令牌不泄露内部自增ID的明文()
    {
        var token = Cipher().Encode(987654321);

        Assert.DoesNotContain("987654321", token, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("!!!not-base64!!!")]
    [InlineData("dG9vLXNob3J0")]      // 合法 base64 但长度不对
    public void 非法令牌解码为0(string? token)
    {
        Assert.Equal(0, Cipher().Decode(token));
    }

    [Fact]
    public void 被篡改的令牌解码为0_GCM认证标签生效()
    {
        var cipher = Cipher();
        var token = cipher.Encode(42);

        // 翻转最后一个字符
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        Assert.Equal(0, cipher.Decode(tampered));
    }

    [Fact]
    public void 不同密钥无法解开对方的令牌()
    {
        var token = Cipher("key-one").Encode(42);

        Assert.Equal(0, Cipher("key-two").Decode(token));
    }

    [Fact]
    public void 未配置密钥时回落内置开发密钥_且自身可往返()
    {
        var cipher = Cipher(string.Empty);

        Assert.Equal(42, cipher.Decode(cipher.Encode(42)));
    }

    [Fact]
    public void 两个未配置密钥的实例互相兼容_保证重启后旧令牌仍可用()
    {
        var token = Cipher(string.Empty).Encode(42);

        Assert.Equal(42, Cipher(string.Empty).Decode(token));
    }
}

public class HmacWebhookSignerTests
{
    private readonly HmacWebhookSigner _signer = new();

    [Fact]
    public void 签名稳定可复现()
    {
        var first = _signer.Sign("secret", "{\"a\":1}");
        var second = _signer.Sign("secret", "{\"a\":1}");

        Assert.Equal(first, second);
    }

    [Fact]
    public void 签名是小写十六进制()
    {
        var signature = _signer.Sign("secret", "body");

        Assert.Equal(64, signature.Length);   // SHA256 → 32 字节 → 64 hex
        Assert.Equal(signature.ToLowerInvariant(), signature);
        Assert.True(signature.All(Uri.IsHexDigit));
    }

    [Fact]
    public void 内容不同签名不同()
    {
        Assert.NotEqual(_signer.Sign("secret", "body-a"), _signer.Sign("secret", "body-b"));
    }

    [Fact]
    public void 密钥不同签名不同()
    {
        Assert.NotEqual(_signer.Sign("key-a", "body"), _signer.Sign("key-b", "body"));
    }

    [Fact]
    public void Verify_正确签名通过()
    {
        const string body = "{\"content\":\"hi\"}";
        var signature = _signer.Sign("secret", body);

        Assert.True(_signer.Verify("secret", body, signature));
    }

    [Fact]
    public void Verify_大小写不同的签名不通过_严格比对()
    {
        const string body = "body";
        var signature = _signer.Sign("secret", body).ToUpperInvariant();

        Assert.False(_signer.Verify("secret", body, signature));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("deadbeef")]
    public void Verify_缺失或错误签名被拒(string? signature)
    {
        Assert.False(_signer.Verify("secret", "body", signature));
    }

    [Fact]
    public void Verify_内容被篡改时不通过()
    {
        var signature = _signer.Sign("secret", "original");

        Assert.False(_signer.Verify("secret", "tampered", signature));
    }

    [Fact]
    public void Verify_换了密钥不通过()
    {
        var signature = _signer.Sign("key-a", "body");

        Assert.False(_signer.Verify("key-b", "body", signature));
    }

    [Fact]
    public void 中文内容按UTF8签名()
    {
        const string body = "{\"content\":\"你好世界\"}";
        var signature = _signer.Sign("secret", body);

        Assert.True(_signer.Verify("secret", body, signature));
    }
}
