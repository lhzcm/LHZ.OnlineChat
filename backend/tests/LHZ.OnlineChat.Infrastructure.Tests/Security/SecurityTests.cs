using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Users;
using LHZ.OnlineChat.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace LHZ.OnlineChat.Infrastructure.Tests.Security;

/// <summary>真实 BCrypt 的性质：加盐、不落明文、可校验</summary>
public class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new();

    [Fact(DisplayName = "Hash_结果不包含明文")]
    public void Hash_ResultDoesNotContainPlaintext()
    {
        var hash = _hasher.Hash("pass123456");

        Assert.DoesNotContain("pass123456", hash.Value, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Hash_同一口令两次结果不同_说明加了随机盐")]
    public void Hash_SamePasswordTwice_DiffersDueToRandomSalt()
    {
        var first = _hasher.Hash("pass123456");
        var second = _hasher.Hash("pass123456");

        Assert.NotEqual(first.Value, second.Value);
    }

    [Fact(DisplayName = "Verify_正确口令通过")]
    public void Verify_CorrectPassword_Succeeds()
    {
        var hash = _hasher.Hash("pass123456");

        Assert.True(_hasher.Verify("pass123456", hash));
    }

    [Theory(DisplayName = "Verify_错误口令被拒")]
    [InlineData("wrong")]
    [InlineData("pass12345")]      // 少一位
    [InlineData("PASS123456")]     // 大小写敏感
    [InlineData("")]
    [InlineData(null)]
    public void Verify_WrongPassword_IsRejected(string? password)
    {
        var hash = _hasher.Hash("pass123456");

        Assert.False(_hasher.Verify(password, hash));
    }

    [Fact(DisplayName = "Verify_哈希为null时返回false_而非抛异常")]
    public void Verify_NullHash_ReturnsFalseWithoutThrowing()
    {
        // 机器人账号的 PasswordHash 就是 null，登录路径会走到这里
        Assert.False(_hasher.Verify("anything", null));
    }

    [Fact(DisplayName = "Verify_库里存了非BCrypt格式的历史值时返回false_不抛异常")]
    public void Verify_LegacyNonBCryptHash_ReturnsFalseWithoutThrowing()
    {
        var malformed = PasswordHash.FromHash("plain-text-from-ancient-data");

        Assert.False(_hasher.Verify("anything", malformed));
    }

    [Fact(DisplayName = "Hash_同一口令的不同哈希都能校验通过")]
    public void Hash_DifferentHashesOfSamePassword_AllVerify()
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

    [Fact(DisplayName = "用户令牌携带账号ID与会话标识")]
    public void IssueUserToken_CarriesAccountIdAndSessionId()
    {
        var token = Parse(_issuer.IssueUserToken(AnyUser(), "session-abc"));

        Assert.Equal("10001",
            token.Claims.First(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier).Value);
        Assert.Equal("session-abc", token.Claims.First(c => c.Type == "sid").Value);
    }

    [Fact(DisplayName = "用户令牌携带昵称")]
    public void IssueUserToken_CarriesNickname()
    {
        var token = Parse(_issuer.IssueUserToken(AnyUser(nickname: "李四"), "s1"));

        Assert.Equal("李四", token.Claims.First(c => c.Type == "nickname").Value);
    }

    [Fact(DisplayName = "用户令牌不带管理员角色_普通令牌无法访问管理接口")]
    public void IssueUserToken_OmitsAdminRoles_DeniesAdminAccess()
    {
        var token = Parse(_issuer.IssueUserToken(AnyUser(), "s1"));

        Assert.DoesNotContain(token.Claims, c => c.Type == "role");
        Assert.DoesNotContain(token.Claims, c => c.Type == "arole");
    }

    [Fact(DisplayName = "管理员令牌携带role与arole")]
    public void IssueAdminToken_IncludesRoleAndAroleClaims()
    {
        var token = Parse(_issuer.IssueAdminToken(AnyAdmin(role: AdminRole.Super), "sess-1"));

        Assert.Equal("admin", token.Claims.First(c => c.Type == "role").Value);
        Assert.Equal("0", token.Claims.First(c => c.Type == "arole").Value);
    }

    [Fact(DisplayName = "运营管理员的arole为1")]
    public void IssueAdminToken_OperatorRole_AroleIsOne()
    {
        var token = Parse(_issuer.IssueAdminToken(AnyAdmin(role: AdminRole.Operator), "sess-2"));

        Assert.Equal("1", token.Claims.First(c => c.Type == "arole").Value);
    }

    [Fact(DisplayName = "管理员令牌带sid_以便停用或删除后立即失效")]
    public void IssueAdminToken_IncludesSid_EnablesImmediateRevocation()
    {
        var token = Parse(_issuer.IssueAdminToken(AnyAdmin(), "sess-admin"));

        // sid 是管理员会话可吊销的前提：鉴权管道会拿它去 IAdminSessionStore 校验。
        // 没有 sid 的旧令牌在新管道里一律被拒（无法吊销的令牌不能放行）。
        Assert.Equal("sess-admin", token.Claims.First(c => c.Type == "sid").Value);
    }

    [Fact(DisplayName = "令牌带正确的签发方与受众")]
    public void IssueToken_SetsCorrectIssuerAndAudience()
    {
        var token = Parse(_issuer.IssueUserToken(AnyUser(), "s1"));

        Assert.Equal("OnlineChat", token.Issuer);
        Assert.Contains("OnlineChat", token.Audiences);
    }

    [Fact(DisplayName = "令牌过期时间按配置")]
    public void IssueToken_ExpiryFollowsConfiguration()
    {
        var before = DateTime.UtcNow;

        var token = Parse(_issuer.IssueUserToken(AnyUser(), "s1"));

        var expected = before.AddMinutes(Options.ExpireMinutes);
        Assert.InRange(token.ValidTo, expected.AddMinutes(-1), expected.AddMinutes(1));
    }

    [Fact(DisplayName = "刷新令牌足够长且每次不同")]
    public void GenerateRefreshToken_IsLongEnoughAndUnique()
    {
        var first = _issuer.GenerateRefreshToken();
        var second = _issuer.GenerateRefreshToken();

        Assert.NotEqual(first, second);
        // 64 字节 base64 → 88 字符
        Assert.True(first.Length >= 80, $"长度仅 {first.Length}");
    }

    [Fact(DisplayName = "刷新令牌是合法base64")]
    public void GenerateRefreshToken_IsValidBase64()
    {
        var token = _issuer.GenerateRefreshToken();

        Assert.Equal(64, Convert.FromBase64String(token).Length);
    }
}

public class RobotTokenCipherTests
{
    private static RobotTokenCipher Cipher(string key = "test-robot-key")
        => new(new RobotOptions { TokenKey = key }, NullLogger<RobotTokenCipher>.Instance);

    [Theory(DisplayName = "加解密往返一致")]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(long.MaxValue)]
    public void EncodeDecode_RoundTrip_ReturnsOriginalId(long robotId)
    {
        var cipher = Cipher();

        Assert.Equal(robotId, cipher.Decode(cipher.Encode(robotId)));
    }

    [Fact(DisplayName = "令牌是URL安全的base64url")]
    public void Encode_ProducesUrlSafeBase64UrlToken()
    {
        var token = Cipher().Encode(12345);

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact(DisplayName = "同一ID每次加密结果不同_随机nonce")]
    public void Encode_SameIdTwice_DiffersDueToRandomNonce()
    {
        var cipher = Cipher();

        var first = cipher.Encode(42);
        var second = cipher.Encode(42);

        Assert.NotEqual(first, second);
        // 但都能解回同一个 ID
        Assert.Equal(42, cipher.Decode(first));
        Assert.Equal(42, cipher.Decode(second));
    }

    [Fact(DisplayName = "相邻ID的令牌不可互相推测")]
    public void Encode_AdjacentIds_TokensAreNotPredictable()
    {
        var cipher = Cipher();

        var first = cipher.Encode(1);
        var second = cipher.Encode(2);

        var sharedPrefix = first.Zip(second).TakeWhile(p => p.First == p.Second).Count();
        Assert.True(sharedPrefix <= 2, $"共同前缀过长（{sharedPrefix} 字符）");
    }

    [Fact(DisplayName = "令牌不泄露内部自增ID的明文")]
    public void Encode_Token_DoesNotRevealPlaintextId()
    {
        var token = Cipher().Encode(987654321);

        Assert.DoesNotContain("987654321", token, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "非法令牌解码为0")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("!!!not-base64!!!")]
    [InlineData("dG9vLXNob3J0")]      // 合法 base64 但长度不对
    public void Decode_InvalidToken_ReturnsZero(string? token)
    {
        Assert.Equal(0, Cipher().Decode(token));
    }

    [Fact(DisplayName = "被篡改的令牌解码为0_GCM认证标签生效")]
    public void Decode_TamperedToken_ReturnsZeroViaGcmTag()
    {
        var cipher = Cipher();
        var token = cipher.Encode(42);

        // 翻转最后一个字符
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        Assert.Equal(0, cipher.Decode(tampered));
    }

    [Fact(DisplayName = "不同密钥无法解开对方的令牌")]
    public void Decode_WithDifferentKey_ReturnsZero()
    {
        var token = Cipher("key-one").Encode(42);

        Assert.Equal(0, Cipher("key-two").Decode(token));
    }

    [Fact(DisplayName = "未配置密钥时回落内置开发密钥_且自身可往返")]
    public void Encode_WithoutKey_FallsBackToDevKeyAndRoundTrips()
    {
        var cipher = Cipher(string.Empty);

        Assert.Equal(42, cipher.Decode(cipher.Encode(42)));
    }

    [Fact(DisplayName = "两个未配置密钥的实例互相兼容_保证重启后旧令牌仍可用")]
    public void Encode_KeylessInstances_AreMutuallyCompatible()
    {
        var token = Cipher(string.Empty).Encode(42);

        Assert.Equal(42, Cipher(string.Empty).Decode(token));
    }
}

public class HmacWebhookSignerTests
{
    private readonly HmacWebhookSigner _signer = new();

    [Fact(DisplayName = "签名稳定可复现")]
    public void Sign_SameInput_IsDeterministic()
    {
        var first = _signer.Sign("secret", "{\"a\":1}");
        var second = _signer.Sign("secret", "{\"a\":1}");

        Assert.Equal(first, second);
    }

    [Fact(DisplayName = "签名是小写十六进制")]
    public void Sign_ProducesLowercaseHex()
    {
        var signature = _signer.Sign("secret", "body");

        Assert.Equal(64, signature.Length);   // SHA256 → 32 字节 → 64 hex
        Assert.Equal(signature.ToLowerInvariant(), signature);
        Assert.True(signature.All(Uri.IsHexDigit));
    }

    [Fact(DisplayName = "内容不同签名不同")]
    public void Sign_DifferentBodies_ProduceDifferentSignatures()
    {
        Assert.NotEqual(_signer.Sign("secret", "body-a"), _signer.Sign("secret", "body-b"));
    }

    [Fact(DisplayName = "密钥不同签名不同")]
    public void Sign_DifferentKeys_ProduceDifferentSignatures()
    {
        Assert.NotEqual(_signer.Sign("key-a", "body"), _signer.Sign("key-b", "body"));
    }

    [Fact(DisplayName = "Verify_正确签名通过")]
    public void Verify_CorrectSignature_Succeeds()
    {
        const string body = "{\"content\":\"hi\"}";
        var signature = _signer.Sign("secret", body);

        Assert.True(_signer.Verify("secret", body, signature));
    }

    [Fact(DisplayName = "Verify_大小写不同的签名不通过_严格比对")]
    public void Verify_CaseMismatchedSignature_FailsStrictComparison()
    {
        const string body = "body";
        var signature = _signer.Sign("secret", body).ToUpperInvariant();

        Assert.False(_signer.Verify("secret", body, signature));
    }

    [Theory(DisplayName = "Verify_缺失或错误签名被拒")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("deadbeef")]
    public void Verify_MissingOrWrongSignature_IsRejected(string? signature)
    {
        Assert.False(_signer.Verify("secret", "body", signature));
    }

    [Fact(DisplayName = "Verify_内容被篡改时不通过")]
    public void Verify_TamperedBody_Fails()
    {
        var signature = _signer.Sign("secret", "original");

        Assert.False(_signer.Verify("secret", "tampered", signature));
    }

    [Fact(DisplayName = "Verify_换了密钥不通过")]
    public void Verify_WrongKey_Fails()
    {
        var signature = _signer.Sign("key-a", "body");

        Assert.False(_signer.Verify("key-b", "body", signature));
    }

    [Fact(DisplayName = "中文内容按UTF8签名")]
    public void Sign_ChineseContent_UsesUtf8Encoding()
    {
        const string body = "{\"content\":\"你好世界\"}";
        var signature = _signer.Sign("secret", body);

        Assert.True(_signer.Verify("secret", body, signature));
    }
}
