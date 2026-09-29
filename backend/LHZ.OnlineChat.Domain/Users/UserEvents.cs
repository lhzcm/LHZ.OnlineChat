using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Users;

/// <summary>注册成功（账号 ID 已分配）</summary>
public sealed record UserRegistered(int UserId, string Nickname, DateTime OccurredAt) : IDomainEvent;

/// <summary>被管理后台封禁 —— 订阅方负责踢掉全部登录设备</summary>
public sealed record UserBanned(int UserId, string Nickname, string? Reason, DateTime OccurredAt) : IDomainEvent;

/// <summary>解除封禁</summary>
public sealed record UserUnbanned(int UserId, string Nickname, DateTime OccurredAt) : IDomainEvent;

/// <summary>
/// 口令发生变更（自助改密 / 忘记密码重置 / 管理员重置）。
/// 订阅方负责使该账号所有会话失效，原先这一步在三处各写一遍。
/// </summary>
public sealed record UserPasswordChanged(int UserId, PasswordChangeReason Reason, DateTime OccurredAt) : IDomainEvent;

/// <summary>口令变更来源</summary>
public enum PasswordChangeReason
{
    /// <summary>用户自助修改（验证原密码）</summary>
    SelfService = 0,

    /// <summary>忘记密码，邮箱验证码重置</summary>
    ForgotPassword = 1,

    /// <summary>管理后台强制重置</summary>
    AdminReset = 2
}

/// <summary>换绑邮箱成功</summary>
public sealed record UserEmailChanged(int UserId, string NewEmail, DateTime OccurredAt) : IDomainEvent;

/// <summary>机器人账号已创建（IsBot=true，禁止登录）</summary>
public sealed record BotAccountCreated(int UserId, int OwnerId, string Name, DateTime OccurredAt) : IDomainEvent;
