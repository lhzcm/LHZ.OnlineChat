using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;

namespace LHZ.OnlineChat.Domain.Tests.Admins;

public class AdminCreationTests
{
    private static readonly PasswordHash AnyHash = PasswordHash.FromHash("$2a$11$fakehash");

    [Fact]
    public void Create_默认启用()
    {
        var admin = Admin.Create("ops01", AnyHash, AdminRole.Operator, T.Now);

        Assert.Equal("ops01", admin.Username);
        Assert.Equal(AdminRole.Operator, admin.Role);
        Assert.Equal(AdminStatus.Enabled, admin.Status);
        Assert.False(admin.IsSuper);
        Assert.Null(admin.LastLoginAt);
        Assert.Equal(T.Now, admin.CreatedAt);
    }

    [Fact]
    public void Create_超管角色()
    {
        var admin = Admin.Create("root", AnyHash, AdminRole.Super, T.Now);

        Assert.True(admin.IsSuper);
    }

    [Fact]
    public void NormalizeUsername_去空白()
    {
        Assert.Equal("ops01", Admin.NormalizeUsername("  ops01 "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]              // 短于下限
    public void NormalizeUsername_过短时抛出(string? username)
    {
        var ex = Assert.Throws<DomainException>(() => Admin.NormalizeUsername(username));

        Assert.Equal(
            $"账号长度需为 {Admin.MinUsernameLength}-{Admin.MaxUsernameLength} 个字符", ex.Message);
    }

    [Fact]
    public void NormalizeUsername_过长时抛出()
    {
        Assert.Throws<DomainException>(
            () => Admin.NormalizeUsername(new string('x', Admin.MaxUsernameLength + 1)));
    }

    [Theory]
    [InlineData(Admin.MinUsernameLength)]
    [InlineData(Admin.MaxUsernameLength)]
    public void NormalizeUsername_边界长度通过(int length)
    {
        Assert.Equal(length, Admin.NormalizeUsername(new string('x', length)).Length);
    }
}

public class AdminLoginTests
{
    [Fact]
    public void EnsureCanLogin_启用状态通过()
    {
        TestAdmins.Create(status: AdminStatus.Enabled).EnsureCanLogin();
    }

    [Fact]
    public void EnsureCanLogin_停用状态被拒()
    {
        var admin = TestAdmins.Create(status: AdminStatus.Disabled);

        var ex = Assert.Throws<DomainException>(admin.EnsureCanLogin);

        Assert.Equal("该管理员账号已停用", ex.Message);
    }

    [Fact]
    public void RecordLogin_记录最后登录时间()
    {
        var admin = TestAdmins.Create();

        admin.RecordLogin(T.Now);

        Assert.Equal(T.Now, admin.LastLoginAt);
    }

    [Fact]
    public void SetPassword_替换哈希()
    {
        var admin = TestAdmins.Create();
        var newHash = PasswordHash.FromHash("$2a$11$newhash");

        admin.SetPassword(newHash);

        Assert.Equal(newHash, admin.PasswordHash);
    }
}

public class AdminRoleChangeTests
{
    [Fact]
    public void ChangeRoleAndStatus_可改他人角色与状态()
    {
        var target = TestAdmins.Create(id: 2, role: AdminRole.Operator);

        target.ChangeRoleAndStatus(AdminRole.Super, AdminStatus.Disabled, operatorId: 1);

        Assert.Equal(AdminRole.Super, target.Role);
        Assert.Equal(AdminStatus.Disabled, target.Status);
    }

    [Fact]
    public void ChangeRoleAndStatus_只传角色时状态不变()
    {
        var target = TestAdmins.Create(id: 2, status: AdminStatus.Enabled);

        target.ChangeRoleAndStatus(AdminRole.Super, null, operatorId: 1);

        Assert.Equal(AdminStatus.Enabled, target.Status);
    }

    [Fact]
    public void ChangeRoleAndStatus_不能停用自己()
    {
        // 否则超管可能把自己锁在门外
        var self = TestAdmins.Create(id: 1, role: AdminRole.Super);

        var ex = Assert.Throws<DomainException>(
            () => self.ChangeRoleAndStatus(null, AdminStatus.Disabled, operatorId: 1));

        Assert.Equal("不能停用或降级自己", ex.Message);
        Assert.Equal(AdminStatus.Enabled, self.Status);
    }

    [Fact]
    public void ChangeRoleAndStatus_不能把自己降级为运营()
    {
        var self = TestAdmins.Create(id: 1, role: AdminRole.Super);

        var ex = Assert.Throws<DomainException>(
            () => self.ChangeRoleAndStatus(AdminRole.Operator, null, operatorId: 1));

        Assert.Equal("不能停用或降级自己", ex.Message);
        Assert.Equal(AdminRole.Super, self.Role);
    }

    [Fact]
    public void ChangeRoleAndStatus_可以把自己保持为超管()
    {
        // 幂等操作不该被拦
        var self = TestAdmins.Create(id: 1, role: AdminRole.Super);

        self.ChangeRoleAndStatus(AdminRole.Super, AdminStatus.Enabled, operatorId: 1);

        Assert.True(self.IsSuper);
    }

    [Fact]
    public void EnsureDeletableBy_不能删自己()
    {
        var self = TestAdmins.Create(id: 1, role: AdminRole.Operator);

        Assert.Equal("不能删除自己",
            Assert.Throws<DomainException>(() => self.EnsureDeletableBy(1)).Message);
    }

    [Fact]
    public void EnsureDeletableBy_不能删超管()
    {
        var superAdmin = TestAdmins.Create(id: 2, role: AdminRole.Super);

        Assert.Equal("不能删除超级管理员",
            Assert.Throws<DomainException>(() => superAdmin.EnsureDeletableBy(1)).Message);
    }

    [Fact]
    public void EnsureDeletableBy_可删他人运营账号()
    {
        TestAdmins.Create(id: 2, role: AdminRole.Operator).EnsureDeletableBy(1);
    }
}

public class AdminAuditLogTests
{
    [Fact]
    public void Record_完整记录操作上下文()
    {
        var log = AdminAuditLog.Record(
            adminId: 1, adminName: "admin", AuditActions.UserBan, AuditActions.TargetUser,
            targetId: "10002", detail: "封禁用户 张三", ip: "1.2.3.4", T.Now);

        Assert.Equal(1, log.AdminId);
        Assert.Equal("admin", log.AdminName);
        Assert.Equal("user.ban", log.Action);
        Assert.Equal("user", log.TargetType);
        Assert.Equal("10002", log.TargetId);
        Assert.Equal("封禁用户 张三", log.Detail);
        Assert.Equal("1.2.3.4", log.Ip);
        Assert.Equal(T.Now, log.CreatedAt);
    }

    [Fact]
    public void Record_可选字段允许为空()
    {
        var log = AdminAuditLog.Record(1, "admin", "x", "y", null, null, null, T.Now);

        Assert.Null(log.TargetId);
        Assert.Null(log.Detail);
        Assert.Null(log.Ip);
    }

    [Fact]
    public void 审计动作常量与目标类型互不重叠()
    {
        // 动作名形如 "对象.动作"，目标类型是单个词；混用会让按动作过滤失效
        var actions = new[]
        {
            AuditActions.UserBan, AuditActions.UserUnban, AuditActions.UserKick,
            AuditActions.UserResetPassword, AuditActions.AdminCreate, AuditActions.AdminUpdate,
            AuditActions.AdminDelete, AuditActions.GroupDissolve, AuditActions.GroupRemoveMember,
            AuditActions.GroupMute, AuditActions.GroupTransfer, AuditActions.MessageDelete,
            AuditActions.RobotSetEnabled, AuditActions.RobotDelete
        };

        Assert.All(actions, a => Assert.Contains('.', a));
        Assert.Equal(actions.Length, actions.Distinct(StringComparer.Ordinal).Count());

        var targets = new[]
        {
            AuditActions.TargetUser, AuditActions.TargetGroup, AuditActions.TargetMessage,
            AuditActions.TargetRobot, AuditActions.TargetAdmin
        };
        Assert.All(targets, t => Assert.DoesNotContain('.', t));
    }
}

internal static class TestAdmins
{
    internal static Admin Create(
        int id = 1,
        string username = "admin",
        AdminRole role = AdminRole.Super,
        AdminStatus status = AdminStatus.Enabled)
    {
        var admin = Admin.Create(username, PasswordHash.FromHash("$2a$11$fakehash"), role, T.Now);
        admin.AssignPersistedId(id);

        if (status == AdminStatus.Disabled)
        {
            // 走公开行为达成停用状态（操作者用另一个 ID，避开"不能停用自己"）
            admin.ChangeRoleAndStatus(null, AdminStatus.Disabled, operatorId: id + 1000);
        }

        return admin;
    }
}
