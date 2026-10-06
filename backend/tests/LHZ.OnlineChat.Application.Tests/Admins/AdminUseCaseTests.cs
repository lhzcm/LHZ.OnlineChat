using LHZ.OnlineChat.Application.Admins.Commands;
using LHZ.OnlineChat.Application.Admins.Queries;
using LHZ.OnlineChat.Application.Tests.TestDoubles;
using LHZ.OnlineChat.Application.Users.EventHandlers;
using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;

namespace LHZ.OnlineChat.Application.Tests.Admins;

public class AdminAuthTests
{
    private readonly ApplicationTestContext _ctx = new();

    private AdminLoginHandler Handler()
        => new(_ctx.Admins, _ctx.Hasher, _ctx.Tokens, _ctx.AdminSessions, _ctx.Throttle, _ctx.Clock);

    [Fact]
    public async Task 登录成功并返回角色()
    {
        _ctx.GivenAdmin("admin", AdminRole.Super);

        var result = await Handler().Handle(
            new AdminLoginCommand { Username = "admin", Password = "admin123456" }, default);

        Assert.True(result.Success);
        Assert.Equal("登录成功", result.Message);
        Assert.Equal(0, result.Data!.Admin.Role);
        Assert.NotEmpty(result.Data.Token);

        // 登录必须登记会话，否则令牌无法吊销（停用/删除要等过期才生效）
        var sessionId = Assert.Single(_ctx.AdminSessions.Created);
        Assert.True(await _ctx.AdminSessions.IsValidAsync(sessionId));
    }

    [Fact]
    public async Task 登录时记录最后登录时间()
    {
        var admin = _ctx.GivenAdmin();

        await Handler().Handle(
            new AdminLoginCommand { Username = "admin", Password = "admin123456" }, default);

        Assert.Equal(_ctx.Now, (await _ctx.Admins.FindByIdAsync(admin.Id))!.LastLoginAt);
    }

    [Fact]
    public async Task 口令错误时统一提示语()
    {
        _ctx.GivenAdmin();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new AdminLoginCommand { Username = "admin", Password = "wrong" }, default));

        Assert.Equal("账号或密码错误", ex.Message);
    }

    [Fact]
    public async Task 账号不存在时与口令错误同一提示语()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new AdminLoginCommand { Username = "nobody", Password = "admin123456" }, default));

        Assert.Equal("账号或密码错误", ex.Message);
    }

    [Fact]
    public async Task 停用的管理员不能登录()
    {
        var admin = _ctx.GivenAdmin();
        admin.ChangeRoleAndStatus(null, AdminStatus.Disabled, operatorId: 9999);
        await _ctx.Admins.UpdateAsync(admin);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new AdminLoginCommand { Username = "admin", Password = "admin123456" }, default));

        Assert.Equal("该管理员账号已停用", ex.Message);
    }

    [Theory]
    [InlineData("", "admin123456")]
    [InlineData("   ", "admin123456")]
    [InlineData("admin", "")]
    public async Task 账号或口令为空时提示补全(string username, string password)
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new AdminLoginCommand { Username = username, Password = password }, default));

        Assert.Equal("请输入账号和密码", ex.Message);
    }

    [Fact]
    public async Task 取当前管理员信息()
    {
        var admin = _ctx.GivenAdmin("ops", AdminRole.Operator);

        var result = await new GetAdminProfileHandler(_ctx.Admins).Handle(
            new GetAdminProfileQuery { AdminId = admin.Id }, default);

        Assert.Equal("ops", result.Data!.Username);
        Assert.Equal(1, result.Data.Role);
        Assert.Equal(1, result.Data.Status);
    }

    [Fact]
    public async Task 管理员改密成功_并吊销全部会话迫使用新口令重新登录()
    {
        var admin = _ctx.GivenAdmin();
        await _ctx.AdminSessions.CreateAsync(admin.Id, "sess-a");
        await _ctx.AdminSessions.CreateAsync(admin.Id, "sess-b");

        var result = await new ChangeAdminPasswordHandler(_ctx.Admins, _ctx.Hasher, _ctx.AdminSessions).Handle(
            new ChangeAdminPasswordCommand
            {
                AdminId = admin.Id, OldPassword = "admin123456", NewPassword = "newadmin123"
            }, default);

        Assert.Equal("密码修改成功，请重新登录", result.Message);
        Assert.True(_ctx.Hasher.Verify(
            "newadmin123", (await _ctx.Admins.FindByIdAsync(admin.Id))!.PasswordHash));

        // 改密的动机通常就是「怀疑口令泄露」，旧令牌必须立刻不能再用
        Assert.Contains(admin.Id, _ctx.AdminSessions.RevokedAdmins);
        Assert.False(await _ctx.AdminSessions.IsValidAsync("sess-a"));
        Assert.False(await _ctx.AdminSessions.IsValidAsync("sess-b"));
    }

    [Fact]
    public async Task 管理员改密_原口令错误时被拒()
    {
        var admin = _ctx.GivenAdmin();

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new ChangeAdminPasswordHandler(_ctx.Admins, _ctx.Hasher, _ctx.AdminSessions).Handle(
                new ChangeAdminPasswordCommand
                {
                    AdminId = admin.Id, OldPassword = "wrong", NewPassword = "newadmin123"
                }, default));

        Assert.Equal("原密码错误", ex.Message);
    }
}

public class ManageAdminsTests
{
    private readonly ApplicationTestContext _ctx = new();

    private CreateAdminHandler CreateHandler()
        => new(_ctx.Admins, _ctx.Hasher, _ctx.Audit, _ctx.Clock);

    [Fact]
    public async Task 创建运营管理员并留审计()
    {
        var superAdmin = _ctx.GivenAdmin();

        var result = await CreateHandler().Handle(new CreateAdminCommand
        {
            OperatorId = superAdmin.Id, Username = "ops01", Password = "ops123456", Role = 1
        }, default);

        Assert.Equal("管理员已创建", result.Message);
        Assert.Contains(_ctx.Admins.All, a => a.Username == "ops01" && a.Role == AdminRole.Operator);
        Assert.True(_ctx.Audit.Has(AuditActions.AdminCreate));
    }

    [Fact]
    public async Task 账号重复时被拒()
    {
        var superAdmin = _ctx.GivenAdmin("admin");

        var ex = await Assert.ThrowsAsync<DomainException>(() => CreateHandler().Handle(
            new CreateAdminCommand
            {
                OperatorId = superAdmin.Id, Username = "admin", Password = "x123456", Role = 1
            }, default));

        Assert.Equal("该管理员账号已存在", ex.Message);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(99)]
    public async Task 无效角色被拒(int role)
    {
        var superAdmin = _ctx.GivenAdmin();

        var ex = await Assert.ThrowsAsync<DomainException>(() => CreateHandler().Handle(
            new CreateAdminCommand
            {
                OperatorId = superAdmin.Id, Username = "ops", Password = "ops123456", Role = role
            }, default));

        Assert.Equal("无效的角色", ex.Message);
    }

    [Fact]
    public async Task 账号过短被拒()
    {
        var superAdmin = _ctx.GivenAdmin();

        var ex = await Assert.ThrowsAsync<DomainException>(() => CreateHandler().Handle(
            new CreateAdminCommand
            {
                OperatorId = superAdmin.Id, Username = "a", Password = "ops123456", Role = 1
            }, default));

        Assert.Contains("账号长度", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 口令过短被拒()
    {
        var superAdmin = _ctx.GivenAdmin();

        var ex = await Assert.ThrowsAsync<DomainException>(() => CreateHandler().Handle(
            new CreateAdminCommand
            {
                OperatorId = superAdmin.Id, Username = "ops01", Password = "123", Role = 1
            }, default));

        Assert.Equal("密码长度不能少于 6 个字符", ex.Message);
    }

    [Fact]
    public async Task 更新他人角色与状态()
    {
        var superAdmin = _ctx.GivenAdmin("admin");
        var ops = _ctx.GivenAdmin("ops", AdminRole.Operator);

        var result = await new UpdateAdminHandler(_ctx.Admins, _ctx.AdminSessions, _ctx.Audit).Handle(
            new UpdateAdminCommand
            {
                OperatorId = superAdmin.Id, TargetId = ops.Id, Role = 0, Status = 0
            }, default);

        Assert.Equal("已更新", result.Message);
        var updated = await _ctx.Admins.FindByIdAsync(ops.Id);
        Assert.Equal(AdminRole.Super, updated!.Role);
        Assert.Equal(AdminStatus.Disabled, updated.Status);
        Assert.True(_ctx.Audit.Has(AuditActions.AdminUpdate));
    }

    [Fact]
    public async Task 停用管理员后其令牌立即失效()
    {
        var superAdmin = _ctx.GivenAdmin("admin");
        var ops = _ctx.GivenAdmin("ops", AdminRole.Operator);
        await _ctx.AdminSessions.CreateAsync(ops.Id, "ops-session");

        await new UpdateAdminHandler(_ctx.Admins, _ctx.AdminSessions, _ctx.Audit).Handle(
            new UpdateAdminCommand
            {
                OperatorId = superAdmin.Id, TargetId = ops.Id, Status = 0
            }, default);

        // 停用必须立刻踢掉他的手机会话，否则最长要等 Jwt:ExpireMinutes 才真正生效
        Assert.Contains(ops.Id, _ctx.AdminSessions.RevokedAdmins);
        Assert.False(await _ctx.AdminSessions.IsValidAsync("ops-session"));
    }

    [Fact]
    public async Task 仅改状态为启用时不吊销会话()
    {
        var superAdmin = _ctx.GivenAdmin("admin");
        var ops = _ctx.GivenAdmin("ops", AdminRole.Operator);
        await _ctx.AdminSessions.CreateAsync(ops.Id, "ops-session");

        await new UpdateAdminHandler(_ctx.Admins, _ctx.AdminSessions, _ctx.Audit).Handle(
            new UpdateAdminCommand
            {
                OperatorId = superAdmin.Id, TargetId = ops.Id, Status = 1
            }, default);

        // 启用一个账号不该顺手把别人的会话踢掉（与被停用时的处理刻意不同）
        Assert.DoesNotContain(ops.Id, _ctx.AdminSessions.RevokedAdmins);
        Assert.True(await _ctx.AdminSessions.IsValidAsync("ops-session"));
    }

    [Fact]
    public async Task 不能停用自己()
    {
        var superAdmin = _ctx.GivenAdmin();

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new UpdateAdminHandler(_ctx.Admins, _ctx.AdminSessions, _ctx.Audit).Handle(
                new UpdateAdminCommand
                {
                    OperatorId = superAdmin.Id, TargetId = superAdmin.Id, Status = 0
                }, default));

        Assert.Equal("不能停用或降级自己", ex.Message);
    }

    [Fact]
    public async Task 不能把自己降级为运营()
    {
        var superAdmin = _ctx.GivenAdmin();

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new UpdateAdminHandler(_ctx.Admins, _ctx.AdminSessions, _ctx.Audit).Handle(
                new UpdateAdminCommand
                {
                    OperatorId = superAdmin.Id, TargetId = superAdmin.Id, Role = 1
                }, default));

        Assert.Equal("不能停用或降级自己", ex.Message);
    }

    [Fact]
    public async Task 无效状态值被拒()
    {
        var superAdmin = _ctx.GivenAdmin("admin");
        var ops = _ctx.GivenAdmin("ops", AdminRole.Operator);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new UpdateAdminHandler(_ctx.Admins, _ctx.AdminSessions, _ctx.Audit).Handle(
                new UpdateAdminCommand
                {
                    OperatorId = superAdmin.Id, TargetId = ops.Id, Status = 9
                }, default));

        Assert.Equal("无效的状态", ex.Message);
    }

    [Fact]
    public async Task 删除运营管理员并留审计()
    {
        var superAdmin = _ctx.GivenAdmin("admin");
        var ops = _ctx.GivenAdmin("ops", AdminRole.Operator);

        var result = await new DeleteAdminHandler(_ctx.Admins, _ctx.AdminSessions, _ctx.Audit).Handle(
            new DeleteAdminCommand { OperatorId = superAdmin.Id, TargetId = ops.Id }, default);

        Assert.Equal("已删除", result.Message);
        Assert.Null(await _ctx.Admins.FindByIdAsync(ops.Id));
        Assert.True(_ctx.Audit.Has(AuditActions.AdminDelete));
    }

    [Fact]
    public async Task 删除管理员后其令牌立即失效()
    {
        var superAdmin = _ctx.GivenAdmin("admin");
        var ops = _ctx.GivenAdmin("ops", AdminRole.Operator);
        await _ctx.AdminSessions.CreateAsync(ops.Id, "ops-session");

        await new DeleteAdminHandler(_ctx.Admins, _ctx.AdminSessions, _ctx.Audit).Handle(
            new DeleteAdminCommand { OperatorId = superAdmin.Id, TargetId = ops.Id }, default);

        // 账号行删了但令牌还在有效期内 —— 必须同时吊销会话，否则等于没删
        Assert.Contains(ops.Id, _ctx.AdminSessions.RevokedAdmins);
        Assert.False(await _ctx.AdminSessions.IsValidAsync("ops-session"));
    }

    [Fact]
    public async Task 不能删除自己()
    {
        var superAdmin = _ctx.GivenAdmin();

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new DeleteAdminHandler(_ctx.Admins, _ctx.AdminSessions, _ctx.Audit).Handle(
                new DeleteAdminCommand
                {
                    OperatorId = superAdmin.Id, TargetId = superAdmin.Id
                }, default));

        Assert.Equal("不能删除自己", ex.Message);
    }

    [Fact]
    public async Task 不能删除超级管理员()
    {
        var operatorAdmin = _ctx.GivenAdmin("ops", AdminRole.Operator);
        var superAdmin = _ctx.GivenAdmin("root", AdminRole.Super);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new DeleteAdminHandler(_ctx.Admins, _ctx.AdminSessions, _ctx.Audit).Handle(
                new DeleteAdminCommand
                {
                    OperatorId = operatorAdmin.Id, TargetId = superAdmin.Id
                }, default));

        Assert.Equal("不能删除超级管理员", ex.Message);
    }

    [Fact]
    public async Task 管理员列表按ID升序()
    {
        _ctx.GivenAdmin("admin");
        _ctx.GivenAdmin("ops", AdminRole.Operator);

        var result = await new ListAdminsHandler(_ctx.Admins).Handle(new ListAdminsQuery(), default);

        Assert.Equal(2, result.Data!.Count);
        Assert.True(result.Data[0].Id < result.Data[1].Id);
    }

    [Fact]
    public async Task 审计日志分页与按动作过滤()
    {
        await _ctx.AuditLogs.AddAsync(AdminAuditLog.Record(
            1, "admin", AuditActions.UserBan, AuditActions.TargetUser, "1", "d", null, _ctx.Now));
        await _ctx.AuditLogs.AddAsync(AdminAuditLog.Record(
            1, "admin", AuditActions.UserKick, AuditActions.TargetUser, "1", "d", null, _ctx.Now));

        var all = await new ListAuditLogsHandler(_ctx.AuditLogs).Handle(
            new ListAuditLogsQuery(), default);
        Assert.Equal(2, all.Data!.Total);

        var filtered = await new ListAuditLogsHandler(_ctx.AuditLogs).Handle(
            new ListAuditLogsQuery { Action = AuditActions.UserBan }, default);
        Assert.Equal(AuditActions.UserBan, Assert.Single(filtered.Data!.Items).Action);
    }
}

public class AdminUserManagementTests
{
    private readonly ApplicationTestContext _ctx = new();

    private BanUserHandler BanHandler()
        => new(_ctx.Users, _ctx.Events, _ctx.Audit, _ctx.Clock);

    [Fact]
    public async Task 封禁成功并留审计()
    {
        var admin = _ctx.GivenAdmin();
        var user = _ctx.GivenUser();

        var result = await BanHandler().Handle(new BanUserCommand
        {
            AdminId = admin.Id, UserId = user.Id, Banned = true, Reason = "违规"
        }, default);

        Assert.Equal("已封禁，该用户所有设备已下线", result.Message);
        Assert.True((await _ctx.Users.FindByIdAsync(user.Id))!.IsBanned);
        Assert.True(_ctx.Audit.Has(AuditActions.UserBan));
    }

    /// <summary>
    /// 封禁的「踢掉全部设备」由 UserBanned 事件订阅方完成，
    /// 而非在用例里手写 —— 这样任何触发封禁的路径都自动获得该行为。
    /// </summary>
    [Fact]
    public async Task 封禁触发终止全部会话()
    {
        var admin = _ctx.GivenAdmin();
        var user = _ctx.GivenUser();
        await _ctx.Sessions.SeedSessionAsync(user.Id, "s1");
        await _ctx.Sessions.SeedSessionAsync(user.Id, "s2");
        _ctx.Events.Subscribe(new TerminateSessionsOnUserBanned(
            _ctx.Terminator, NullLogger<TerminateSessionsOnUserBanned>.Instance));

        await BanHandler().Handle(new BanUserCommand
        {
            AdminId = admin.Id, UserId = user.Id, Banned = true, Reason = "违规"
        }, default);

        Assert.Contains(user.Id, _ctx.Terminator.TerminatedAllFor);
        Assert.Empty(await _ctx.Sessions.ListSessionIdsAsync(user.Id));
    }

    [Fact]
    public async Task 解封成功并清空封禁字段()
    {
        var admin = _ctx.GivenAdmin();
        var user = _ctx.GivenUser();
        await BanHandler().Handle(new BanUserCommand
        {
            AdminId = admin.Id, UserId = user.Id, Banned = true, Reason = "违规"
        }, default);

        var result = await BanHandler().Handle(new BanUserCommand
        {
            AdminId = admin.Id, UserId = user.Id, Banned = false
        }, default);

        Assert.Equal("已解封", result.Message);
        var stored = await _ctx.Users.FindByIdAsync(user.Id);
        Assert.False(stored!.IsBanned);
        Assert.Null(stored.BanReason);
        Assert.True(_ctx.Audit.Has(AuditActions.UserUnban));
    }

    [Fact]
    public async Task 机器人账号不支持封禁()
    {
        var admin = _ctx.GivenAdmin();
        var owner = _ctx.GivenUser();
        var (_, botUser) = _ctx.GivenRobot(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => BanHandler().Handle(
            new BanUserCommand { AdminId = admin.Id, UserId = botUser.Id, Banned = true }, default));

        Assert.Equal("机器人账号不支持封禁，请直接删除机器人", ex.Message);
    }

    [Fact]
    public async Task 用户不存在时抛出()
    {
        var admin = _ctx.GivenAdmin();

        await Assert.ThrowsAsync<EntityNotFoundException>(() => BanHandler().Handle(
            new BanUserCommand { AdminId = admin.Id, UserId = 99999, Banned = true }, default));
    }

    [Fact]
    public async Task 强制下线不改账号状态()
    {
        var admin = _ctx.GivenAdmin();
        var user = _ctx.GivenUser();
        await _ctx.Sessions.SeedSessionAsync(user.Id, "s1");

        var result = await new KickUserHandler(_ctx.Users, _ctx.Terminator, _ctx.Audit).Handle(
            new KickUserCommand { AdminId = admin.Id, UserId = user.Id }, default);

        Assert.Equal("该用户所有设备已下线", result.Message);
        Assert.False((await _ctx.Users.FindByIdAsync(user.Id))!.IsBanned);
        Assert.Contains(user.Id, _ctx.Terminator.TerminatedAllFor);
        Assert.True(_ctx.Audit.Has(AuditActions.UserKick));
    }

    [Fact]
    public async Task 重置口令成功并留审计()
    {
        var admin = _ctx.GivenAdmin();
        var user = _ctx.GivenUser();

        var result = await new ResetUserPasswordHandler(
                _ctx.Users, _ctx.Hasher, _ctx.Events, _ctx.Audit, _ctx.Clock)
            .Handle(new ResetUserPasswordCommand
            {
                AdminId = admin.Id, UserId = user.Id, NewPassword = "adminset123"
            }, default);

        Assert.Equal("密码已重置，该用户需重新登录", result.Message);
        Assert.True(_ctx.Hasher.Verify(
            "adminset123", (await _ctx.Users.FindByIdAsync(user.Id))!.PasswordHash));
        Assert.True(_ctx.Audit.Has(AuditActions.UserResetPassword));
    }

    [Fact]
    public async Task 重置口令过短时被拒()
    {
        var admin = _ctx.GivenAdmin();
        var user = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new ResetUserPasswordHandler(
                    _ctx.Users, _ctx.Hasher, _ctx.Events, _ctx.Audit, _ctx.Clock)
                .Handle(new ResetUserPasswordCommand
                {
                    AdminId = admin.Id, UserId = user.Id, NewPassword = "123"
                }, default));

        Assert.Equal("密码长度不能少于 6 个字符", ex.Message);
    }

    [Fact]
    public async Task 机器人账号无口令可重置()
    {
        var admin = _ctx.GivenAdmin();
        var owner = _ctx.GivenUser();
        var (_, botUser) = _ctx.GivenRobot(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new ResetUserPasswordHandler(
                    _ctx.Users, _ctx.Hasher, _ctx.Events, _ctx.Audit, _ctx.Clock)
                .Handle(new ResetUserPasswordCommand
                {
                    AdminId = admin.Id, UserId = botUser.Id, NewPassword = "x123456"
                }, default));

        Assert.Equal("机器人账号无密码", ex.Message);
    }
}

public class AdminUserQueryTests
{
    private readonly ApplicationTestContext _ctx = new();

    private AdminUserDtoBuilder Builder()
        => new(_ctx.Friendships, _ctx.GroupMembers,
            _ctx.PrivateMessages, _ctx.GroupMessages, _ctx.Connections);

    private ListUsersHandler ListHandler() => new(_ctx.Users, Builder());

    [Fact]
    public async Task 用户列表按ID倒序分页()
    {
        for (var i = 0; i < 5; i++) _ctx.GivenUser($"用户{i}", $"u{i}@test.local");

        var result = await ListHandler().Handle(new ListUsersQuery { PageSize = 2 }, default);

        Assert.Equal(2, result.Data!.Items.Count);
        Assert.Equal(5, result.Data.Total);
        Assert.True(result.Data.Items[0].Id > result.Data.Items[1].Id);
    }

    [Fact]
    public async Task 按昵称搜索()
    {
        _ctx.GivenUser("张三", "a@test.local");
        _ctx.GivenUser("李四", "b@test.local");

        var result = await ListHandler().Handle(new ListUsersQuery { Keyword = "张" }, default);

        Assert.Equal("张三", Assert.Single(result.Data!.Items).Nickname);
    }

    [Fact]
    public async Task 按账号ID搜索()
    {
        var user = _ctx.GivenUser();

        var result = await ListHandler().Handle(new ListUsersQuery
        {
            Keyword = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }, default);

        Assert.Equal(user.Id, Assert.Single(result.Data!.Items).Id);
    }

    [Fact]
    public async Task 按邮箱片段搜索()
    {
        _ctx.GivenUser("张三", "alice@test.local");
        _ctx.GivenUser("李四", "bob@test.local");

        var result = await ListHandler().Handle(new ListUsersQuery { Keyword = "alice" }, default);

        Assert.Equal("张三", Assert.Single(result.Data!.Items).Nickname);
    }

    [Fact]
    public async Task 按机器人筛选()
    {
        var owner = _ctx.GivenUser();
        _ctx.GivenRobot(owner.Id);

        var bots = await ListHandler().Handle(new ListUsersQuery { IsBot = true }, default);
        Assert.All(bots.Data!.Items, u => Assert.True(u.IsBot));

        var humans = await ListHandler().Handle(new ListUsersQuery { IsBot = false }, default);
        Assert.All(humans.Data!.Items, u => Assert.False(u.IsBot));
    }

    [Fact]
    public async Task 按封禁状态筛选()
    {
        var admin = _ctx.GivenAdmin();
        var banned = _ctx.GivenUser("被封", "a@test.local");
        _ctx.GivenUser("正常", "b@test.local");
        await new BanUserHandler(_ctx.Users, _ctx.Events, _ctx.Audit, _ctx.Clock).Handle(
            new BanUserCommand { AdminId = admin.Id, UserId = banned.Id, Banned = true }, default);

        var result = await ListHandler().Handle(new ListUsersQuery { Banned = true }, default);

        Assert.Equal(banned.Id, Assert.Single(result.Data!.Items).Id);
    }

    [Fact]
    public async Task 用户DTO含好友数群数消息数与在线状态()
    {
        var (a, b) = _ctx.GivenFriends();
        var group = _ctx.GivenGroup(a.Id, b.Id);
        _ctx.GivenPrivateMessage(a.Id, b.Id);
        _ctx.GivenGroupMessage(group.Id, a.Id);
        _ctx.Connections.SetOnline(a.Id);

        var result = await ListHandler().Handle(new ListUsersQuery { Keyword = "张三" }, default);

        var dto = Assert.Single(result.Data!.Items);
        Assert.Equal(1, dto.FriendCount);
        Assert.Equal(1, dto.GroupCount);
        Assert.Equal(2, dto.MessageCount);   // 1 私聊 + 1 群聊
        Assert.True(dto.IsOnline);
    }

    [Fact]
    public async Task 用户详情含登录设备()
    {
        var user = _ctx.GivenUser();
        await _ctx.Sessions.SeedSessionAsync(user.Id, "s1", "手机");

        var result = await new GetUserDetailHandler(_ctx.Users, _ctx.Sessions, Builder())
            .Handle(new GetUserDetailQuery { UserId = user.Id }, default);

        Assert.Equal(user.Id, result.Data!.User.Id);
        Assert.Equal("手机", Assert.Single(result.Data.Sessions).DeviceName);
    }

    [Fact]
    public async Task 用户详情_用户不存在时抛出()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => new GetUserDetailHandler(_ctx.Users, _ctx.Sessions, Builder())
                .Handle(new GetUserDetailQuery { UserId = 99999 }, default));
    }

    [Fact]
    public async Task 空结果时返回空分页()
    {
        var result = await ListHandler().Handle(new ListUsersQuery { Keyword = "不存在" }, default);

        Assert.Empty(result.Data!.Items);
        Assert.Equal(0, result.Data.Total);
    }
}

public class AdminGroupManagementTests
{
    private readonly ApplicationTestContext _ctx = new();

    [Fact]
    public async Task 强制解散群_删群删成员删消息()
    {
        var admin = _ctx.GivenAdmin();
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);
        _ctx.GivenGroupMessage(group.Id, owner.Id);

        var result = await new DissolveGroupByAdminHandler(
                _ctx.Groups, _ctx.GroupMembers, _ctx.GroupMessages,
                _ctx.Events, _ctx.Audit, _ctx.Clock)
            .Handle(new DissolveGroupByAdminCommand { AdminId = admin.Id, GroupId = group.Id },
                default);

        Assert.Equal("群已解散（2 名成员）", result.Message);
        Assert.Empty(_ctx.Groups.All);
        Assert.Empty(_ctx.GroupMembers.All);
        Assert.Empty(_ctx.GroupMessages.All);
        Assert.True(_ctx.Audit.Has(AuditActions.GroupDissolve));
    }

    [Fact]
    public async Task 强制解散与用户自行解散发同一事件()
    {
        var admin = _ctx.GivenAdmin();
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        await new DissolveGroupByAdminHandler(
                _ctx.Groups, _ctx.GroupMembers, _ctx.GroupMessages,
                _ctx.Events, _ctx.Audit, _ctx.Clock)
            .Handle(new DissolveGroupByAdminCommand { AdminId = admin.Id, GroupId = group.Id },
                default);

        Assert.True(_ctx.Events.Has<Domain.Groups.GroupDissolved>());
    }

    [Fact]
    public async Task 强制移除成员()
    {
        var admin = _ctx.GivenAdmin();
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        var result = await new RemoveGroupMemberByAdminHandler(
                _ctx.Groups, _ctx.GroupMembers, _ctx.Users, _ctx.Audit)
            .Handle(new RemoveGroupMemberByAdminCommand
            {
                AdminId = admin.Id, GroupId = group.Id, UserId = member.Id
            }, default);

        Assert.Equal("已移除该成员", result.Message);
        Assert.False(await _ctx.GroupMembers.ExistsAsync(group.Id, member.Id));
        Assert.True(_ctx.Audit.Has(AuditActions.GroupRemoveMember));
    }

    [Fact]
    public async Task 不能移除群主()
    {
        var admin = _ctx.GivenAdmin();
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new RemoveGroupMemberByAdminHandler(
                    _ctx.Groups, _ctx.GroupMembers, _ctx.Users, _ctx.Audit)
                .Handle(new RemoveGroupMemberByAdminCommand
                {
                    AdminId = admin.Id, GroupId = group.Id, UserId = owner.Id
                }, default));

        Assert.Equal("不能移除群主，请先转让群主", ex.Message);
    }

    [Fact]
    public async Task 禁言与解除禁言()
    {
        var admin = _ctx.GivenAdmin();
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);
        var handler = new MuteGroupMemberHandler(
            _ctx.Groups, _ctx.GroupMembers, _ctx.Users, _ctx.Events, _ctx.Audit, _ctx.Clock);

        var muted = await handler.Handle(new MuteGroupMemberCommand
        {
            AdminId = admin.Id, GroupId = group.Id, UserId = member.Id,
            MutedUntil = _ctx.Now.AddHours(2)
        }, default);
        Assert.Equal("已禁言", muted.Message);
        Assert.NotNull((await _ctx.GroupMembers.FindAsync(group.Id, member.Id))!.MutedUntil);

        var unmuted = await handler.Handle(new MuteGroupMemberCommand
        {
            AdminId = admin.Id, GroupId = group.Id, UserId = member.Id, MutedUntil = null
        }, default);
        Assert.Equal("已解除禁言", unmuted.Message);
        Assert.Null((await _ctx.GroupMembers.FindAsync(group.Id, member.Id))!.MutedUntil);
    }

    [Fact]
    public async Task 不能禁言群主()
    {
        var admin = _ctx.GivenAdmin();
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new MuteGroupMemberHandler(
                    _ctx.Groups, _ctx.GroupMembers, _ctx.Users, _ctx.Events, _ctx.Audit, _ctx.Clock)
                .Handle(new MuteGroupMemberCommand
                {
                    AdminId = admin.Id, GroupId = group.Id, UserId = owner.Id,
                    MutedUntil = _ctx.Now.AddHours(1)
                }, default));

        Assert.Equal("不能禁言群主", ex.Message);
    }

    [Fact]
    public async Task 禁言留审计且描述含截止时间()
    {
        var admin = _ctx.GivenAdmin();
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        await new MuteGroupMemberHandler(
                _ctx.Groups, _ctx.GroupMembers, _ctx.Users, _ctx.Events, _ctx.Audit, _ctx.Clock)
            .Handle(new MuteGroupMemberCommand
            {
                AdminId = admin.Id, GroupId = group.Id, UserId = member.Id,
                MutedUntil = _ctx.Now.AddHours(2)
            }, default);

        var record = Assert.Single(_ctx.Audit.Records, r => r.Action == AuditActions.GroupMute);
        Assert.Contains("禁言", record.Detail!, StringComparison.Ordinal);
        Assert.Contains("UTC", record.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 转让群主_新群主升级原群主降级()
    {
        var admin = _ctx.GivenAdmin();
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        var result = await new TransferGroupOwnerHandler(
                _ctx.Groups, _ctx.GroupMembers, _ctx.Events, _ctx.Audit, _ctx.Clock)
            .Handle(new TransferGroupOwnerCommand
            {
                AdminId = admin.Id, GroupId = group.Id, NewOwnerId = member.Id
            }, default);

        Assert.Equal("群主已转让", result.Message);
        Assert.Equal(member.Id, (await _ctx.Groups.FindByIdAsync(group.Id))!.OwnerId);
        Assert.Equal(Domain.Groups.GroupRole.Owner,
            (await _ctx.GroupMembers.FindAsync(group.Id, member.Id))!.Role);
        Assert.Equal(Domain.Groups.GroupRole.Member,
            (await _ctx.GroupMembers.FindAsync(group.Id, owner.Id))!.Role);
        Assert.True(_ctx.Audit.Has(AuditActions.GroupTransfer));
    }

    [Fact]
    public async Task 新群主必须是群成员()
    {
        var admin = _ctx.GivenAdmin();
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => new TransferGroupOwnerHandler(
                    _ctx.Groups, _ctx.GroupMembers, _ctx.Events, _ctx.Audit, _ctx.Clock)
                .Handle(new TransferGroupOwnerCommand
                {
                    AdminId = admin.Id, GroupId = group.Id, NewOwnerId = 99999
                }, default));

        Assert.Equal("新群主必须是群成员", ex.Message);
    }

    [Fact]
    public async Task 群列表含群主昵称成员数消息数()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);
        _ctx.GivenGroupMessage(group.Id, owner.Id);

        var result = await new ListGroupsHandler(
                _ctx.Groups, _ctx.GroupMembers, _ctx.GroupMessages, _ctx.Users)
            .Handle(new ListGroupsQuery(), default);

        var dto = Assert.Single(result.Data!.Items);
        Assert.Equal("群主", dto.OwnerName);
        Assert.Equal(2, dto.MemberCount);
        Assert.Equal(1, dto.MessageCount);
    }

    [Fact]
    public async Task 群详情含成员禁言状态与机器人标记()
    {
        var owner = _ctx.GivenUser();
        var (_, botUser) = _ctx.GivenRobot(owner.Id);
        var group = _ctx.GivenGroup(owner.Id, botUser.Id);
        var botMember = await _ctx.GroupMembers.FindAsync(group.Id, botUser.Id);
        botMember!.SetMute(_ctx.Now.AddHours(1), _ctx.Now);
        await _ctx.GroupMembers.UpdateAsync(botMember);

        var result = await new GetGroupDetailHandler(
                _ctx.Groups, _ctx.GroupMembers, _ctx.GroupMessages, _ctx.Users, _ctx.Connections)
            .Handle(new GetGroupDetailQuery { GroupId = group.Id }, default);

        var bot = result.Data!.Members.Single(m => m.UserId == botUser.Id);
        Assert.True(bot.IsBot);
        Assert.NotNull(bot.MutedUntil);
    }

    [Fact]
    public async Task 群详情_群不存在时抛出()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => new GetGroupDetailHandler(
                    _ctx.Groups, _ctx.GroupMembers, _ctx.GroupMessages, _ctx.Users, _ctx.Connections)
                .Handle(new GetGroupDetailQuery { GroupId = 99999 }, default));
    }
}

public class AdminMessageManagementTests
{
    private readonly ApplicationTestContext _ctx = new();

    private DeleteMessageByAdminHandler Handler()
        => new(_ctx.PrivateMessages, _ctx.GroupMessages, _ctx.Events, _ctx.Audit, _ctx.Clock);

    [Fact]
    public async Task 强制删除私聊消息_复用撤回事件()
    {
        var admin = _ctx.GivenAdmin();
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id);

        var result = await Handler().Handle(new DeleteMessageByAdminCommand
        {
            AdminId = admin.Id, Type = "private", MessageId = message.Id
        }, default);

        Assert.Equal("消息已删除", result.Message);
        Assert.True((await _ctx.PrivateMessages.FindByIdAsync(message.Id))!.IsDeleted);

        var e = _ctx.Events.SingleEvent<Domain.Messaging.MessageRecalled>();
        Assert.True(e.ByAdmin);
        Assert.Equal(Domain.Messaging.ChatSessionType.Private, e.SessionType);
        Assert.True(_ctx.Audit.Has(AuditActions.MessageDelete));
    }

    [Fact]
    public async Task 强制删除不受时间窗限制()
    {
        var admin = _ctx.GivenAdmin();
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id, sentAt: _ctx.Now.AddDays(-30));

        var result = await Handler().Handle(new DeleteMessageByAdminCommand
        {
            AdminId = admin.Id, Type = "private", MessageId = message.Id
        }, default);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task 强制删除群消息()
    {
        var admin = _ctx.GivenAdmin();
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);
        var message = _ctx.GivenGroupMessage(group.Id, owner.Id);

        await Handler().Handle(new DeleteMessageByAdminCommand
        {
            AdminId = admin.Id, Type = "group", MessageId = message.Id
        }, default);

        Assert.True((await _ctx.GroupMessages.FindByIdAsync(message.Id))!.IsDeleted);
        Assert.Equal(group.Id, _ctx.Events.SingleEvent<Domain.Messaging.MessageRecalled>().GroupId);
    }

    [Fact]
    public async Task 无效消息类型被拒()
    {
        var admin = _ctx.GivenAdmin();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new DeleteMessageByAdminCommand
            {
                AdminId = admin.Id, Type = "bogus", MessageId = 1
            }, default));

        Assert.Equal("无效的消息类型", ex.Message);
    }

    [Fact]
    public async Task 消息不存在时抛出()
    {
        var admin = _ctx.GivenAdmin();

        await Assert.ThrowsAsync<EntityNotFoundException>(() => Handler().Handle(
            new DeleteMessageByAdminCommand
            {
                AdminId = admin.Id, Type = "private", MessageId = 99999
            }, default));
    }

    [Fact]
    public async Task 检索合并私聊与群聊_按时间倒序()
    {
        var (a, b) = _ctx.GivenFriends();
        var group = _ctx.GivenGroup(a.Id);
        _ctx.GivenPrivateMessage(a.Id, b.Id, "关键词私聊", _ctx.Now.AddMinutes(-5));
        _ctx.GivenGroupMessage(group.Id, a.Id, "关键词群聊", _ctx.Now.AddMinutes(-1));

        var result = await new SearchMessagesByAdminHandler(
                _ctx.PrivateMessages, _ctx.GroupMessages, _ctx.Users)
            .Handle(new SearchMessagesByAdminQuery { Keyword = "关键词" }, default);

        Assert.Equal(2, result.Data!.Items.Count);
        Assert.Equal("关键词群聊", result.Data.Items[0].Content);
    }

    [Fact]
    public async Task 指定群时不掺入私聊结果()
    {
        // 否则「按群过滤」形同虚设
        var (a, b) = _ctx.GivenFriends();
        var group = _ctx.GivenGroup(a.Id);
        _ctx.GivenPrivateMessage(a.Id, b.Id, "关键词私聊");
        _ctx.GivenGroupMessage(group.Id, a.Id, "关键词群聊");

        var result = await new SearchMessagesByAdminHandler(
                _ctx.PrivateMessages, _ctx.GroupMessages, _ctx.Users)
            .Handle(new SearchMessagesByAdminQuery { Keyword = "关键词", GroupId = group.Id },
                default);

        Assert.Equal("关键词群聊", Assert.Single(result.Data!.Items).Content);
    }

    [Fact]
    public async Task 已删除的消息仍出现在管理检索里()
    {
        // 管理员需要能看到被删内容以便复核
        var admin = _ctx.GivenAdmin();
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id, "关键词");
        await Handler().Handle(new DeleteMessageByAdminCommand
        {
            AdminId = admin.Id, Type = "private", MessageId = message.Id
        }, default);

        var result = await new SearchMessagesByAdminHandler(
                _ctx.PrivateMessages, _ctx.GroupMessages, _ctx.Users)
            .Handle(new SearchMessagesByAdminQuery { Keyword = "关键词" }, default);

        Assert.True(Assert.Single(result.Data!.Items).IsDeleted);
    }
}

public class AdminRobotManagementTests
{
    private readonly ApplicationTestContext _ctx = new();

    [Fact]
    public async Task 机器人列表含创建者与统计()
    {
        var owner = _ctx.GivenUser(nickname: "创建者");
        var (robot, _) = _ctx.GivenRobot(owner.Id);
        robot.RecordPush();
        robot.RecordCallbackFailure();
        await _ctx.Robots.UpdateAsync(robot);

        var result = await new ListRobotsHandler(_ctx.Robots, _ctx.Users).Handle(
            new ListRobotsQuery(), default);

        var dto = Assert.Single(result.Data!.Items);
        Assert.Equal("创建者", dto.OwnerName);
        Assert.Equal(1, dto.PushCount);
        Assert.Equal(1, dto.CallbackFailCount);
    }

    [Fact]
    public async Task 启停机器人并留审计()
    {
        var admin = _ctx.GivenAdmin();
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id);
        var handler = new SetRobotEnabledHandler(_ctx.Robots, _ctx.Audit);

        var disabled = await handler.Handle(new SetRobotEnabledCommand
        {
            AdminId = admin.Id, RobotId = robot.Id, Enabled = false
        }, default);
        Assert.Equal("机器人已停用", disabled.Message);
        Assert.False((await _ctx.Robots.FindByIdAsync(robot.Id))!.Enabled);

        var enabled = await handler.Handle(new SetRobotEnabledCommand
        {
            AdminId = admin.Id, RobotId = robot.Id, Enabled = true
        }, default);
        Assert.Equal("机器人已启用", enabled.Message);
        Assert.True(_ctx.Audit.Has(AuditActions.RobotSetEnabled));
    }

    [Fact]
    public async Task 启停不存在的机器人时抛出()
    {
        var admin = _ctx.GivenAdmin();

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => new SetRobotEnabledHandler(_ctx.Robots, _ctx.Audit).Handle(
                new SetRobotEnabledCommand
                {
                    AdminId = admin.Id, RobotId = 99999, Enabled = false
                }, default));
    }

    [Fact]
    public async Task 机器人列表搜索()
    {
        var owner = _ctx.GivenUser();
        _ctx.GivenRobot(owner.Id, name: "客服助理");
        _ctx.GivenRobot(owner.Id, name: "监控告警");

        var result = await new ListRobotsHandler(_ctx.Robots, _ctx.Users).Handle(
            new ListRobotsQuery { Keyword = "客服" }, default);

        Assert.Equal("客服助理", Assert.Single(result.Data!.Items).Name);
    }
}

public class DashboardTests
{
    private readonly ApplicationTestContext _ctx = new();

    private GetDashboardHandler Handler()
        => new(_ctx.Users, _ctx.Groups, _ctx.Robots,
            _ctx.PrivateMessages, _ctx.GroupMessages, _ctx.Connections, _ctx.Clock);

    [Fact]
    public async Task 统计卡片数据正确()
    {
        var admin = _ctx.GivenAdmin();
        var (a, b) = _ctx.GivenFriends();
        var (_, botUser) = _ctx.GivenRobot(a.Id);
        var group = _ctx.GivenGroup(a.Id, b.Id);
        _ctx.GivenPrivateMessage(a.Id, b.Id);
        _ctx.GivenGroupMessage(group.Id, a.Id);
        _ctx.Connections.SetOnline(a.Id, b.Id);

        await new BanUserHandler(_ctx.Users, _ctx.Events, _ctx.Audit, _ctx.Clock).Handle(
            new BanUserCommand { AdminId = admin.Id, UserId = b.Id, Banned = true }, default);

        var result = await Handler().Handle(new GetDashboardQuery(), default);
        var dto = result.Data!;

        Assert.Equal(3, dto.TotalUsers);          // 甲 + 乙 + 机器人账号
        Assert.Equal(1, dto.BannedUsers);
        Assert.Equal(1, dto.TotalGroups);
        Assert.Equal(1, dto.TotalRobots);
        Assert.Equal(1, dto.PrivateMessageTotal);
        Assert.Equal(1, dto.GroupMessageTotal);
        Assert.Equal(2, dto.TotalMessages);
        Assert.Equal(2, dto.OnlineUsers);
        Assert.NotEqual(0, botUser.Id);
    }

    [Fact]
    public async Task 消息总数等于私聊加群聊()
    {
        var (a, b) = _ctx.GivenFriends();
        var group = _ctx.GivenGroup(a.Id);
        _ctx.GivenPrivateMessage(a.Id, b.Id);
        _ctx.GivenPrivateMessage(a.Id, b.Id);
        _ctx.GivenGroupMessage(group.Id, a.Id);

        var dto = (await Handler().Handle(new GetDashboardQuery(), default)).Data!;

        Assert.Equal(dto.PrivateMessageTotal + dto.GroupMessageTotal, dto.TotalMessages);
        Assert.Equal(dto.TodayPrivateMessages + dto.TodayGroupMessages, dto.TodayMessages);
    }

    [Fact]
    public async Task 趋势与小时分布点数固定()
    {
        var dto = (await Handler().Handle(new GetDashboardQuery(), default)).Data!;

        Assert.Equal(GetDashboardQuery.TrendDays, dto.RegisterTrend.Count);
        Assert.Equal(GetDashboardQuery.TrendDays, dto.MessageTrend.Count);
        Assert.Equal(GetDashboardQuery.TrendHours, dto.MessageHourTrend.Count);
    }

    /// <summary>
    /// 改造前这里有 bug：先按 dayAgo 算刻度、下一行又用 UtcNow 覆盖，
    /// 两行都 AddHours(-i)，导致图表左右颠倒。
    /// </summary>
    [Fact]
    public async Task 小时刻度按时间正序_末位是当前小时()
    {
        var dto = (await Handler().Handle(new GetDashboardQuery(), default)).Data!;

        var expectedLast = _ctx.Now.ToString("HH:00", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expectedLast, dto.MessageHourTrend[^1].Hour);

        var expectedFirst = _ctx.Now
            .AddHours(-(GetDashboardQuery.TrendHours - 1))
            .ToString("HH:00", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expectedFirst, dto.MessageHourTrend[0].Hour);
    }

    [Fact]
    public async Task 今日活跃用户按去重发送者统计()
    {
        var (a, b) = _ctx.GivenFriends();
        var group = _ctx.GivenGroup(a.Id);
        _ctx.GivenPrivateMessage(a.Id, b.Id);       // 甲发
        _ctx.GivenPrivateMessage(b.Id, a.Id);       // 乙发
        _ctx.GivenGroupMessage(group.Id, a.Id);     // 甲又发（群）

        var dto = (await Handler().Handle(new GetDashboardQuery(), default)).Data!;

        Assert.Equal(2, dto.TodayActiveUsers);
    }

    [Fact]
    public async Task TOP用户排行按发送量合并私聊与群聊()
    {
        var (a, b) = _ctx.GivenFriends();
        var group = _ctx.GivenGroup(a.Id, b.Id);
        _ctx.GivenPrivateMessage(a.Id, b.Id);
        _ctx.GivenGroupMessage(group.Id, a.Id);
        _ctx.GivenPrivateMessage(b.Id, a.Id);

        var dto = (await Handler().Handle(new GetDashboardQuery(), default)).Data!;

        Assert.NotEmpty(dto.TopUsers);
        Assert.Equal(a.Id, dto.TopUsers[0].UserId);
        Assert.Equal(2, dto.TopUsers[0].Count);
        Assert.Equal("张三", dto.TopUsers[0].Nickname);
    }

    [Fact]
    public async Task TOP群排行按消息量倒序()
    {
        var owner = _ctx.GivenUser();
        var busy = _ctx.GivenGroup(owner.Id);
        var quiet = _ctx.GivenGroup(owner.Id);
        _ctx.GivenGroupMessage(busy.Id, owner.Id);
        _ctx.GivenGroupMessage(busy.Id, owner.Id);
        _ctx.GivenGroupMessage(quiet.Id, owner.Id);

        var dto = (await Handler().Handle(new GetDashboardQuery(), default)).Data!;

        Assert.Equal(busy.Id, dto.TopGroups[0].GroupId);
        Assert.Equal(2, dto.TopGroups[0].Count);
    }

    [Fact]
    public async Task 空库时各项为0且不报错()
    {
        var dto = (await Handler().Handle(new GetDashboardQuery(), default)).Data!;

        Assert.Equal(0, dto.TotalUsers);
        Assert.Equal(0, dto.TotalMessages);
        Assert.Empty(dto.TopUsers);
        Assert.Empty(dto.TopGroups);
        Assert.Equal(GetDashboardQuery.TrendDays, dto.RegisterTrend.Count);
    }
}
