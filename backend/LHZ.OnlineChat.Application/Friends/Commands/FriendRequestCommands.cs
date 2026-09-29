using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Blacklists;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Friends;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Friends.Commands;

/// <summary>发送好友申请（按账号 ID）</summary>
public sealed class SendFriendRequestCommand : ICommand<ApiResponse>
{
    public int RequesterId { get; set; }

    /// <summary>目标账号 ID</summary>
    public int AccountId { get; set; }
}

internal sealed class SendFriendRequestHandler : IRequestHandler<SendFriendRequestCommand, ApiResponse>
{
    private readonly IUserRepository _users;
    private readonly IFriendshipRepository _friendships;
    private readonly IBlacklistRepository _blacklist;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public SendFriendRequestHandler(
        IUserRepository users,
        IFriendshipRepository friendships,
        IBlacklistRepository blacklist,
        IDomainEventDispatcher events,
        IClock clock)
    {
        _users = users;
        _friendships = friendships;
        _blacklist = blacklist;
        _events = events;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(SendFriendRequestCommand command, CancellationToken ct)
    {
        DomainException.Ensure(command.AccountId > 0, "请输入正确的账号 ID");

        var target = await _users.FindByIdAsync(command.AccountId, ct).ConfigureAwait(false);
        DomainException.Ensure(target is not null, "用户不存在");

        // 黑名单：任一方向存在拉黑都不允许申请
        var blocked = await _blacklist
            .ExistsEitherDirectionAsync(command.RequesterId, target!.Id, ct)
            .ConfigureAwait(false);
        DomainException.Ensure(!blocked, "无法发送好友申请（你或对方已在黑名单中）");

        // 已存在关系时，提示语由关系自身的状态决定
        var existing = await _friendships
            .FindBetweenAsync(command.RequesterId, target.Id, ct)
            .ConfigureAwait(false);
        if (existing is not null) throw new DomainException(existing.DescribeRejectionOfNewRequest());

        var now = _clock.UtcNow;
        var friendship = Friendship.Request(command.RequesterId, target.Id, now);
        await _friendships.AddAsync(friendship, ct).ConfigureAwait(false);

        await _events
            .DispatchAsync(new FriendRequestSent(command.RequesterId, target.Id, now), ct)
            .ConfigureAwait(false);

        return ApiResponse.Ok("好友申请已发送");
    }
}

/// <summary>接受好友申请</summary>
public sealed class AcceptFriendRequestCommand : ICommand<ApiResponse>
{
    public long RequestId { get; set; }

    public int OperatorId { get; set; }
}

internal sealed class AcceptFriendRequestHandler : IRequestHandler<AcceptFriendRequestCommand, ApiResponse>
{
    private readonly IFriendshipRepository _friendships;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public AcceptFriendRequestHandler(
        IFriendshipRepository friendships, IDomainEventDispatcher events, IClock clock)
    {
        _friendships = friendships;
        _events = events;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(AcceptFriendRequestCommand command, CancellationToken ct)
    {
        var friendship = await _friendships.FindByIdAsync(command.RequestId, ct).ConfigureAwait(false)
                         ?? throw new EntityNotFoundException("申请不存在");

        // 「只有被申请方能接受」「必须仍待确认」两条规则在聚合根里
        friendship.Accept(command.OperatorId, _clock.UtcNow);

        await _friendships.UpdateAsync(friendship, ct).ConfigureAwait(false);
        await _events.DispatchEventsOfAsync(friendship, ct).ConfigureAwait(false);

        return ApiResponse.Ok("已添加为好友");
    }
}

/// <summary>拒绝好友申请（删除申请记录）</summary>
public sealed class RejectFriendRequestCommand : ICommand<ApiResponse>
{
    public long RequestId { get; set; }

    public int OperatorId { get; set; }
}

internal sealed class RejectFriendRequestHandler : IRequestHandler<RejectFriendRequestCommand, ApiResponse>
{
    private readonly IFriendshipRepository _friendships;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public RejectFriendRequestHandler(
        IFriendshipRepository friendships, IDomainEventDispatcher events, IClock clock)
    {
        _friendships = friendships;
        _events = events;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(RejectFriendRequestCommand command, CancellationToken ct)
    {
        var friendship = await _friendships.FindByIdAsync(command.RequestId, ct).ConfigureAwait(false)
                         ?? throw new EntityNotFoundException("申请不存在");

        friendship.EnsureCanReject(command.OperatorId);

        var requesterId = friendship.UserId;
        var rejecterId = friendship.FriendId;
        await _friendships.DeleteAsync(friendship.Id, ct).ConfigureAwait(false);

        await _events
            .DispatchAsync(new FriendRequestRejected(requesterId, rejecterId, _clock.UtcNow), ct)
            .ConfigureAwait(false);

        return ApiResponse.Ok("已拒绝好友申请");
    }
}

/// <summary>删除好友</summary>
public sealed class RemoveFriendCommand : ICommand<ApiResponse>
{
    public int UserId { get; set; }

    public int FriendId { get; set; }
}

internal sealed class RemoveFriendHandler : IRequestHandler<RemoveFriendCommand, ApiResponse>
{
    private readonly IFriendshipRepository _friendships;

    public RemoveFriendHandler(IFriendshipRepository friendships) => _friendships = friendships;

    public async Task<ApiResponse> Handle(RemoveFriendCommand command, CancellationToken ct)
    {
        var removed = await _friendships
            .DeleteAcceptedBetweenAsync(command.UserId, command.FriendId, ct)
            .ConfigureAwait(false);

        DomainException.Ensure(removed > 0, "好友关系不存在");
        return ApiResponse.Ok("已删除好友");
    }
}
