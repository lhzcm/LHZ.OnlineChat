using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Application.Friends;
using LHZ.OnlineChat.Domain.Blacklists;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Friends;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Blacklists;

/// <summary>拉黑用户（自动解除好友关系 + 通知被拉黑者）</summary>
public sealed class BlockUserCommand : ICommand<ApiResponse>
{
    public int UserId { get; set; }

    /// <summary>被拉黑者账号 ID</summary>
    public int BlockedUserId { get; set; }
}

internal sealed class BlockUserHandler : IRequestHandler<BlockUserCommand, ApiResponse>
{
    private readonly IBlacklistRepository _blacklist;
    private readonly IUserRepository _users;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public BlockUserHandler(
        IBlacklistRepository blacklist,
        IUserRepository users,
        IDomainEventDispatcher events,
        IClock clock)
    {
        _blacklist = blacklist;
        _users = users;
        _events = events;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(BlockUserCommand command, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        // 「不能拉黑自己」「ID 必须有效」两条规则在工厂里
        var entry = BlacklistEntry.Create(command.UserId, command.BlockedUserId, now);

        var target = await _users.FindByIdAsync(command.BlockedUserId, ct).ConfigureAwait(false);
        DomainException.Ensure(target is not null, "用户不存在");

        var already = await _blacklist
            .ExistsAsync(command.UserId, command.BlockedUserId, ct)
            .ConfigureAwait(false);
        DomainException.Ensure(!already, "该用户已在黑名单中");

        await _blacklist.AddAsync(entry, ct).ConfigureAwait(false);

        // 解除好友关系与推送通知都交给事件订阅方 ——
        // 改造前「解除好友」写在 BlacklistService 里，而「推送通知」写在 Controller 里，
        // 一个业务规则被劈成了两半。
        await _events
            .DispatchAsync(new UserBlocked(command.UserId, command.BlockedUserId, now), ct)
            .ConfigureAwait(false);

        return ApiResponse.Ok("已拉黑");
    }
}

/// <summary>解除拉黑</summary>
public sealed class UnblockUserCommand : ICommand<ApiResponse>
{
    public int UserId { get; set; }

    public int BlockedUserId { get; set; }
}

internal sealed class UnblockUserHandler : IRequestHandler<UnblockUserCommand, ApiResponse>
{
    private readonly IBlacklistRepository _blacklist;

    public UnblockUserHandler(IBlacklistRepository blacklist) => _blacklist = blacklist;

    public async Task<ApiResponse> Handle(UnblockUserCommand command, CancellationToken ct)
    {
        var removed = await _blacklist
            .RemoveAsync(command.UserId, command.BlockedUserId, ct)
            .ConfigureAwait(false);

        DomainException.Ensure(removed > 0, "该用户不在你的黑名单中");
        return ApiResponse.Ok("已解除拉黑");
    }
}

/// <summary>我的黑名单列表</summary>
public sealed class GetBlacklistQuery : IQuery<ApiResponse<List<BlacklistUserDto>>>
{
    public int UserId { get; set; }
}

internal sealed class GetBlacklistHandler : IRequestHandler<GetBlacklistQuery, ApiResponse<List<BlacklistUserDto>>>
{
    private readonly IBlacklistRepository _blacklist;
    private readonly IUserRepository _users;

    public GetBlacklistHandler(IBlacklistRepository blacklist, IUserRepository users)
    {
        _blacklist = blacklist;
        _users = users;
    }

    public async Task<ApiResponse<List<BlacklistUserDto>>> Handle(GetBlacklistQuery query, CancellationToken ct)
    {
        var entries = await _blacklist.ListOfAsync(query.UserId, ct).ConfigureAwait(false);
        if (entries.Count == 0)
            return ApiResponse<List<BlacklistUserDto>>.Ok(new List<BlacklistUserDto>());

        var users = await _users
            .GetManyAsync(entries.Select(e => e.BlockedUserId), ct)
            .ConfigureAwait(false);

        var items = entries.Select(e => new BlacklistUserDto
        {
            UserId = e.BlockedUserId,
            Nickname = users.TryGetValue(e.BlockedUserId, out var u) ? u.Nickname : "未知",
            Avatar = users.TryGetValue(e.BlockedUserId, out var u2) ? u2.Avatar : null,
            BlockedAt = UtcTime.Normalize(e.CreatedAt)
        }).ToList();

        return ApiResponse<List<BlacklistUserDto>>.Ok(items);
    }
}

/// <summary>
/// 拉黑 → 解除双向好友关系 + 通知被拉黑者。
/// </summary>
internal sealed class HandleUserBlocked : DomainEventHandler<UserBlocked>
{
    private readonly IFriendshipRepository _friendships;
    private readonly IRealtimeNotifier _notifier;

    public HandleUserBlocked(IFriendshipRepository friendships, IRealtimeNotifier notifier)
    {
        _friendships = friendships;
        _notifier = notifier;
    }

    protected override async Task HandleAsync(UserBlocked e, CancellationToken ct)
    {
        await _friendships
            .DeleteAcceptedBetweenAsync(e.BlockerId, e.BlockedUserId, ct)
            .ConfigureAwait(false);

        await _notifier
            .NotifyBlockedAsync(e.BlockedUserId, e.BlockerId, "你已被对方拉黑", ct)
            .ConfigureAwait(false);
    }
}
