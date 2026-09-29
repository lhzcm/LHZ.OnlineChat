using LHZ.OnlineChat.Application.Tests.TestDoubles;
using LHZ.OnlineChat.Application.Users.Commands;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;

namespace LHZ.OnlineChat.Application.Tests.Users;

public class SendVerificationCodeTests
{
    private readonly ApplicationTestContext _ctx = new();

    private SendVerificationCodeHandler Handler()
        => new(_ctx.Users, _ctx.Codes, _ctx.Email);

    [Fact]
    public async Task 发送成功并返回冷却秒数()
    {
        var result = await Handler().Handle(
            new SendVerificationCodeCommand { Email = "a@test.local" }, default);

        Assert.True(result.Success);
        Assert.Equal(60, result.Data!.CooldownSeconds);
        Assert.Single(_ctx.Email.Sent);
    }

    [Fact]
    public async Task 验证码为6位数字且按5分钟有效期保存()
    {
        await Handler().Handle(new SendVerificationCodeCommand { Email = "a@test.local" }, default);

        var saved = Assert.Single(_ctx.Codes.Saved);
        Assert.Equal(6, saved.Code.Length);
        Assert.True(saved.Code.All(char.IsDigit));
        Assert.Equal(TimeSpan.FromMinutes(5), saved.Ttl);
    }

    [Fact]
    public async Task 未配置SMTP时回传devCode_保留本地调试体验()
    {
        _ctx.Email.SmtpConfigured = false;

        var result = await Handler().Handle(
            new SendVerificationCodeCommand { Email = "a@test.local" }, default);

        Assert.NotNull(result.Data!.DevCode);
        Assert.Equal(_ctx.Codes.Saved[0].Code, result.Data.DevCode);
    }

    [Fact]
    public async Task 已配置SMTP时不回传devCode()
    {
        _ctx.Email.SmtpConfigured = true;

        var result = await Handler().Handle(
            new SendVerificationCodeCommand { Email = "a@test.local" }, default);

        Assert.Null(result.Data!.DevCode);
    }

    [Fact]
    public async Task 邮箱归一化后保存_大小写不影响后续校验()
    {
        await Handler().Handle(new SendVerificationCodeCommand { Email = "  A@Test.LOCAL " }, default);

        Assert.Equal("a@test.local", _ctx.Codes.Saved[0].Email);
    }

    [Fact]
    public async Task 冷却期内重复发送被拒()
    {
        _ctx.Codes.Seed("a@test.local", "123456");

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SendVerificationCodeCommand { Email = "a@test.local" }, default));

        Assert.Equal("验证码已发送，请稍后再试", ex.Message);
        Assert.Empty(_ctx.Email.Sent);
    }

    [Fact]
    public async Task 忘记密码场景要求邮箱已注册()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SendVerificationCodeCommand
            {
                Email = "nobody@test.local",
                Purpose = VerificationPurposes.Forgot
            }, default));

        Assert.Equal("该邮箱未注册", ex.Message);
    }

    [Fact]
    public async Task 忘记密码场景_邮箱已注册时放行()
    {
        _ctx.GivenUser(email: "a@test.local");

        var result = await Handler().Handle(
            new SendVerificationCodeCommand
            {
                Email = "a@test.local",
                Purpose = VerificationPurposes.Forgot
            }, default);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task 注册场景不校验邮箱是否已注册_由注册用例负责()
    {
        // 否则「邮箱已注册」会在发码阶段泄露账号存在性
        _ctx.GivenUser(email: "a@test.local");

        var result = await Handler().Handle(
            new SendVerificationCodeCommand { Email = "a@test.local" }, default);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task 邮箱格式非法时抛出()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SendVerificationCodeCommand { Email = "not-an-email" }, default));

        Assert.Equal("邮箱格式不正确", ex.Message);
    }
}

public class RegisterUserTests
{
    private readonly ApplicationTestContext _ctx = new();

    private RegisterUserHandler Handler()
        => new(_ctx.Users, _ctx.Codes, _ctx.Hasher, _ctx.Events, _ctx.Clock);

    private static RegisterUserCommand ValidCommand(string email = "a@test.local", string code = "123456")
        => new() { Nickname = "张三", Email = email, Code = code, Password = "pass123456" };

    [Fact]
    public async Task 注册成功并分配起始10000的账号ID()
    {
        _ctx.Codes.Seed("a@test.local", "123456");

        var result = await Handler().Handle(ValidCommand(), default);

        Assert.True(result.Success);
        Assert.Equal(10000, result.Data!.AccountId);
        Assert.Contains("10000", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 注册后用户已落库且字段正确()
    {
        _ctx.Codes.Seed("a@test.local", "123456");

        await Handler().Handle(ValidCommand(), default);

        var user = Assert.Single(_ctx.Users.All);
        Assert.Equal("张三", user.Nickname);
        Assert.Equal("a@test.local", user.Email!.Value);
        Assert.False(user.IsBot);
        Assert.Equal(_ctx.Now, user.CreatedAt);
    }

    [Fact]
    public async Task 口令经哈希器处理后才存储()
    {
        // 这里只验证「用例把口令交给了 IPasswordHasher」；
        // 「哈希串不含明文、加盐、可校验」是 BCrypt 自身的性质，
        // 由 Infrastructure 的 BCryptPasswordHasher 测试覆盖。
        _ctx.Codes.Seed("a@test.local", "123456");

        await Handler().Handle(ValidCommand(), default);

        var user = Assert.Single(_ctx.Users.All);
        Assert.NotEqual("pass123456", user.PasswordHash!.Value);
        Assert.True(_ctx.Hasher.Verify("pass123456", user.PasswordHash));
        Assert.False(_ctx.Hasher.Verify("wrong", user.PasswordHash));
    }

    [Fact]
    public async Task 注册事件在主键回填之后发出_携带真实账号ID()
    {
        // 改造前这里是隐患点：若在 Insert 之前发事件，订阅方拿到的 UserId 是 0
        _ctx.Codes.Seed("a@test.local", "123456");

        await Handler().Handle(ValidCommand(), default);

        var registered = _ctx.Events.SingleEvent<UserRegistered>();
        Assert.Equal(10000, registered.UserId);
        Assert.Equal("张三", registered.Nickname);
    }

    [Fact]
    public async Task 验证码被消费_不能重放()
    {
        _ctx.Codes.Seed("a@test.local", "123456");
        await Handler().Handle(ValidCommand(), default);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => Handler().Handle(ValidCommand(email: "b@test.local"), default));

        Assert.Equal("验证码错误或已过期", ex.Message);
    }

    [Fact]
    public async Task 验证码错误时抛出且不建用户()
    {
        _ctx.Codes.Seed("a@test.local", "123456");

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => Handler().Handle(ValidCommand(code: "999999"), default));

        Assert.Equal("验证码错误或已过期", ex.Message);
        Assert.Empty(_ctx.Users.All);
    }

    [Fact]
    public async Task 邮箱已注册时抛出()
    {
        _ctx.GivenUser(email: "a@test.local");
        _ctx.Codes.Seed("a@test.local", "123456");

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => Handler().Handle(ValidCommand(), default));

        Assert.Equal("该邮箱已注册", ex.Message);
    }

    [Fact]
    public async Task 校验先于验证码消费_格式错误不浪费验证码()
    {
        _ctx.Codes.Seed("a@test.local", "123456");

        await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new RegisterUserCommand
            {
                Nickname = "张三", Email = "a@test.local", Code = "123456", Password = "123"
            }, default));

        // 口令长度不合法时验证码应保持可用
        Assert.True(await _ctx.Codes.HasPendingCodeAsync(Email.Parse("a@test.local")));
    }

    [Fact]
    public async Task 昵称与邮箱都被归一化()
    {
        _ctx.Codes.Seed("a@test.local", "123456");

        await Handler().Handle(new RegisterUserCommand
        {
            Nickname = "  张三  ", Email = "  A@Test.LOCAL ", Code = "123456", Password = "pass123456"
        }, default);

        var user = Assert.Single(_ctx.Users.All);
        Assert.Equal("张三", user.Nickname);
        Assert.Equal("a@test.local", user.Email!.Value);
    }

    [Fact]
    public async Task 连续注册的账号ID递增()
    {
        _ctx.Codes.Seed("a@test.local", "111111");
        _ctx.Codes.Seed("b@test.local", "222222");

        var first = await Handler().Handle(ValidCommand("a@test.local", "111111"), default);
        var second = await Handler().Handle(ValidCommand("b@test.local", "222222"), default);

        Assert.Equal(10000, first.Data!.AccountId);
        Assert.Equal(10001, second.Data!.AccountId);
    }
}

public class LoginTests
{
    private readonly ApplicationTestContext _ctx = new();

    private LoginHandler Handler()
        => new(_ctx.Users, _ctx.Hasher, _ctx.Tokens, _ctx.Sessions);

    [Fact]
    public async Task 按账号ID登录成功()
    {
        var user = _ctx.GivenUser();

        var result = await Handler().Handle(new LoginCommand
        {
            Account = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Password = "pass123456"
        }, default);

        Assert.True(result.Success);
        Assert.Equal(user.Id, result.Data!.User.Id);
        Assert.Equal("张三", result.Data.User.Nickname);
        Assert.NotEmpty(result.Data.Token);
        Assert.NotEmpty(result.Data.RefreshToken);
    }

    [Fact]
    public async Task 按邮箱登录成功()
    {
        var user = _ctx.GivenUser(email: "a@test.local");

        var result = await Handler().Handle(
            new LoginCommand { Account = "a@test.local", Password = "pass123456" }, default);

        Assert.True(result.Success);
        Assert.Equal(user.Id, result.Data!.User.Id);
    }

    [Fact]
    public async Task 邮箱大小写不敏感()
    {
        _ctx.GivenUser(email: "a@test.local");

        var result = await Handler().Handle(
            new LoginCommand { Account = "  A@TEST.LOCAL ", Password = "pass123456" }, default);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task 登录后创建会话并存储刷新令牌()
    {
        var user = _ctx.GivenUser();

        var result = await Handler().Handle(new LoginCommand
        {
            Account = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Password = "pass123456",
            DeviceName = "我的手机"
        }, default);

        var sessions = await _ctx.Sessions.ListSessionsAsync(user.Id);
        var session = Assert.Single(sessions);
        Assert.Equal("我的手机", session.DeviceName);

        var current = await _ctx.Sessions.GetCurrentRefreshTokenAsync(session.SessionId);
        Assert.Equal(result.Data!.RefreshToken, current);
    }

    [Fact]
    public async Task 未传设备名时记为未知设备()
    {
        var user = _ctx.GivenUser();

        await Handler().Handle(new LoginCommand
        {
            Account = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Password = "pass123456",
            DeviceName = "   "
        }, default);

        var session = Assert.Single(await _ctx.Sessions.ListSessionsAsync(user.Id));
        Assert.Equal("未知设备", session.DeviceName);
    }

    [Fact]
    public async Task 多端登录各自独立会话()
    {
        var user = _ctx.GivenUser();
        var command = new LoginCommand
        {
            Account = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Password = "pass123456"
        };

        await Handler().Handle(command, default);
        await Handler().Handle(command, default);

        Assert.Equal(2, (await _ctx.Sessions.ListSessionsAsync(user.Id)).Count);
    }

    [Fact]
    public async Task 口令错误时统一提示语()
    {
        var user = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(new LoginCommand
        {
            Account = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Password = "wrong"
        }, default));

        Assert.Equal("账号或密码错误", ex.Message);
    }

    [Fact]
    public async Task 账号不存在时与口令错误同一提示语_不泄露存在性()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new LoginCommand { Account = "99999999", Password = "pass123456" }, default));

        Assert.Equal("账号或密码错误", ex.Message);
    }

    [Fact]
    public async Task 邮箱格式非法时也走统一提示语()
    {
        // 登录输入既可能是账号也可能是邮箱，不该抛「邮箱格式不正确」
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new LoginCommand { Account = "garbage-input", Password = "pass123456" }, default));

        Assert.Equal("账号或密码错误", ex.Message);
    }

    [Theory]
    [InlineData("", "pass123456")]
    [InlineData("   ", "pass123456")]
    [InlineData("10000", "")]
    [InlineData("10000", "   ")]
    public async Task 账号或口令为空时提示补全(string account, string password)
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new LoginCommand { Account = account, Password = password }, default));

        Assert.Equal("请输入账号和密码", ex.Message);
    }

    [Fact]
    public async Task 机器人账号不能登录()
    {
        var (_, botUser) = _ctx.GivenRobot(ownerId: 10001);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(new LoginCommand
        {
            Account = botUser.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Password = "anything"
        }, default));

        Assert.Equal("机器人账号不能登录", ex.Message);
    }

    [Fact]
    public async Task 封禁账号登录被拒且提示封禁原因()
    {
        var user = _ctx.GivenUser();
        user.Ban("发布违规内容", _ctx.Now);
        await _ctx.Users.UpdateAsync(user);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(new LoginCommand
        {
            Account = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Password = "pass123456"
        }, default));

        Assert.Equal("发布违规内容", ex.Message);
    }

    [Fact]
    public async Task 封禁校验先于口令校验_不给出口令是否正确的线索()
    {
        var user = _ctx.GivenUser();
        user.Ban("封禁中", _ctx.Now);
        await _ctx.Users.UpdateAsync(user);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(new LoginCommand
        {
            Account = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Password = "wrong-password"
        }, default));

        Assert.Equal("封禁中", ex.Message);
    }

    [Fact]
    public async Task 登录失败时不创建会话()
    {
        var user = _ctx.GivenUser();

        await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(new LoginCommand
        {
            Account = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Password = "wrong"
        }, default));

        Assert.Empty(await _ctx.Sessions.ListSessionIdsAsync(user.Id));
    }
}

public class RefreshTokenTests
{
    private readonly ApplicationTestContext _ctx = new();

    private RefreshTokenHandler Handler() => new(_ctx.Users, _ctx.Tokens, _ctx.Sessions);

    private async Task<(Domain.Users.User User, string RefreshToken)> GivenLoggedInAsync()
    {
        var user = _ctx.GivenUser();
        var login = await new LoginHandler(_ctx.Users, _ctx.Hasher, _ctx.Tokens, _ctx.Sessions)
            .Handle(new LoginCommand
            {
                Account = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Password = "pass123456"
            }, default);
        return (user, login.Data!.RefreshToken);
    }

    [Fact]
    public async Task 刷新成功并返回新令牌()
    {
        var (user, refreshToken) = await GivenLoggedInAsync();

        var result = await Handler().Handle(
            new RefreshTokenCommand { RefreshToken = refreshToken }, default);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Data!.Token);
        Assert.Equal(user.Id, result.Data.User.Id);
    }

    [Fact]
    public async Task 刷新令牌被轮换()
    {
        var (_, refreshToken) = await GivenLoggedInAsync();

        var result = await Handler().Handle(
            new RefreshTokenCommand { RefreshToken = refreshToken }, default);

        Assert.NotEqual(refreshToken, result.Data!.RefreshToken);
    }

    [Fact]
    public async Task 旧刷新令牌轮换后立即失效_防复用()
    {
        var (_, refreshToken) = await GivenLoggedInAsync();
        await Handler().Handle(new RefreshTokenCommand { RefreshToken = refreshToken }, default);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new RefreshTokenCommand { RefreshToken = refreshToken }, default));

        Assert.Equal("RefreshToken 无效或已过期", ex.Message);
    }

    [Fact]
    public async Task 刷新不改变会话ID_多端互不影响()
    {
        var (user, refreshToken) = await GivenLoggedInAsync();
        var sessionBefore = Assert.Single(await _ctx.Sessions.ListSessionIdsAsync(user.Id));

        await Handler().Handle(new RefreshTokenCommand { RefreshToken = refreshToken }, default);

        var sessionAfter = Assert.Single(await _ctx.Sessions.ListSessionIdsAsync(user.Id));
        Assert.Equal(sessionBefore, sessionAfter);
    }

    [Fact]
    public async Task 刷新时更新最后活跃时间()
    {
        var (user, refreshToken) = await GivenLoggedInAsync();
        var sessionId = Assert.Single(await _ctx.Sessions.ListSessionIdsAsync(user.Id));

        await Handler().Handle(new RefreshTokenCommand { RefreshToken = refreshToken }, default);

        Assert.Contains(sessionId, _ctx.Sessions.TouchedSessions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task 令牌为空时提示不能为空(string token)
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new RefreshTokenCommand { RefreshToken = token }, default));

        Assert.Equal("RefreshToken 不能为空", ex.Message);
    }

    [Fact]
    public async Task 未知令牌被拒()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new RefreshTokenCommand { RefreshToken = "never-issued" }, default));

        Assert.Equal("RefreshToken 无效或已过期", ex.Message);
    }

    [Fact]
    public async Task 用户已被删除时抛出()
    {
        var (user, refreshToken) = await GivenLoggedInAsync();
        await _ctx.Users.DeleteAsync(user.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new RefreshTokenCommand { RefreshToken = refreshToken }, default));

        Assert.Equal("用户不存在", ex.Message);
    }
}
