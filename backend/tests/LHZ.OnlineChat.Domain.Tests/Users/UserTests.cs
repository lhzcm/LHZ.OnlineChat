using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;

namespace LHZ.OnlineChat.Domain.Tests.Users;

public class UserRegistrationTests
{
    private static readonly Email AnyEmail = Email.Parse("a@test.local");
    private static readonly PasswordHash AnyHash = PasswordHash.FromHash("$2a$11$fakehash");

    [Fact]
    public void Register_归一化昵称并记录时间()
    {
        var user = User.Register("  张三  ", AnyEmail, AnyHash, T.Now);

        Assert.Equal("张三", user.Nickname);
        Assert.Equal(AnyEmail, user.Email);
        Assert.Equal(AnyHash, user.PasswordHash);
        Assert.False(user.IsBot);
        Assert.False(user.IsBanned);
        Assert.Equal(T.Now, user.CreatedAt);
        Assert.Equal(T.Now, user.UpdatedAt);
    }

    [Fact]
    public void Register_此刻还没有账号ID_也还没有事件()
    {
        // 自增主键要等仓储插入后才有，事件必须等到那时才发（否则携带 Id=0）
        var user = User.Register("张三", AnyEmail, AnyHash, T.Now);

        Assert.True(user.IsTransient);
        Assert.Empty(user.DequeueDomainEvents());
    }

    [Fact]
    public void ConfirmRegistration_在主键回填后发出携带真实ID的事件()
    {
        var user = User.Register("张三", AnyEmail, AnyHash, T.Now);
        user.AssignPersistedId(10001);

        user.ConfirmRegistration(T.Now);

        var e = Assert.Single(user.DequeueDomainEvents());
        var registered = Assert.IsType<UserRegistered>(e);
        Assert.Equal(10001, registered.UserId);
        Assert.Equal("张三", registered.Nickname);
        Assert.Equal(T.Now, registered.OccurredAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Register_昵称为空时抛出(string? nickname)
    {
        var ex = Assert.Throws<DomainException>(
            () => User.Register(nickname!, AnyEmail, AnyHash, T.Now));

        Assert.Equal("昵称不能为空", ex.Message);
    }

    [Fact]
    public void Register_昵称超长时抛出()
    {
        var ex = Assert.Throws<DomainException>(
            () => User.Register(new string('长', 51), AnyEmail, AnyHash, T.Now));

        Assert.Equal("昵称长度不能超过 50 个字符", ex.Message);
    }

    [Fact]
    public void Register_昵称恰好50字符时通过()
    {
        var user = User.Register(new string('长', 50), AnyEmail, AnyHash, T.Now);

        Assert.Equal(50, user.Nickname.Length);
    }

    [Fact]
    public void CreateBot_无邮箱无口令且标记为机器人()
    {
        var bot = User.CreateBot("助理", "/uploads/bot.png", T.Now);

        Assert.True(bot.IsBot);
        Assert.Null(bot.Email);
        Assert.Null(bot.PasswordHash);
        Assert.Equal("助理", bot.Nickname);
        Assert.Equal("/uploads/bot.png", bot.Avatar);
    }

    [Fact]
    public void ConfirmBotCreation_发出携带创建者的事件()
    {
        var bot = User.CreateBot("助理", null, T.Now);
        bot.AssignPersistedId(10005);

        bot.ConfirmBotCreation(ownerId: 10001, T.Now);

        var e = Assert.Single(bot.DequeueDomainEvents());
        var created = Assert.IsType<BotAccountCreated>(e);
        Assert.Equal(10005, created.UserId);
        Assert.Equal(10001, created.OwnerId);
        Assert.Equal("助理", created.Name);
    }
}

public class UserLoginRulesTests
{
    [Fact]
    public void EnsureCanLogin_正常账号通过()
    {
        var user = TestUsers.Normal();

        user.EnsureCanLogin();
    }

    [Fact]
    public void EnsureCanLogin_机器人账号被拒()
    {
        var bot = TestUsers.Bot();

        var ex = Assert.Throws<DomainException>(bot.EnsureCanLogin);

        Assert.Equal("机器人账号不能登录", ex.Message);
    }

    [Fact]
    public void EnsureCanLogin_封禁账号被拒且提示语为封禁原因()
    {
        var user = TestUsers.Normal();
        user.Ban("发布违规内容", T.Now);

        var ex = Assert.Throws<DomainException>(user.EnsureCanLogin);

        Assert.Equal("发布违规内容", ex.Message);
    }

    [Fact]
    public void EnsureCanLogin_封禁未填原因时用兜底提示语()
    {
        var user = TestUsers.Normal();
        user.Ban(null, T.Now);

        var ex = Assert.Throws<DomainException>(user.EnsureCanLogin);

        Assert.Equal("账号已被封禁，请联系管理员", ex.Message);
    }

    [Fact]
    public void EnsureCanLogin_机器人判定优先于封禁判定()
    {
        // 机器人不可能被封禁（Ban 会抛），所以顺序上机器人先被拦下
        var bot = TestUsers.Bot();

        var ex = Assert.Throws<DomainException>(bot.EnsureCanLogin);

        Assert.Equal("机器人账号不能登录", ex.Message);
    }
}

public class UserBehaviourTests
{
    [Fact]
    public void Rename_归一化并推进更新时间()
    {
        var user = TestUsers.Normal();
        var later = T.PlusMinutes(5);

        user.Rename("  李四 ", later);

        Assert.Equal("李四", user.Nickname);
        Assert.Equal(later, user.UpdatedAt);
    }

    [Fact]
    public void ChangeAvatar_可设置也可清空()
    {
        var user = TestUsers.Normal();

        user.ChangeAvatar("/uploads/a.png", T.Now);
        Assert.Equal("/uploads/a.png", user.Avatar);

        user.ChangeAvatar(null, T.Now);
        Assert.Null(user.Avatar);
    }

    [Fact]
    public void ChangeEmail_更新并发出事件()
    {
        var user = TestUsers.Normal();
        var newEmail = Email.Parse("new@test.local");

        user.ChangeEmail(newEmail, T.Now);

        Assert.Equal(newEmail, user.Email);
        var changed = Assert.IsType<UserEmailChanged>(Assert.Single(user.DequeueDomainEvents()));
        Assert.Equal("new@test.local", changed.NewEmail);
        Assert.Equal(user.Id, changed.UserId);
    }

    [Fact]
    public void ChangeEmail_机器人账号被拒()
    {
        var bot = TestUsers.Bot();

        var ex = Assert.Throws<DomainException>(
            () => bot.ChangeEmail(Email.Parse("x@test.local"), T.Now));

        Assert.Equal("机器人账号没有邮箱", ex.Message);
    }

    [Theory]
    [InlineData(PasswordChangeReason.SelfService)]
    [InlineData(PasswordChangeReason.ForgotPassword)]
    [InlineData(PasswordChangeReason.AdminReset)]
    public void SetPassword_三条路径都发出同一个事件(PasswordChangeReason reason)
    {
        // 这正是改造要解决的问题：只要口令变了，"踢掉全部会话"就必然被触发，
        // 不再依赖三处调用方各自记得去调
        var user = TestUsers.Normal();
        var newHash = PasswordHash.FromHash("$2a$11$newhash");

        user.SetPassword(newHash, reason, T.Now);

        Assert.Equal(newHash, user.PasswordHash);
        var changed = Assert.IsType<UserPasswordChanged>(Assert.Single(user.DequeueDomainEvents()));
        Assert.Equal(reason, changed.Reason);
        Assert.Equal(user.Id, changed.UserId);
    }

    [Fact]
    public void SetPassword_机器人账号被拒()
    {
        var bot = TestUsers.Bot();

        var ex = Assert.Throws<DomainException>(() => bot.SetPassword(
            PasswordHash.FromHash("$2a$11$x"), PasswordChangeReason.AdminReset, T.Now));

        Assert.Equal("机器人账号无密码", ex.Message);
    }

    [Fact]
    public void Ban_记录原因时间并发出事件()
    {
        var user = TestUsers.Normal();

        user.Ban("刷屏", T.Now);

        Assert.True(user.IsBanned);
        Assert.Equal("刷屏", user.BanReason);
        Assert.Equal(T.Now, user.BannedAt);
        var banned = Assert.IsType<UserBanned>(Assert.Single(user.DequeueDomainEvents()));
        Assert.Equal("刷屏", banned.Reason);
    }

    [Fact]
    public void Ban_机器人账号被拒()
    {
        var bot = TestUsers.Bot();

        var ex = Assert.Throws<DomainException>(() => bot.Ban("x", T.Now));

        Assert.Equal("机器人账号不支持封禁，请直接删除机器人", ex.Message);
    }

    [Fact]
    public void Unban_清空封禁字段并发出事件()
    {
        var user = TestUsers.Normal();
        user.Ban("刷屏", T.Now);
        user.DequeueDomainEvents();

        user.Unban(T.PlusMinutes(10));

        Assert.False(user.IsBanned);
        Assert.Null(user.BanReason);
        Assert.Null(user.BannedAt);
        Assert.IsType<UserUnbanned>(Assert.Single(user.DequeueDomainEvents()));
    }

    [Fact]
    public void Unban_解封后可重新登录()
    {
        var user = TestUsers.Normal();
        user.Ban("刷屏", T.Now);
        user.Unban(T.Now);

        user.EnsureCanLogin();
    }
}

/// <summary>构造处于指定状态的 User（含回填主键）</summary>
internal static class TestUsers
{
    internal static User Normal(int id = 10001, string nickname = "张三", string email = "a@test.local")
    {
        var user = User.Register(
            nickname, Email.Parse(email), PasswordHash.FromHash("$2a$11$fakehash"), T.Now);
        user.AssignPersistedId(id);
        user.DequeueDomainEvents();
        return user;
    }

    internal static User Bot(int id = 10005, string name = "助理")
    {
        var bot = User.CreateBot(name, null, T.Now);
        bot.AssignPersistedId(id);
        bot.DequeueDomainEvents();
        return bot;
    }
}
