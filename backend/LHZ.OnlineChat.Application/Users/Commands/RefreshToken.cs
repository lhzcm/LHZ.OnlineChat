using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Users.Commands;

/// <summary>
/// 刷新访问令牌：会话 ID 不变（多端互不影响），轮换 RefreshToken。
/// </summary>
public sealed class RefreshTokenCommand : ICommand<ApiResponse<LoginResponse>>
{
    public string RefreshToken { get; set; } = string.Empty;
}

internal sealed class RefreshTokenHandler : IRequestHandler<RefreshTokenCommand, ApiResponse<LoginResponse>>
{
    private const string InvalidToken = "RefreshToken 无效或已过期";

    private readonly IUserRepository _users;
    private readonly ITokenIssuer _tokens;
    private readonly ISessionStore _sessions;

    public RefreshTokenHandler(IUserRepository users, ITokenIssuer tokens, ISessionStore sessions)
    {
        _users = users;
        _tokens = tokens;
        _sessions = sessions;
    }

    public async Task<ApiResponse<LoginResponse>> Handle(RefreshTokenCommand command, CancellationToken ct)
    {
        DomainException.Ensure(!string.IsNullOrWhiteSpace(command.RefreshToken), "RefreshToken 不能为空");

        // O(1) 反查：令牌哈希 → userId:sessionId
        var lookup = await _sessions.LookupByRefreshTokenAsync(command.RefreshToken, ct).ConfigureAwait(false);
        DomainException.Ensure(lookup is not null, InvalidToken);

        // 二次校验：该令牌必须仍是这个会话当前有效的刷新令牌（防止已轮换的旧令牌复用）
        var current = await _sessions.GetCurrentRefreshTokenAsync(lookup!.SessionId, ct).ConfigureAwait(false);
        DomainException.Ensure(current == command.RefreshToken, InvalidToken);

        var user = await _users.FindByIdAsync(lookup.UserId, ct).ConfigureAwait(false);
        DomainException.Ensure(user is not null, "用户不存在");

        var token = _tokens.IssueUserToken(user!, lookup.SessionId);
        var newRefreshToken = _tokens.GenerateRefreshToken();

        await _sessions.RemoveRefreshLookupAsync(command.RefreshToken, ct).ConfigureAwait(false);
        await _sessions.StoreRefreshTokenAsync(user!.Id, lookup.SessionId, newRefreshToken, ct).ConfigureAwait(false);
        await _sessions.TouchAsync(lookup.SessionId, ct).ConfigureAwait(false);

        return ApiResponse<LoginResponse>.Ok(new LoginResponse
        {
            Token = token,
            RefreshToken = newRefreshToken,
            User = user.ToInfo()
        });
    }
}
