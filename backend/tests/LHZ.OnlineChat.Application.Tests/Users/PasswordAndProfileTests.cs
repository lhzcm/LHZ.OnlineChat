using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Tests.TestDoubles;
using LHZ.OnlineChat.Application.Users.Commands;
using LHZ.OnlineChat.Application.Users.EventHandlers;
using LHZ.OnlineChat.Application.Users.Queries;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;

namespace LHZ.OnlineChat.Application.Tests.Users;

public class ChangePasswordTests
{
    private readonly ApplicationTestContext _ctx = new();

    private ChangePasswordHandler Handler()
        => new(_ctx.Users, _ctx.Hasher, _ctx.Events, _ctx.Clock);

    [Fact(DisplayName = "改密成功并提示其他设备已下线")]
    public async Task ChangePassword_Succeeds_NotifiesOtherDevicesOffline()
    {
        var user = _ctx.GivenUser();

        var result = await Handler().Handle(new ChangePasswordCommand
        {
            UserId = user.Id, OldPassword = "pass123456", NewPassword = "newpass123"
        }, default);

        Assert.True(result.Success);
        Assert.Equal("密码修改成功，其他设备已下线，请重新登录", result.Message);
    }

    [Fact(DisplayName = "新口令生效_旧口令失效")]
    public async Task ChangePassword_NewPasswordWorks_OldPasswordRejected()
    {
        var user = _ctx.GivenUser();

        await Handler().Handle(new ChangePasswordCommand
        {
            UserId = user.Id, OldPassword = "pass123456", NewPassword = "newpass123"
        }, default);

        var stored = await _ctx.Users.FindByIdAsync(user.Id);
        Assert.True(_ctx.Hasher.Verify("newpass123", stored!.PasswordHash));
        Assert.False(_ctx.Hasher.Verify("pass123456", stored.PasswordHash));
    }

    [Fact(DisplayName = "发出自助改密事件")]
    public async Task ChangePassword_EmitsSelfServiceChangedEvent()
    {
        var user = _ctx.GivenUser();

        await Handler().Handle(new ChangePasswordCommand
        {
            UserId = user.Id, OldPassword = "pass123456", NewPassword = "newpass123"
        }, default);

        var e = _ctx.Events.SingleEvent<UserPasswordChanged>();
        Assert.Equal(PasswordChangeReason.SelfService, e.Reason);
        Assert.Equal(user.Id, e.UserId);
    }

    [Fact(DisplayName = "原口令错误时抛出且不改动")]
    public async Task ChangePassword_WrongOldPassword_ThrowsWithoutChanges()
    {
        var user = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new ChangePasswordCommand
            {
                UserId = user.Id, OldPassword = "wrong", NewPassword = "newpass123"
            }, default));

        Assert.Equal("原密码错误", ex.Message);
        var stored = await _ctx.Users.FindByIdAsync(user.Id);
        Assert.True(_ctx.Hasher.Verify("pass123456", stored!.PasswordHash));
        Assert.Empty(_ctx.Events.Dispatched);
    }

    [Fact(DisplayName = "新口令过短时抛出且不触碰仓储")]
    public async Task ChangePassword_TooShortNewPassword_ThrowsAndSkipsRepository()
    {
        var user = _ctx.GivenUser();
        var updatesBefore = _ctx.Users.UpdateCount;

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new ChangePasswordCommand
            {
                UserId = user.Id, OldPassword = "pass123456", NewPassword = "123"
            }, default));

        Assert.Equal("密码长度不能少于 6 个字符", ex.Message);
        Assert.Equal(updatesBefore, _ctx.Users.UpdateCount);
    }

    [Fact(DisplayName = "用户不存在时抛出EntityNotFound")]
    public async Task ChangePassword_UserNotFound_ThrowsEntityNotFound()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(() => Handler().Handle(
            new ChangePasswordCommand
            {
                UserId = 99999, OldPassword = "pass123456", NewPassword = "newpass123"
            }, default));
    }
}

public class ForgotPasswordTests
{
    private readonly ApplicationTestContext _ctx = new();

    private ForgotPasswordHandler Handler()
        => new(_ctx.Users, _ctx.Codes, _ctx.Hasher, _ctx.Events, _ctx.Clock);

    [Fact(DisplayName = "重置成功")]
    public async Task ForgotPassword_ValidCode_ResetsPassword()
    {
        var user = _ctx.GivenUser(email: "a@test.local");
        _ctx.Codes.Seed("a@test.local", "123456");

        var result = await Handler().Handle(new ForgotPasswordCommand
        {
            Email = "a@test.local", Code = "123456", NewPassword = "reset123456"
        }, default);

        Assert.True(result.Success);
        Assert.Equal("密码重置成功，请使用新密码登录", result.Message);

        var stored = await _ctx.Users.FindByIdAsync(user.Id);
        Assert.True(_ctx.Hasher.Verify("reset123456", stored!.PasswordHash));
    }

    [Fact(DisplayName = "发出忘记密码原因的事件")]
    public async Task ForgotPassword_EmitsForgotPasswordReasonEvent()
    {
        _ctx.GivenUser(email: "a@test.local");
        _ctx.Codes.Seed("a@test.local", "123456");

        await Handler().Handle(new ForgotPasswordCommand
        {
            Email = "a@test.local", Code = "123456", NewPassword = "reset123456"
        }, default);

        Assert.Equal(
            PasswordChangeReason.ForgotPassword,
            _ctx.Events.SingleEvent<UserPasswordChanged>().Reason);
    }

    [Fact(DisplayName = "邮箱未注册时抛出")]
    public async Task ForgotPassword_UnregisteredEmail_Throws()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new ForgotPasswordCommand
            {
                Email = "nobody@test.local", Code = "123456", NewPassword = "reset123456"
            }, default));

        Assert.Equal("该邮箱未注册", ex.Message);
    }

    [Fact(DisplayName = "验证码错误时抛出")]
    public async Task ForgotPassword_WrongCode_Throws()
    {
        _ctx.GivenUser(email: "a@test.local");
        _ctx.Codes.Seed("a@test.local", "123456");

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new ForgotPasswordCommand
            {
                Email = "a@test.local", Code = "999999", NewPassword = "reset123456"
            }, default));

        Assert.Equal("验证码错误或已过期", ex.Message);
    }

    [Fact(DisplayName = "验证码一次性")]
    public async Task ForgotPassword_Code_IsSingleUse()
    {
        _ctx.GivenUser(email: "a@test.local");
        _ctx.Codes.Seed("a@test.local", "123456");
        var command = new ForgotPasswordCommand
        {
            Email = "a@test.local", Code = "123456", NewPassword = "reset123456"
        };

        await Handler().Handle(command, default);

        await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(command, default));
    }
}

/// <summary>
/// 口令变更 → 终止全部会话。
/// 这是改造的核心收益之一：三条改密路径共用同一个事件订阅方，行为必然一致。
/// </summary>
public class PasswordChangeTerminatesSessionsTests
{
    private readonly ApplicationTestContext _ctx = new();

    private ApplicationTestContext WithSubscriber()
    {
        _ctx.Events.Subscribe(new TerminateSessionsOnPasswordChanged(
            _ctx.Terminator, NullLogger<TerminateSessionsOnPasswordChanged>.Instance));
        return _ctx;
    }

    [Fact(DisplayName = "自助改密后全部会话被终止")]
    public async Task ChangePassword_TerminatesAllSessions()
    {
        WithSubscriber();
        var user = _ctx.GivenUser();
        await _ctx.Sessions.SeedSessionAsync(user.Id, "s1", "手机");
        await _ctx.Sessions.SeedSessionAsync(user.Id, "s2", "电脑");

        await new ChangePasswordHandler(_ctx.Users, _ctx.Hasher, _ctx.Events, _ctx.Clock)
            .Handle(new ChangePasswordCommand
            {
                UserId = user.Id, OldPassword = "pass123456", NewPassword = "newpass123"
            }, default);

        Assert.Contains(user.Id, _ctx.Terminator.TerminatedAllFor);
        Assert.Empty(await _ctx.Sessions.ListSessionIdsAsync(user.Id));
    }

    [Fact(DisplayName = "忘记密码重置后全部会话被终止")]
    public async Task ForgotPassword_TerminatesAllSessions()
    {
        WithSubscriber();
        var user = _ctx.GivenUser(email: "a@test.local");
        await _ctx.Sessions.SeedSessionAsync(user.Id, "s1");
        _ctx.Codes.Seed("a@test.local", "123456");

        await new ForgotPasswordHandler(_ctx.Users, _ctx.Codes, _ctx.Hasher, _ctx.Events, _ctx.Clock)
            .Handle(new ForgotPasswordCommand
            {
                Email = "a@test.local", Code = "123456", NewPassword = "reset123456"
            }, default);

        Assert.Contains(user.Id, _ctx.Terminator.TerminatedAllFor);
    }

    [Fact(DisplayName = "管理员重置后全部会话被终止")]
    public async Task AdminReset_TerminatesAllSessions()
    {
        // 第三条路径走的是管理后台用例，但事件与订阅方完全相同
        WithSubscriber();
        var user = _ctx.GivenUser();
        await _ctx.Sessions.SeedSessionAsync(user.Id, "s1");

        await new LHZ.OnlineChat.Application.Admins.Commands.ResetUserPasswordHandler(
                _ctx.Users, _ctx.Hasher, _ctx.Events, _ctx.Audit, _ctx.Clock)
            .Handle(new LHZ.OnlineChat.Application.Admins.Commands.ResetUserPasswordCommand
            {
                AdminId = 1, UserId = user.Id, NewPassword = "adminset123"
            }, default);

        Assert.Contains(user.Id, _ctx.Terminator.TerminatedAllFor);
        Assert.Equal(
            PasswordChangeReason.AdminReset,
            _ctx.Events.SingleEvent<UserPasswordChanged>().Reason);
    }
}

public class UpdateProfileTests
{
    private readonly ApplicationTestContext _ctx = new();

    [Fact(DisplayName = "改昵称成功")]
    public async Task UpdateNickname_Succeeds()
    {
        var user = _ctx.GivenUser();

        var result = await new UpdateNicknameHandler(_ctx.Users, _ctx.Clock).Handle(
            new UpdateNicknameCommand { UserId = user.Id, Nickname = "  新名字 " }, default);

        Assert.True(result.Success);
        Assert.Equal("昵称修改成功", result.Message);
        Assert.Equal("新名字", (await _ctx.Users.FindByIdAsync(user.Id))!.Nickname);
    }

    [Fact(DisplayName = "昵称为空时抛出")]
    public async Task UpdateNickname_EmptyNickname_Throws()
    {
        var user = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new UpdateNicknameHandler(_ctx.Users, _ctx.Clock).Handle(
                new UpdateNicknameCommand { UserId = user.Id, Nickname = "  " }, default));

        Assert.Equal("昵称不能为空", ex.Message);
    }

    [Fact(DisplayName = "换绑邮箱成功并发出事件")]
    public async Task ChangeEmail_Succeeds_EmitsEvent()
    {
        var user = _ctx.GivenUser(email: "old@test.local");
        _ctx.Codes.Seed("new@test.local", "123456");

        var result = await new ChangeEmailHandler(_ctx.Users, _ctx.Codes, _ctx.Events, _ctx.Clock)
            .Handle(new ChangeEmailCommand
            {
                UserId = user.Id, NewEmail = "new@test.local", Code = "123456"
            }, default);

        Assert.True(result.Success);
        Assert.Equal("new@test.local", (await _ctx.Users.FindByIdAsync(user.Id))!.Email!.Value);
        Assert.Equal("new@test.local", _ctx.Events.SingleEvent<UserEmailChanged>().NewEmail);
    }

    [Fact(DisplayName = "换绑到已被占用的邮箱时抛出")]
    public async Task ChangeEmail_EmailTaken_Throws()
    {
        _ctx.GivenUser(nickname: "别人", email: "taken@test.local");
        var user = _ctx.GivenUser(email: "old@test.local");
        _ctx.Codes.Seed("taken@test.local", "123456");

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new ChangeEmailHandler(_ctx.Users, _ctx.Codes, _ctx.Events, _ctx.Clock)
                .Handle(new ChangeEmailCommand
                {
                    UserId = user.Id, NewEmail = "taken@test.local", Code = "123456"
                }, default));

        Assert.Equal("该邮箱已被其他账号绑定", ex.Message);
    }

    [Fact(DisplayName = "换绑到自己当前邮箱时允许_排除自身占用")]
    public async Task ChangeEmail_OwnCurrentEmail_IsAllowed()
    {
        var user = _ctx.GivenUser(email: "same@test.local");
        _ctx.Codes.Seed("same@test.local", "123456");

        var result = await new ChangeEmailHandler(_ctx.Users, _ctx.Codes, _ctx.Events, _ctx.Clock)
            .Handle(new ChangeEmailCommand
            {
                UserId = user.Id, NewEmail = "same@test.local", Code = "123456"
            }, default);

        Assert.True(result.Success);
    }

    [Fact(DisplayName = "换绑验证码错误时抛出")]
    public async Task ChangeEmail_WrongCode_Throws()
    {
        var user = _ctx.GivenUser();
        _ctx.Codes.Seed("new@test.local", "123456");

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new ChangeEmailHandler(_ctx.Users, _ctx.Codes, _ctx.Events, _ctx.Clock)
                .Handle(new ChangeEmailCommand
                {
                    UserId = user.Id, NewEmail = "new@test.local", Code = "000000"
                }, default));

        Assert.Equal("验证码错误或已过期", ex.Message);
    }

    [Fact(DisplayName = "上传头像成功并更新用户")]
    public async Task UploadAvatar_Succeeds_UpdatesUser()
    {
        var user = _ctx.GivenUser();

        var result = await new UploadAvatarHandler(_ctx.Users, _ctx.Files, _ctx.Clock).Handle(
            new UploadAvatarCommand { UserId = user.Id, File = Upload("a.png", 1024) }, default);

        Assert.True(result.Success);
        Assert.Equal("/uploads/stored.png", result.Data!.Avatar);
        Assert.Equal("/uploads/stored.png", (await _ctx.Users.FindByIdAsync(user.Id))!.Avatar);
    }

    [Fact(DisplayName = "头像超过2MB时被拒")]
    public async Task UploadAvatar_Exceeds2Mb_IsRejected()
    {
        var user = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new UploadAvatarHandler(_ctx.Users, _ctx.Files, _ctx.Clock).Handle(
                new UploadAvatarCommand
                {
                    UserId = user.Id, File = Upload("a.png", UploadRules.MaxAvatarBytes + 1)
                }, default));

        Assert.Equal("图片大小不能超过 2MB", ex.Message);
        Assert.Empty(_ctx.Files.Saved);
    }

    [Theory(DisplayName = "非白名单扩展名被拒")]
    [InlineData("a.bmp")]
    [InlineData("a.svg")]
    [InlineData("a.exe")]
    [InlineData("a")]
    public async Task UploadAvatar_NonWhitelistedExtension_IsRejected(string fileName)
    {
        var user = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new UploadAvatarHandler(_ctx.Users, _ctx.Files, _ctx.Clock).Handle(
                new UploadAvatarCommand { UserId = user.Id, File = Upload(fileName, 100) }, default));

        Assert.Equal("仅支持 jpg / png / gif / webp 格式图片", ex.Message);
    }

    [Fact(DisplayName = "未选文件时被拒")]
    public async Task UploadAvatar_NoFile_IsRejected()
    {
        var user = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new UploadAvatarHandler(_ctx.Users, _ctx.Files, _ctx.Clock).Handle(
                new UploadAvatarCommand { UserId = user.Id, File = null }, default));

        Assert.Equal("请选择图片文件", ex.Message);
    }

    [Fact(DisplayName = "聊天图片上限为5MB且存到images子目录")]
    public async Task UploadChatImage_UpTo5Mb_StoredInImagesSubdirectory()
    {
        var result = await new UploadChatImageHandler(_ctx.Files).Handle(
            new UploadChatImageCommand { File = Upload("chat.jpg", 4 * 1024 * 1024) }, default);

        Assert.True(result.Success);
        Assert.Equal("/uploads/images/stored.jpg", result.Data!.Url);
        Assert.Equal("images", _ctx.Files.Saved[0].Subdirectory);
    }

    [Fact(DisplayName = "聊天图片超过5MB被拒")]
    public async Task UploadChatImage_Exceeds5Mb_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new UploadChatImageHandler(_ctx.Files).Handle(
                new UploadChatImageCommand
                {
                    File = Upload("chat.jpg", UploadRules.MaxChatImageBytes + 1)
                }, default));

        Assert.Equal("图片大小不能超过 5MB", ex.Message);
    }

    [Fact(DisplayName = "语音上传存到voices子目录")]
    public async Task UploadChatVoice_StoredInVoicesSubdirectory()
    {
        var result = await new UploadChatVoiceHandler(_ctx.Files).Handle(
            new UploadChatVoiceCommand { File = Upload("voice.webm", 64 * 1024) }, default);

        Assert.True(result.Success);
        Assert.Equal("voices", _ctx.Files.Saved[0].Subdirectory);
        Assert.StartsWith("/uploads/voices/", result.Data!.Url, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "语音超过10MB被拒")]
    public async Task UploadChatVoice_Exceeds10Mb_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new UploadChatVoiceHandler(_ctx.Files).Handle(
                new UploadChatVoiceCommand { File = Upload("voice.webm", UploadRules.MaxVoiceBytes + 1) },
                default));

        Assert.Equal("语音大小不能超过 10MB", ex.Message);
    }

    private static FileUpload Upload(string fileName, long length)
        => new() { FileName = fileName, Length = length, Content = new MemoryStream(new byte[1]) };
}

public class UserQueryTests
{
    private readonly ApplicationTestContext _ctx = new();

    [Fact(DisplayName = "取当前用户信息")]
    public async Task GetCurrentUser_ReturnsProfile()
    {
        var user = _ctx.GivenUser(nickname: "张三", email: "a@test.local");

        var result = await new GetCurrentUserHandler(_ctx.Users).Handle(
            new GetCurrentUserQuery { UserId = user.Id }, default);

        Assert.True(result.Success);
        Assert.Equal(user.Id, result.Data!.Id);
        Assert.Equal("张三", result.Data.Nickname);
        Assert.Equal("a@test.local", result.Data.Email);
    }

    [Fact(DisplayName = "无效Token时抛出")]
    public async Task GetCurrentUser_InvalidToken_Throws()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new GetCurrentUserHandler(_ctx.Users).Handle(
                new GetCurrentUserQuery { UserId = 0 }, default));

        Assert.Equal("无效的 Token", ex.Message);
    }

    [Fact(DisplayName = "用户不存在时抛出")]
    public async Task GetCurrentUser_UserNotFound_Throws()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => new GetCurrentUserHandler(_ctx.Users).Handle(
                new GetCurrentUserQuery { UserId = 99999 }, default));
    }

    [Fact(DisplayName = "机器人账号的邮箱为null")]
    public async Task GetCurrentUser_BotAccount_EmailIsNull()
    {
        var (_, botUser) = _ctx.GivenRobot(ownerId: 10001);

        var result = await new GetCurrentUserHandler(_ctx.Users).Handle(
            new GetCurrentUserQuery { UserId = botUser.Id }, default);

        Assert.Null(result.Data!.Email);
    }

    [Fact(DisplayName = "设备列表按最后活跃倒序并标记当前设备")]
    public async Task GetLoginSessions_OrderedByLastActiveDesc_MarksCurrent()
    {
        var user = _ctx.GivenUser();
        await _ctx.Sessions.SeedSessionAsync(user.Id, "s1", "手机");
        await _ctx.Sessions.SeedSessionAsync(user.Id, "s2", "电脑");

        var result = await new GetLoginSessionsHandler(_ctx.Sessions).Handle(
            new GetLoginSessionsQuery { UserId = user.Id, CurrentSessionId = "s2" }, default);

        Assert.Equal(2, result.Data!.Count);
        Assert.Single(result.Data, s => s.IsCurrent);
        Assert.Equal("s2", result.Data.Single(s => s.IsCurrent).SessionId);
    }

    [Fact(DisplayName = "无会话时返回空列表")]
    public async Task GetLoginSessions_NoSessions_ReturnsEmptyList()
    {
        var result = await new GetLoginSessionsHandler(_ctx.Sessions).Handle(
            new GetLoginSessionsQuery { UserId = 10001, CurrentSessionId = "x" }, default);

        Assert.True(result.Success);
        Assert.Empty(result.Data!);
    }
}

public class ManageSessionsTests
{
    private readonly ApplicationTestContext _ctx = new();

    [Fact(DisplayName = "踢下线指定设备")]
    public async Task KickSession_KicksTargetDevice()
    {
        var user = _ctx.GivenUser();
        await _ctx.Sessions.SeedSessionAsync(user.Id, "s1");

        var result = await new KickSessionHandler(_ctx.Sessions, _ctx.Terminator).Handle(
            new KickSessionCommand { UserId = user.Id, SessionId = "s1" }, default);

        Assert.Equal("该设备已下线", result.Message);
        Assert.Contains((user.Id, "s1"), _ctx.Terminator.Terminated);
    }

    [Fact(DisplayName = "不能踢别人的会话")]
    public async Task KickSession_OtherUsersSession_IsRejected()
    {
        var owner = _ctx.GivenUser();
        var other = _ctx.GivenUser(nickname: "别人", email: "b@test.local");
        await _ctx.Sessions.SeedSessionAsync(other.Id, "other-session");

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new KickSessionHandler(_ctx.Sessions, _ctx.Terminator).Handle(
                new KickSessionCommand { UserId = owner.Id, SessionId = "other-session" }, default));

        Assert.Equal("会话不存在", ex.Message);
        Assert.Empty(_ctx.Terminator.Terminated);
    }

    [Fact(DisplayName = "退出其他设备_保留当前设备")]
    public async Task LogoutOtherSessions_KeepsCurrentSession()
    {
        var user = _ctx.GivenUser();
        await _ctx.Sessions.SeedSessionAsync(user.Id, "current");
        await _ctx.Sessions.SeedSessionAsync(user.Id, "other1");
        await _ctx.Sessions.SeedSessionAsync(user.Id, "other2");

        var result = await new LogoutOtherSessionsHandler(_ctx.Terminator).Handle(
            new LogoutOtherSessionsCommand { UserId = user.Id, CurrentSessionId = "current" },
            default);

        Assert.Equal("已退出 2 台设备", result.Message);
        Assert.Equal(new[] { "current" }, await _ctx.Sessions.ListSessionIdsAsync(user.Id));
    }

    [Fact(DisplayName = "没有其他设备时给出相应提示")]
    public async Task LogoutOtherSessions_NoOtherDevices_ReturnsNotice()
    {
        var user = _ctx.GivenUser();
        await _ctx.Sessions.SeedSessionAsync(user.Id, "current");

        var result = await new LogoutOtherSessionsHandler(_ctx.Terminator).Handle(
            new LogoutOtherSessionsCommand { UserId = user.Id, CurrentSessionId = "current" },
            default);

        Assert.Equal("没有其他在线设备", result.Message);
    }
}
