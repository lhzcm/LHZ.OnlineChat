using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Users;
using Microsoft.Extensions.Logging;

namespace LHZ.OnlineChat.Application.Users.EventHandlers;

/// <summary>
/// 口令变更 → 使该账号全部会话失效（API 立即 401 + WS 收到 kicked 后自动登出）。
///
/// 改造前这行 `await LogoutAllSessionsAsync(userId)` 在四处被分别调用：
/// AuthService.ChangePasswordAsync、AuthService.ForgotPasswordAsync、
/// AdminService.ResetUserPasswordAsync，漏一处就是一个安全缺口。
/// 现在只要口令变了，事件必然发出，副作用必然执行。
/// </summary>
internal sealed class TerminateSessionsOnPasswordChanged : DomainEventHandler<UserPasswordChanged>
{
    private readonly ISessionTerminator _terminator;
    private readonly ILogger<TerminateSessionsOnPasswordChanged> _logger;

    public TerminateSessionsOnPasswordChanged(
        ISessionTerminator terminator, ILogger<TerminateSessionsOnPasswordChanged> logger)
    {
        _terminator = terminator;
        _logger = logger;
    }

    protected override async Task HandleAsync(UserPasswordChanged e, CancellationToken ct)
    {
        await _terminator.TerminateAllAsync(e.UserId, ct).ConfigureAwait(false);
        _logger.LogInformation("用户 {UserId} 口令变更（{Reason}），已终止全部登录会话", e.UserId, e.Reason);
    }
}

/// <summary>
/// 封禁 → 立即踢掉该用户所有设备。
/// 封禁后「登录被拒」由 User.EnsureCanLogin 保证，两者合起来构成完整闭环。
/// </summary>
internal sealed class TerminateSessionsOnUserBanned : DomainEventHandler<UserBanned>
{
    private readonly ISessionTerminator _terminator;
    private readonly ILogger<TerminateSessionsOnUserBanned> _logger;

    public TerminateSessionsOnUserBanned(
        ISessionTerminator terminator, ILogger<TerminateSessionsOnUserBanned> logger)
    {
        _terminator = terminator;
        _logger = logger;
    }

    protected override async Task HandleAsync(UserBanned e, CancellationToken ct)
    {
        await _terminator.TerminateAllAsync(e.UserId, ct).ConfigureAwait(false);
        _logger.LogInformation("用户 {UserId}（{Nickname}）被封禁，已踢下线全部设备", e.UserId, e.Nickname);
    }
}
