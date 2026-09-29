using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Robots;

namespace LHZ.OnlineChat.Domain.Tests.Robots;

public class WebhookUrlTests
{
    [Theory]
    [InlineData("https://example.com/hook")]
    [InlineData("http://192.168.1.10:9000/hook")]   // 允许内网 http，便于本地调试
    public void Parse_接受http与https(string url)
    {
        var parsed = WebhookUrl.Parse(url);

        Assert.True(parsed.IsConfigured);
        Assert.Equal(url, parsed.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void Parse_空地址是合法的_表示纯推送模式(string? url)
    {
        // 纯推送模式：不接收消息回调，只由第三方主动推送
        var parsed = WebhookUrl.Parse(url);

        Assert.False(parsed.IsConfigured);
        Assert.Equal(WebhookUrl.None, parsed);
    }

    [Theory]
    [InlineData("ftp://example.com/hook")]
    [InlineData("ws://example.com/hook")]
    [InlineData("example.com/hook")]        // 缺少协议
    [InlineData("/relative/path")]
    [InlineData("javascript:alert(1)")]
    public void Parse_非http协议时抛出(string url)
    {
        var ex = Assert.Throws<DomainException>(() => WebhookUrl.Parse(url));

        Assert.Equal("Webhook 地址必须是 http/https 开头", ex.Message);
    }

    [Fact]
    public void Parse_超长时抛出()
    {
        var url = "https://example.com/" + new string('a', WebhookUrl.MaxLength);

        var ex = Assert.Throws<DomainException>(() => WebhookUrl.Parse(url));

        Assert.Equal("Webhook 地址过长", ex.Message);
    }

    [Fact]
    public void Parse_去除首尾空白()
    {
        Assert.Equal("https://example.com/hook",
            WebhookUrl.Parse("  https://example.com/hook  ").Value);
    }
}

public class RobotCreationTests
{
    [Fact]
    public void Create_记录归属与配置()
    {
        var robot = Robot.Create(
            ownerId: 10001, botUserId: 10005, "助理", "/uploads/bot.png",
            WebhookUrl.Parse("https://example.com/hook"), "secret", timeoutMs: 8000, T.Now);

        Assert.Equal(10001, robot.OwnerId);
        Assert.Equal(10005, robot.UserId);
        Assert.Equal("助理", robot.Name);
        Assert.Equal("https://example.com/hook", robot.WebhookUrlValue);
        Assert.Equal("secret", robot.WebhookSecret);
        Assert.Equal(8000, robot.TimeoutMs);
        Assert.True(robot.Enabled);
        Assert.Equal(0, robot.PushCount);
        Assert.Equal(0, robot.CallbackFailCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_名称为空时抛出(string? name)
    {
        var ex = Assert.Throws<DomainException>(() => Robot.Create(
            10001, 10005, name!, null, WebhookUrl.None, null, null, T.Now));

        Assert.Equal("机器人名称不能为空", ex.Message);
    }

    [Fact]
    public void Create_名称超长时抛出()
    {
        var ex = Assert.Throws<DomainException>(() => Robot.Create(
            10001, 10005, new string('x', Robot.MaxNameLength + 1),
            null, WebhookUrl.None, null, null, T.Now));

        Assert.Equal($"机器人名称不能超过 {Robot.MaxNameLength} 个字符", ex.Message);
    }

    [Theory]
    [InlineData(null, Robot.DefaultTimeoutMs)]
    [InlineData(0, Robot.DefaultTimeoutMs)]
    [InlineData(-1, Robot.DefaultTimeoutMs)]
    [InlineData(70000, Robot.DefaultTimeoutMs)]        // 超上限回落默认
    [InlineData(5000, 5000)]
    [InlineData(Robot.MaxTimeoutMs, Robot.MaxTimeoutMs)]
    public void Create_超时值归一化(int? input, int expected)
    {
        var robot = Robot.Create(10001, 10005, "助理", null, WebhookUrl.None, null, input, T.Now);

        Assert.Equal(expected, robot.TimeoutMs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_密钥空白时归为null_表示不验签(string? secret)
    {
        var robot = Robot.Create(10001, 10005, "助理", null, WebhookUrl.None, secret, null, T.Now);

        Assert.Null(robot.WebhookSecret);
        Assert.False(robot.SignatureRequired);
    }

    [Fact]
    public void AssignToken_绑定对外令牌()
    {
        var robot = TestRobots.Create();

        robot.AssignToken("encrypted-token");

        Assert.Equal("encrypted-token", robot.Token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AssignToken_空值时抛出(string? token)
    {
        var ex = Assert.Throws<DomainException>(() => TestRobots.Create().AssignToken(token!));

        Assert.Equal("机器人令牌不能为空", ex.Message);
    }
}

public class RobotStateTests
{
    [Fact]
    public void SignatureRequired_配了密钥才强制验签()
    {
        Assert.True(TestRobots.Create(secret: "s").SignatureRequired);
        Assert.False(TestRobots.Create(secret: null).SignatureRequired);
    }

    [Fact]
    public void RespondsToMessages_需要同时启用且配了回调地址()
    {
        Assert.True(TestRobots.Create(webhook: "https://e.com/h", enabled: true).RespondsToMessages);

        // 纯推送模式：不接收消息回调
        Assert.False(TestRobots.Create(webhook: "", enabled: true).RespondsToMessages);

        // 停用后即使配了地址也不触发
        Assert.False(TestRobots.Create(webhook: "https://e.com/h", enabled: false).RespondsToMessages);
    }

    [Fact]
    public void EnsureOwnedBy_非创建者时抛不存在_不泄露存在性()
    {
        var robot = TestRobots.Create(ownerId: 10001);

        robot.EnsureOwnedBy(10001);

        var ex = Assert.Throws<EntityNotFoundException>(() => robot.EnsureOwnedBy(10002));
        Assert.Equal("机器人不存在", ex.Message);
    }

    [Fact]
    public void EnsureTestable_纯推送模式不支持测试触发()
    {
        var pushOnly = TestRobots.Create(webhook: "");

        var ex = Assert.Throws<DomainException>(pushOnly.EnsureTestable);

        Assert.Equal("该机器人未配置 Webhook 地址，仅支持第三方主动推送", ex.Message);
    }

    [Fact]
    public void EnsureTestable_配了地址时通过()
    {
        TestRobots.Create(webhook: "https://e.com/h").EnsureTestable();
    }

    [Fact]
    public void SetEnabled_启停()
    {
        var robot = TestRobots.Create();

        robot.SetEnabled(false);
        Assert.False(robot.Enabled);

        robot.SetEnabled(true);
        Assert.True(robot.Enabled);
    }

    [Fact]
    public void RecordPush_与_RecordCallbackFailure_各自累计()
    {
        var robot = TestRobots.Create();

        robot.RecordPush();
        robot.RecordPush();
        robot.RecordCallbackFailure();

        Assert.Equal(2, robot.PushCount);
        Assert.Equal(1, robot.CallbackFailCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureReplyContentValid_空内容被拒(string? content)
    {
        var ex = Assert.Throws<DomainException>(() => Robot.EnsureReplyContentValid(content));

        Assert.Equal("回复内容不能为空", ex.Message);
    }

    [Fact]
    public void EnsureReplyContentValid_超长被拒()
    {
        var ex = Assert.Throws<DomainException>(
            () => Robot.EnsureReplyContentValid(new string('x', Robot.MaxReplyLength + 1)));

        Assert.Equal("回复内容过长", ex.Message);
    }

    [Fact]
    public void EnsureReplyContentValid_恰好达到上限时通过()
    {
        Robot.EnsureReplyContentValid(new string('x', Robot.MaxReplyLength));
    }
}

public class RobotUpdateTests
{
    [Fact]
    public void UpdateConfiguration_只改传入项_未传的保持原值()
    {
        var robot = TestRobots.Create(name: "原名", webhook: "https://old.com/h", secret: "old");

        robot.UpdateConfiguration(
            name: null, avatar: null, webhookUrl: "https://new.com/h",
            webhookSecret: null, timeoutMs: null, enabled: null);

        Assert.Equal("原名", robot.Name);
        Assert.Equal("https://new.com/h", robot.WebhookUrlValue);
        Assert.Equal("old", robot.WebhookSecret);
    }

    [Fact]
    public void UpdateConfiguration_改名时返回true_供调用方同步账号昵称()
    {
        var robot = TestRobots.Create(name: "原名");

        var nameChanged = robot.UpdateConfiguration("新名", null, null, null, null, null);

        Assert.True(nameChanged);
        Assert.Equal("新名", robot.Name);
    }

    [Fact]
    public void UpdateConfiguration_未改名时返回false()
    {
        var robot = TestRobots.Create();

        var nameChanged = robot.UpdateConfiguration(null, null, null, null, null, false);

        Assert.False(nameChanged);
    }

    [Fact]
    public void UpdateConfiguration_可把回调地址清空为纯推送模式()
    {
        var robot = TestRobots.Create(webhook: "https://old.com/h");

        robot.UpdateConfiguration(null, null, webhookUrl: "", null, null, null);

        Assert.Equal(string.Empty, robot.WebhookUrlValue);
        Assert.False(robot.RespondsToMessages);
    }

    [Fact]
    public void UpdateConfiguration_非法地址时抛出且不改动状态()
    {
        var robot = TestRobots.Create(webhook: "https://old.com/h");

        Assert.Throws<DomainException>(
            () => robot.UpdateConfiguration(null, null, "ftp://bad", null, null, null));

        Assert.Equal("https://old.com/h", robot.WebhookUrlValue);
    }

    [Fact]
    public void UpdateConfiguration_可清空密钥为不验签()
    {
        var robot = TestRobots.Create(secret: "old");

        robot.UpdateConfiguration(null, null, null, webhookSecret: "", null, null);

        Assert.Null(robot.WebhookSecret);
        Assert.False(robot.SignatureRequired);
    }

    [Fact]
    public void UpdateConfiguration_超时越界时保持原值()
    {
        var robot = TestRobots.Create(timeoutMs: 8000);

        robot.UpdateConfiguration(null, null, null, null, timeoutMs: 999999, null);

        Assert.Equal(8000, robot.TimeoutMs);
    }
}

internal static class TestRobots
{
    internal static Robot Create(
        long id = 1,
        int ownerId = 10001,
        int botUserId = 10005,
        string name = "助理",
        string webhook = "https://example.com/hook",
        string? secret = null,
        int? timeoutMs = null,
        bool enabled = true)
    {
        var robot = Robot.Create(
            ownerId, botUserId, name, null, WebhookUrl.Parse(webhook), secret, timeoutMs, T.Now);
        robot.AssignPersistedId(id);
        robot.AssignToken($"token-{id}");
        if (!enabled) robot.SetEnabled(false);
        return robot;
    }
}
