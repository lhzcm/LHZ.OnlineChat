using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using MediatR;

namespace LHZ.OnlineChat.Application.Users.Commands;

/// <summary>踢下线指定设备（只能踢自己的会话）</summary>
public sealed class KickSessionCommand : ICommand<ApiResponse>
{
    public int UserId { get; set; }

    public string SessionId { get; set; } = string.Empty;
}

internal sealed class KickSessionHandler : IRequestHandler<KickSessionCommand, ApiResponse>
{
    private readonly ISessionStore _sessions;
    private readonly ISessionTerminator _terminator;

    public KickSessionHandler(ISessionStore sessions, ISessionTerminator terminator)
    {
        _sessions = sessions;
        _terminator = terminator;
    }

    public async Task<ApiResponse> Handle(KickSessionCommand command, CancellationToken ct)
    {
        var owned = await _sessions.BelongsToUserAsync(command.UserId, command.SessionId, ct).ConfigureAwait(false);
        DomainException.Ensure(owned, "会话不存在");

        await _terminator.TerminateAsync(command.UserId, command.SessionId, ct).ConfigureAwait(false);
        return ApiResponse.Ok("该设备已下线");
    }
}

/// <summary>退出其他所有设备（保留当前设备）</summary>
public sealed class LogoutOtherSessionsCommand : ICommand<ApiResponse>
{
    public int UserId { get; set; }

    public string CurrentSessionId { get; set; } = string.Empty;
}

internal sealed class LogoutOtherSessionsHandler : IRequestHandler<LogoutOtherSessionsCommand, ApiResponse>
{
    private readonly ISessionTerminator _terminator;

    public LogoutOtherSessionsHandler(ISessionTerminator terminator) => _terminator = terminator;

    public async Task<ApiResponse> Handle(LogoutOtherSessionsCommand command, CancellationToken ct)
    {
        var kicked = await _terminator
            .TerminateOthersAsync(command.UserId, command.CurrentSessionId, ct)
            .ConfigureAwait(false);

        return ApiResponse.Ok(kicked > 0 ? $"已退出 {kicked} 台设备" : "没有其他在线设备");
    }
}
