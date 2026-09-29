using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Users;

/// <summary>
/// 用户聚合根。Id 即登录账号（int 自增，起始 10000）。
/// 机器人账号也是 User（IsBot=true、无邮箱无口令、禁止登录），由 Robots 上下文创建。
/// </summary>
public sealed class User : AggregateRoot<int>
{
    /// <summary>仅供 ORM 物化</summary>
    private User() { }

    public string Nickname { get; private set; } = string.Empty;

    /// <summary>机器人账号为 null</summary>
    public Email? Email { get; private set; }

    /// <summary>机器人账号为 null（因此永远无法通过口令校验，天然禁止登录）</summary>
    public PasswordHash? PasswordHash { get; private set; }

    public string? Avatar { get; private set; }

    public bool IsBot { get; private set; }

    public bool IsBanned { get; private set; }

    public string? BanReason { get; private set; }

    public DateTime? BannedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    // ==================== 工厂 ====================

    /// <summary>注册真人账号</summary>
    public static User Register(string nickname, Email email, PasswordHash passwordHash, DateTime now)
    {
        var user = new User
        {
            Nickname = NicknameRules.Normalize(nickname),
            Email = email,
            PasswordHash = passwordHash,
            IsBot = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        // Id 此刻还是 0，仓储持久化后会回填；事件在持久化后由应用层重新构造携带真实 Id
        return user;
    }

    /// <summary>创建机器人账号（无邮箱/无口令 → 不可登录）</summary>
    public static User CreateBot(string name, string? avatar, DateTime now)
        => new()
        {
            Nickname = NicknameRules.Normalize(name),
            Email = null,
            PasswordHash = null,
            Avatar = avatar,
            IsBot = true,
            CreatedAt = now,
            UpdatedAt = now
        };

    /// <summary>持久化拿到自增主键后补发注册事件（Id 已就绪）</summary>
    public void ConfirmRegistration(DateTime now) => Raise(new UserRegistered(Id, Nickname, now));

    /// <summary>持久化拿到自增主键后补发机器人创建事件</summary>
    public void ConfirmBotCreation(int ownerId, DateTime now) => Raise(new BotAccountCreated(Id, ownerId, Nickname, now));

    // ==================== 登录相关不变量 ====================

    /// <summary>
    /// 校验该账号当前是否允许登录。
    /// 原先散落在 AuthService.LoginAsync 的三段 if，现在是账号自己的职责。
    /// </summary>
    public void EnsureCanLogin()
    {
        DomainException.Ensure(!IsBot, "机器人账号不能登录");
        DomainException.Ensure(!IsBanned, BanReason ?? "账号已被封禁，请联系管理员");
        DomainException.Ensure(PasswordHash is not null, "该账号未设置密码，无法登录");
    }

    // ==================== 领域行为 ====================

    public void Rename(string nickname, DateTime now)
    {
        Nickname = NicknameRules.Normalize(nickname);
        UpdatedAt = now;
    }

    public void ChangeAvatar(string? avatarUrl, DateTime now)
    {
        Avatar = avatarUrl;
        UpdatedAt = now;
    }

    public void ChangeEmail(Email newEmail, DateTime now)
    {
        DomainException.Ensure(!IsBot, "机器人账号没有邮箱");
        Email = newEmail;
        UpdatedAt = now;
        Raise(new UserEmailChanged(Id, newEmail.Value, now));
    }

    /// <summary>
    /// 设置新口令。会发出 UserPasswordChanged —— 订阅方负责让所有会话失效。
    /// 「原密码是否正确」由应用层用 IPasswordHasher 校验后再调用此方法。
    /// </summary>
    public void SetPassword(PasswordHash newHash, PasswordChangeReason reason, DateTime now)
    {
        DomainException.Ensure(!IsBot, "机器人账号无密码");
        PasswordHash = newHash;
        UpdatedAt = now;
        Raise(new UserPasswordChanged(Id, reason, now));
    }

    public void Ban(string? reason, DateTime now)
    {
        DomainException.Ensure(!IsBot, "机器人账号不支持封禁，请直接删除机器人");
        IsBanned = true;
        BanReason = reason;
        BannedAt = now;
        UpdatedAt = now;
        Raise(new UserBanned(Id, Nickname, reason, now));
    }

    public void Unban(DateTime now)
    {
        IsBanned = false;
        BanReason = null;
        BannedAt = null;
        UpdatedAt = now;
        Raise(new UserUnbanned(Id, Nickname, now));
    }
}
