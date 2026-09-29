using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;

namespace LHZ.OnlineChat.Domain.Tests.Users;

public class EmailTests
{
    [Theory]
    [InlineData("a@test.local", "a@test.local")]
    [InlineData("  A@Test.LOCAL  ", "a@test.local")]   // 去空白 + 转小写
    [InlineData("User.Name+tag@Sub.Domain.COM", "user.name+tag@sub.domain.com")]
    public void Parse_归一化为去空白小写(string input, string expected)
    {
        Assert.Equal(expected, Email.Parse(input).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("no@domain")]        // 缺少点
    [InlineData("@test.local")]      // 缺少本地部分
    [InlineData("a@@test.local")]    // 多个 @
    [InlineData("a b@test.local")]   // 含空格
    [InlineData("a@test .local")]
    public void Parse_格式非法时抛出统一提示语(string? input)
    {
        var ex = Assert.Throws<DomainException>(() => Email.Parse(input));

        Assert.Equal("邮箱格式不正确", ex.Message);
    }

    [Fact]
    public void Parse_超长时抛出()
    {
        var tooLong = new string('a', 200) + "@test.local";

        var ex = Assert.Throws<DomainException>(() => Email.Parse(tooLong));

        Assert.Equal("邮箱长度不能超过 200 个字符", ex.Message);
    }

    [Theory]
    [InlineData("a@test.local", true)]
    [InlineData("not-an-email", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_非法时返回null而不抛出(string? input, bool shouldParse)
    {
        // 登录接口的输入既可能是账号 ID 也可能是邮箱，不能因为格式不符就抛业务异常
        var result = Email.TryParse(input);

        Assert.Equal(shouldParse, result is not null);
    }

    [Fact]
    public void TryParse_同样做归一化()
    {
        Assert.Equal("a@test.local", Email.TryParse("  A@TEST.LOCAL ")!.Value);
    }

    [Fact]
    public void 值相等语义_归一化后相同即相等()
    {
        Assert.Equal(Email.Parse("A@Test.local"), Email.Parse("a@test.local"));
        Assert.Equal(Email.Parse("a@test.local").GetHashCode(), Email.Parse("A@TEST.LOCAL").GetHashCode());
    }

    [Fact]
    public void ToString_返回归一化后的值()
    {
        Assert.Equal("a@test.local", Email.Parse("A@Test.Local").ToString());
    }
}

public class PasswordHashTests
{
    [Fact]
    public void FromHash_包装哈希串()
    {
        var hash = PasswordHash.FromHash("$2a$11$abcdef");

        Assert.Equal("$2a$11$abcdef", hash.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FromHash_空值时抛出(string? input)
    {
        var ex = Assert.Throws<DomainException>(() => PasswordHash.FromHash(input!));

        Assert.Equal("口令哈希不能为空", ex.Message);
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("a very long passphrase")]
    public void EnsureRawPasswordValid_满足长度时通过(string password)
    {
        PasswordHash.EnsureRawPasswordValid(password);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    public void EnsureRawPasswordValid_过短或空白时抛出(string? password)
    {
        var ex = Assert.Throws<DomainException>(() => PasswordHash.EnsureRawPasswordValid(password));

        Assert.Equal("密码长度不能少于 6 个字符", ex.Message);
    }

    [Fact]
    public void 值相等语义()
    {
        Assert.Equal(PasswordHash.FromHash("$2a$x"), PasswordHash.FromHash("$2a$x"));
        Assert.NotEqual(PasswordHash.FromHash("$2a$x"), PasswordHash.FromHash("$2a$y"));
    }
}

public class NicknameRulesTests
{
    [Fact]
    public void Normalize_去除首尾空白()
    {
        Assert.Equal("张三", NicknameRules.Normalize("  张三\t"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void Normalize_空白时抛出(string? input)
    {
        Assert.Equal("昵称不能为空", Assert.Throws<DomainException>(
            () => NicknameRules.Normalize(input)).Message);
    }

    [Fact]
    public void Normalize_超长时抛出()
    {
        Assert.Equal($"昵称长度不能超过 {NicknameRules.MaxLength} 个字符",
            Assert.Throws<DomainException>(
                () => NicknameRules.Normalize(new string('x', NicknameRules.MaxLength + 1))).Message);
    }

    [Fact]
    public void Normalize_长度按去空白后计算()
    {
        // 首尾空白不应把合法昵称顶出长度上限
        var padded = "  " + new string('x', NicknameRules.MaxLength) + "  ";

        Assert.Equal(NicknameRules.MaxLength, NicknameRules.Normalize(padded).Length);
    }
}
