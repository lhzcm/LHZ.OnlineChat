using LHZ.OnlineChat.Application.Tests.TestDoubles;
using LHZ.OnlineChat.Application.Users.Commands;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;

namespace LHZ.OnlineChat.Application.Tests.Users;

public class SendVerificationCodeTests
{
    private readonly ApplicationTestContext _ctx = new();

    private SendVerificationCodeHandler Handler()
        => new(_ctx.Users, _ctx.Codes, _ctx.Email, _ctx.Env);

    [Fact(DisplayName = "发送成功并返回冷却秒数")]
    public async Task Send_Succeeds_ReturnsCooldownSeconds()
    {
        var result = await Handler().Handle(
            new SendVerificationCodeCommand { Email = "a@test.local" }, default);

        Assert.True(result.Success);
        Assert.Equal(60, result.Data!.CooldownSeconds);
        Assert.Single(_ctx.Email.Sent);
    }

    [Fact(DisplayName = "验证码为6位数字且按5分钟有效期保存")]
    public async Task Send_CodeIs6Digits_SavedWith5MinuteTtl()
    {
        await Handler().Handle(new SendVerificationCodeCommand { Email = "a@test.local" }, default);

        var saved = Assert.Single(_ctx.Codes.Saved);
        Assert.Equal(6, saved.Code.Length);
        Assert.True(saved.Code.All(char.IsDigit));
        Assert.Equal(TimeSpan.FromMinutes(5), saved.Ttl);
    }

    [Fact(DisplayName = "开发环境未配置SMTP时回传devCode_保留本地调试体验")]
    public async Task Send_DevEnvSmtpUnconfigured_ReturnsDevCodeForLocalDebug()
    {
        _ctx.Env.IsDevelopment = true;
        _ctx.Email.IsConfigured = false;

        var result = await Handler().Handle(
            new SendVerificationCodeCommand { Email = "a@test.local" }, default);

        Assert.NotNull(result.Data!.DevCode);
        Assert.Equal(_ctx.Codes.Saved[0].Code, result.Data.DevCode);
    }

    [Fact(DisplayName = "生产环境未配置SMTP时不回传devCode")]
    public async Task Send_ProdEnvWithoutSmtp_OmitsDevCode()
    {
        _ctx.Env.IsDevelopment = false;
        _ctx.Email.IsConfigured = false;

        // 这是账号接管路径的闸门：/send-code 与 /forgot-password 都是匿名接口，
        // 一旦把验证码放进响应体，攻击者用受害者邮箱请求一次即可重置其密码
        var result = await Handler().Handle(
            new SendVerificationCodeCommand { Email = "a@test.local" }, default);

        Assert.True(result.Success);
        Assert.Null(result.Data!.DevCode);
    }

    [Fact(DisplayName = "生产环境未配置SMTP时验证码仍然有效")]
    public async Task Send_ProdEnvWithoutSmtp_CodeStillValid()
    {
        // 回归测试：曾经把「未配置 SMTP」与「发送失败」当成同一件事，
        // 结果刚存下的验证码被立刻删除 —— 运维按服务器日志里的码去校验，
        // 永远得到「验证码错误或已过期」。
        _ctx.Env.IsDevelopment = false;
        _ctx.Email.IsConfigured = false;

        var result = await Handler().Handle(
            new SendVerificationCodeCommand { Email = "a@test.local" }, default);

        var printed = Assert.Single(_ctx.Codes.Saved).Code;
        Assert.True(await _ctx.Codes.HasPendingCodeAsync(Email.Parse("a@test.local")));
        Assert.True(await _ctx.Codes.ValidateAndConsumeAsync(Email.Parse("a@test.local"), printed));

        Assert.Contains("未配置邮件服务", result.Message);
    }

    [Fact(DisplayName = "已配置但发送失败时抛错_并作废验证码释放冷却")]
    public async Task Send_ConfiguredButSendFails_ThrowsAndReleasesCooldown()
    {
        _ctx.Env.IsDevelopment = false;
        _ctx.Email.IsConfigured = true;
        _ctx.Email.SendSucceeds = false;

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SendVerificationCodeCommand { Email = "a@test.local" }, default));

        Assert.Equal("验证码发送失败，请稍后重试", ex.Message);

        // 这一次确实没有送达渠道：作废验证码，用户才能立刻重试而不是白等 60 秒
        Assert.Contains("a@test.local", _ctx.Codes.Removed);
        Assert.False(await _ctx.Codes.HasPendingCodeAsync(Email.Parse("a@test.local")));
    }

    [Fact(DisplayName = "生产环境SMTP正常时正常返回")]
    public async Task Send_ProdEnvWithSmtp_Succeeds()
    {
        _ctx.Env.IsDevelopment = false;
        _ctx.Email.IsConfigured = true;

        var result = await Handler().Handle(
            new SendVerificationCodeCommand { Email = "a@test.local" }, default);

        Assert.True(result.Success);
        Assert.Null(result.Data!.DevCode);
        Assert.Single(_ctx.Email.Sent);
        Assert.Equal("验证码已发送", result.Message);
    }

    [Fact(DisplayName = "已配置SMTP时不回传devCode")]
    public async Task Send_SmtpConfigured_OmitsDevCode()
    {
        _ctx.Email.IsConfigured = true;

        var result = await Handler().Handle(
            new SendVerificationCodeCommand { Email = "a@test.local" }, default);

        Assert.Null(result.Data!.DevCode);
    }

    [Fact(DisplayName = "邮箱归一化后保存_大小写不影响后续校验")]
    public async Task Send_EmailSavedNormalized_CaseDoesNotAffectValidation()
    {
        await Handler().Handle(new SendVerificationCodeCommand { Email = "  A@Test.LOCAL " }, default);

        Assert.Equal("a@test.local", _ctx.Codes.Saved[0].Email);
    }

    [Fact(DisplayName = "冷却期内重复发送被拒")]
    public async Task Send_WithinCooldown_IsRejected()
    {
        _ctx.Codes.Seed("a@test.local", "123456");

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SendVerificationCodeCommand { Email = "a@test.local" }, default));

        Assert.Equal("验证码已发送，请稍后再试", ex.Message);
        Assert.Empty(_ctx.Email.Sent);
    }

    [Fact(DisplayName = "忘记密码场景要求邮箱已注册")]
    public async Task Send_ForgotPasswordUnregisteredEmail_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SendVerificationCodeCommand
            {
                Email = "nobody@test.local",
                Purpose = VerificationPurposes.Forgot
            }, default));

        Assert.Equal("该邮箱未注册", ex.Message);
    }

    [Fact(DisplayName = "忘记密码场景_邮箱已注册时放行")]
    public async Task Send_ForgotPasswordRegisteredEmail_IsAllowed()
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

    [Fact(DisplayName = "注册场景不校验邮箱是否已注册_由注册用例负责")]
    public async Task Send_RegisterPurpose_SkipsEmailExistenceCheck()
    {
        // 否则「邮箱已注册」会在发码阶段泄露账号存在性
        _ctx.GivenUser(email: "a@test.local");

        var result = await Handler().Handle(
            new SendVerificationCodeCommand { Email = "a@test.local" }, default);

        Assert.True(result.Success);
    }

    [Fact(DisplayName = "邮箱格式非法时抛出")]
    public async Task Send_InvalidEmail_Throws()
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

    [Fact(DisplayName = "注册成功并分配起始10000的账号ID")]
    public async Task Register_Succeeds_AssignsStartingAccountId()
    {
        _ctx.Codes.Seed("a@test.local", "123456");

        var result = await Handler().Handle(ValidCommand(), default);

        Assert.True(result.Success);
        Assert.Equal(10000, result.Data!.AccountId);
        Assert.Contains("10000", result.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "注册后用户已落库且字段正确")]
    public async Task Register_PersistsUser_WithCorrectFields()
    {
        _ctx.Codes.Seed("a@test.local", "123456");

        await Handler().Handle(ValidCommand(), default);

        var user = Assert.Single(_ctx.Users.All);
        Assert.Equal("张三", user.Nickname);
        Assert.Equal("a@test.local", user.Email!.Value);
        Assert.False(user.IsBot);
        Assert.Equal(_ctx.Now, user.CreatedAt);
    }

    [Fact(DisplayName = "口令经哈希器处理后才存储")]
    public async Task Register_PasswordStoredAsHash_NotPlaintext()
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

    [Fact(DisplayName = "注册事件在主键回填之后发出_携带真实账号ID")]
    public async Task Register_RaisesEventAfterIdAssigned_WithRealAccountId()
    {
        // 改造前这里是隐患点：若在 Insert 之前发事件，订阅方拿到的 UserId 是 0
        _ctx.Codes.Seed("a@test.local", "123456");

        await Handler().Handle(ValidCommand(), default);

        var registered = _ctx.Events.SingleEvent<UserRegistered>();
        Assert.Equal(10000, registered.UserId);
        Assert.Equal("张三", registered.Nickname);
    }

    [Fact(DisplayName = "验证码被消费_不能重放")]
    public async Task Register_CodeConsumed_CannotBeReplayed()
    {
        _ctx.Codes.Seed("a@test.local", "123456");
        await Handler().Handle(ValidCommand(), default);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => Handler().Handle(ValidCommand(email: "b@test.local"), default));

        Assert.Equal("验证码错误或已过期", ex.Message);
    }

    [Fact(DisplayName = "验证码错误时抛出且不建用户")]
    public async Task Register_WrongCode_ThrowsWithoutCreatingUser()
    {
        _ctx.Codes.Seed("a@test.local", "123456");

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => Handler().Handle(ValidCommand(code: "999999"), default));

        Assert.Equal("验证码错误或已过期", ex.Message);
        Assert.Empty(_ctx.Users.All);
    }

    [Fact(DisplayName = "邮箱已注册时抛出")]
    public async Task Register_EmailAlreadyRegistered_Throws()
    {
        _ctx.GivenUser(email: "a@test.local");
        _ctx.Codes.Seed("a@test.local", "123456");

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => Handler().Handle(ValidCommand(), default));

        Assert.Equal("该邮箱已注册", ex.Message);
    }

    [Fact(DisplayName = "校验先于验证码消费_格式错误不浪费验证码")]
    public async Task Register_ValidationBeforeCodeConsumption_KeepsCodeUsable()
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

    [Fact(DisplayName = "昵称与邮箱都被归一化")]
    public async Task Register_NicknameAndEmail_AreNormalized()
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

    [Fact(DisplayName = "连续注册的账号ID递增")]
    public async Task Register_SequentialRegistrations_IncrementAccountId()
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
        => new(_ctx.Users, _ctx.Hasher, _ctx.Tokens, _ctx.Sessions, _ctx.Throttle);

    [Fact(DisplayName = "按账号ID登录成功")]
    public async Task Login_ByAccountId_Succeeds()
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

    [Fact(DisplayName = "按邮箱登录成功")]
    public async Task Login_ByEmail_Succeeds()
    {
        var user = _ctx.GivenUser(email: "a@test.local");

        var result = await Handler().Handle(
            new LoginCommand { Account = "a@test.local", Password = "pass123456" }, default);

        Assert.True(result.Success);
        Assert.Equal(user.Id, result.Data!.User.Id);
    }

    [Fact(DisplayName = "邮箱大小写不敏感")]
    public async Task Login_EmailCaseInsensitive_Succeeds()
    {
        _ctx.GivenUser(email: "a@test.local");

        var result = await Handler().Handle(
            new LoginCommand { Account = "  A@TEST.LOCAL ", Password = "pass123456" }, default);

        Assert.True(result.Success);
    }

    [Fact(DisplayName = "登录后创建会话并存储刷新令牌")]
    public async Task Login_CreatesSession_StoresRefreshToken()
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

    [Fact(DisplayName = "未传设备名时记为未知设备")]
    public async Task Login_MissingDeviceName_RecordsUnknownDevice()
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

    [Fact(DisplayName = "多端登录各自独立会话")]
    public async Task Login_MultipleDevices_GetIndependentSessions()
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

    [Fact(DisplayName = "口令错误时统一提示语")]
    public async Task Login_WrongPassword_ShowsGenericMessage()
    {
        var user = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(new LoginCommand
        {
            Account = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Password = "wrong"
        }, default));

        Assert.Equal("账号或密码错误", ex.Message);
    }

    [Fact(DisplayName = "账号不存在时与口令错误同一提示语_不泄露存在性")]
    public async Task Login_UnknownAccount_SameMessageAsWrongPassword()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new LoginCommand { Account = "99999999", Password = "pass123456" }, default));

        Assert.Equal("账号或密码错误", ex.Message);
    }

    [Fact(DisplayName = "邮箱格式非法时也走统一提示语")]
    public async Task Login_MalformedEmail_ShowsGenericMessage()
    {
        // 登录输入既可能是账号也可能是邮箱，不该抛「邮箱格式不正确」
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new LoginCommand { Account = "garbage-input", Password = "pass123456" }, default));

        Assert.Equal("账号或密码错误", ex.Message);
    }

    [Theory(DisplayName = "账号或口令为空时提示补全")]
    [InlineData("", "pass123456")]
    [InlineData("   ", "pass123456")]
    [InlineData("10000", "")]
    [InlineData("10000", "   ")]
    public async Task Login_EmptyAccountOrPassword_PromptsForInput(string account, string password)
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new LoginCommand { Account = account, Password = password }, default));

        Assert.Equal("请输入账号和密码", ex.Message);
    }

    [Fact(DisplayName = "机器人账号无法登录_且提示与口令错误一致不泄露账号类型")]
    public async Task Login_BotAccount_RejectedLikeWrongPassword()
    {
        var (_, botUser) = _ctx.GivenRobot(ownerId: 10001);

        // 机器人账号没有口令哈希，Verify 必然失败，因此走不到「机器人账号不能登录」那条
        // 领域规则 —— 对外表现必须与「账号不存在」「口令错误」完全一致，
        // 否则拿账号 ID 逐个试就能区分出哪些是机器人账号。
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(new LoginCommand
        {
            Account = botUser.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Password = "anything"
        }, default));

        Assert.Equal("账号或密码错误", ex.Message);
    }

    [Fact(DisplayName = "封禁账号登录被拒且提示封禁原因")]
    public async Task Login_BannedAccount_RejectedWithBanReason()
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

    [Fact(DisplayName = "口令错误时不泄露账号是否被封禁")]
    public async Task Login_WrongPassword_DoesNotRevealBanStatus()
    {
        var user = _ctx.GivenUser();
        user.Ban("封禁中", _ctx.Now);
        await _ctx.Users.UpdateAsync(user);

        // 口令校验先于账号状态校验：口令不对时连「被封禁」都不该透露，
        // 否则任何人都能用错误口令探测账号状态，统一 BadCredentials 的设计就被抵消了。
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(new LoginCommand
        {
            Account = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Password = "wrong-password"
        }, default));

        Assert.Equal("账号或密码错误", ex.Message);
    }

    [Fact(DisplayName = "口令错误时记一次限流失败_登录成功后账号维度清零")]
    public async Task Login_WrongPassword_RecordsThrottleFailureThenResets()
    {
        var user = _ctx.GivenUser();

        await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(new LoginCommand
        {
            Account = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Password = "wrong"
        }, default));

        Assert.Single(_ctx.Throttle.RecordedFailures);
        Assert.Empty(_ctx.Throttle.Resets);

        await Handler().Handle(new LoginCommand
        {
            Account = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Password = "pass123456"
        }, default);

        Assert.Single(_ctx.Throttle.Resets);
    }

    [Fact(DisplayName = "限流锁定期内正确口令也被拒绝")]
    public async Task Login_DuringLockout_CorrectPasswordRejected()
    {
        var user = _ctx.GivenUser();
        _ctx.Throttle.LockoutSeconds = 600;

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(new LoginCommand
        {
            Account = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Password = "pass123456"
        }, default));

        Assert.Contains("登录失败次数过多", ex.Message);
        Assert.Empty(await _ctx.Sessions.ListSessionIdsAsync(user.Id));
    }

    [Fact(DisplayName = "登录失败时不创建会话")]
    public async Task Login_Failure_DoesNotCreateSession()
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
        var login = await new LoginHandler(_ctx.Users, _ctx.Hasher, _ctx.Tokens, _ctx.Sessions, _ctx.Throttle)
            .Handle(new LoginCommand
            {
                Account = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Password = "pass123456"
            }, default);
        return (user, login.Data!.RefreshToken);
    }

    [Fact(DisplayName = "刷新成功并返回新令牌")]
    public async Task Refresh_Succeeds_ReturnsNewToken()
    {
        var (user, refreshToken) = await GivenLoggedInAsync();

        var result = await Handler().Handle(
            new RefreshTokenCommand { RefreshToken = refreshToken }, default);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Data!.Token);
        Assert.Equal(user.Id, result.Data.User.Id);
    }

    [Fact(DisplayName = "刷新令牌被轮换")]
    public async Task Refresh_TokenIsRotated()
    {
        var (_, refreshToken) = await GivenLoggedInAsync();

        var result = await Handler().Handle(
            new RefreshTokenCommand { RefreshToken = refreshToken }, default);

        Assert.NotEqual(refreshToken, result.Data!.RefreshToken);
    }

    [Fact(DisplayName = "旧刷新令牌轮换后立即失效_防复用")]
    public async Task Refresh_OldTokenAfterRotation_IsRejected()
    {
        var (_, refreshToken) = await GivenLoggedInAsync();
        await Handler().Handle(new RefreshTokenCommand { RefreshToken = refreshToken }, default);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new RefreshTokenCommand { RefreshToken = refreshToken }, default));

        Assert.Equal("RefreshToken 无效或已过期", ex.Message);
    }

    [Fact(DisplayName = "刷新不改变会话ID_多端互不影响")]
    public async Task Refresh_KeepsSessionId_MultiDeviceUnaffected()
    {
        var (user, refreshToken) = await GivenLoggedInAsync();
        var sessionBefore = Assert.Single(await _ctx.Sessions.ListSessionIdsAsync(user.Id));

        await Handler().Handle(new RefreshTokenCommand { RefreshToken = refreshToken }, default);

        var sessionAfter = Assert.Single(await _ctx.Sessions.ListSessionIdsAsync(user.Id));
        Assert.Equal(sessionBefore, sessionAfter);
    }

    [Fact(DisplayName = "刷新时更新最后活跃时间")]
    public async Task Refresh_UpdatesLastActiveTime()
    {
        var (user, refreshToken) = await GivenLoggedInAsync();
        var sessionId = Assert.Single(await _ctx.Sessions.ListSessionIdsAsync(user.Id));

        await Handler().Handle(new RefreshTokenCommand { RefreshToken = refreshToken }, default);

        Assert.Contains(sessionId, _ctx.Sessions.TouchedSessions);
    }

    [Theory(DisplayName = "令牌为空时提示不能为空")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Refresh_EmptyToken_IsRejected(string token)
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new RefreshTokenCommand { RefreshToken = token }, default));

        Assert.Equal("RefreshToken 不能为空", ex.Message);
    }

    [Fact(DisplayName = "未知令牌被拒")]
    public async Task Refresh_UnknownToken_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new RefreshTokenCommand { RefreshToken = "never-issued" }, default));

        Assert.Equal("RefreshToken 无效或已过期", ex.Message);
    }

    [Fact(DisplayName = "用户已被删除时抛出")]
    public async Task Refresh_DeletedUser_Throws()
    {
        var (user, refreshToken) = await GivenLoggedInAsync();
        await _ctx.Users.DeleteAsync(user.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new RefreshTokenCommand { RefreshToken = refreshToken }, default));

        Assert.Equal("用户不存在", ex.Message);
    }
}
