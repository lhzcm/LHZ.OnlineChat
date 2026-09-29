using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Users.Queries;

/// <summary>取当前登录用户信息</summary>
public sealed class GetCurrentUserQuery : IQuery<ApiResponse<UserInfo>>
{
    public int UserId { get; set; }
}

internal sealed class GetCurrentUserHandler : IRequestHandler<GetCurrentUserQuery, ApiResponse<UserInfo>>
{
    private readonly IUserRepository _users;

    public GetCurrentUserHandler(IUserRepository users) => _users = users;

    public async Task<ApiResponse<UserInfo>> Handle(GetCurrentUserQuery query, CancellationToken ct)
    {
        DomainException.Ensure(query.UserId > 0, "无效的 Token");

        var user = await _users.FindByIdAsync(query.UserId, ct).ConfigureAwait(false)
                   ?? throw new EntityNotFoundException("用户不存在");

        return ApiResponse<UserInfo>.Ok(user.ToInfo());
    }
}

/// <summary>取当前账号的全部登录设备（按最后活跃倒序）</summary>
public sealed class GetLoginSessionsQuery : IQuery<ApiResponse<List<SessionInfoDto>>>
{
    public int UserId { get; set; }

    /// <summary>当前请求所在会话，用于标记「本设备」</summary>
    public string CurrentSessionId { get; set; } = string.Empty;
}

internal sealed class GetLoginSessionsHandler
    : IRequestHandler<GetLoginSessionsQuery, ApiResponse<List<SessionInfoDto>>>
{
    private readonly ISessionStore _sessions;

    public GetLoginSessionsHandler(ISessionStore sessions) => _sessions = sessions;

    public async Task<ApiResponse<List<SessionInfoDto>>> Handle(GetLoginSessionsQuery query, CancellationToken ct)
    {
        var sessions = await _sessions.ListSessionsAsync(query.UserId, ct).ConfigureAwait(false);

        var items = sessions
            .Select(s => s.ToDto(query.CurrentSessionId))
            .OrderByDescending(s => s.LastActiveAt)
            .ToList();

        return ApiResponse<List<SessionInfoDto>>.Ok(items);
    }
}
