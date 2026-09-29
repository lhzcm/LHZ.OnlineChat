using System.Globalization;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Admins.Commands;

/// <summary>
/// 封禁/解封用户。
/// 「踢掉全部设备」由 UserBanned 事件的订阅方完成，这里只表达业务意图。
/// </summary>
public sealed class BanUserCommand : ICommand<ApiResponse>
{
    public int AdminId { get; set; }

    public int UserId { get; set; }

    public bool Banned { get; set; }

    public string? Reason { get; set; }
}

internal sealed class BanUserHandler : IRequestHandler<BanUserCommand, ApiResponse>
{
    private readonly IUserRepository _users;
    private readonly IDomainEventDispatcher _events;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;

    public BanUserHandler(
        IUserRepository users, IDomainEventDispatcher events, IAuditLogger audit, IClock clock)
    {
        _users = users;
        _events = events;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(BanUserCommand command, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(command.UserId, ct).ConfigureAwait(false)
                   ?? throw new EntityNotFoundException("用户不存在");

        var now = _clock.UtcNow;
        var targetId = command.UserId.ToString(CultureInfo.InvariantCulture);

        if (command.Banned)
        {
            // 「机器人不支持封禁」在聚合根里
            user.Ban(command.Reason, now);
            await _users.UpdateAsync(user, ct).ConfigureAwait(false);
            await _events.DispatchEventsOfAsync(user, ct).ConfigureAwait(false);

            await _audit.RecordAsync(
                command.AdminId, AuditActions.UserBan, AuditActions.TargetUser, targetId,
                $"封禁用户 {user.Nickname}（原因：{command.Reason ?? "未填写"}）", ct).ConfigureAwait(false);

            return ApiResponse.Ok("已封禁，该用户所有设备已下线");
        }

        user.Unban(now);
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);
        await _events.DispatchEventsOfAsync(user, ct).ConfigureAwait(false);

        await _audit.RecordAsync(
            command.AdminId, AuditActions.UserUnban, AuditActions.TargetUser, targetId,
            $"解封用户 {user.Nickname}", ct).ConfigureAwait(false);

        return ApiResponse.Ok("已解封");
    }
}

/// <summary>强制下线（不动账号状态）</summary>
public sealed class KickUserCommand : ICommand<ApiResponse>
{
    public int AdminId { get; set; }

    public int UserId { get; set; }
}

internal sealed class KickUserHandler : IRequestHandler<KickUserCommand, ApiResponse>
{
    private readonly IUserRepository _users;
    private readonly ISessionTerminator _terminator;
    private readonly IAuditLogger _audit;

    public KickUserHandler(IUserRepository users, ISessionTerminator terminator, IAuditLogger audit)
    {
        _users = users;
        _terminator = terminator;
        _audit = audit;
    }

    public async Task<ApiResponse> Handle(KickUserCommand command, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(command.UserId, ct).ConfigureAwait(false)
                   ?? throw new EntityNotFoundException("用户不存在");

        await _terminator.TerminateAllAsync(command.UserId, ct).ConfigureAwait(false);

        await _audit.RecordAsync(
            command.AdminId, AuditActions.UserKick, AuditActions.TargetUser,
            command.UserId.ToString(CultureInfo.InvariantCulture),
            $"强制下线用户 {user.Nickname}", ct).ConfigureAwait(false);

        return ApiResponse.Ok("该用户所有设备已下线");
    }
}

/// <summary>重置用户密码（免验证码；旧会话由 UserPasswordChanged 订阅方清理）</summary>
public sealed class ResetUserPasswordCommand : ICommand<ApiResponse>
{
    public int AdminId { get; set; }

    public int UserId { get; set; }

    public string NewPassword { get; set; } = string.Empty;
}

internal sealed class ResetUserPasswordHandler : IRequestHandler<ResetUserPasswordCommand, ApiResponse>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IDomainEventDispatcher _events;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;

    public ResetUserPasswordHandler(
        IUserRepository users,
        IPasswordHasher hasher,
        IDomainEventDispatcher events,
        IAuditLogger audit,
        IClock clock)
    {
        _users = users;
        _hasher = hasher;
        _events = events;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(ResetUserPasswordCommand command, CancellationToken ct)
    {
        PasswordHash.EnsureRawPasswordValid(command.NewPassword);

        var user = await _users.FindByIdAsync(command.UserId, ct).ConfigureAwait(false)
                   ?? throw new EntityNotFoundException("用户不存在");

        // 「机器人账号无密码」在聚合根里
        user.SetPassword(
            _hasher.Hash(command.NewPassword), PasswordChangeReason.AdminReset, _clock.UtcNow);

        await _users.UpdateAsync(user, ct).ConfigureAwait(false);
        await _events.DispatchEventsOfAsync(user, ct).ConfigureAwait(false);

        await _audit.RecordAsync(
            command.AdminId, AuditActions.UserResetPassword, AuditActions.TargetUser,
            command.UserId.ToString(CultureInfo.InvariantCulture),
            $"重置用户 {user.Nickname} 的密码", ct).ConfigureAwait(false);

        return ApiResponse.Ok("密码已重置，该用户需重新登录");
    }
}
