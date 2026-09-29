using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Friends;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Messaging.Commands;

/// <summary>
/// 广播上线/下线状态给在线好友（WS 连接建立/断开时触发）。
/// </summary>
public sealed class BroadcastPresenceCommand : ICommand<Unit>
{
    public int UserId { get; set; }

    public bool Online { get; set; }
}

internal sealed class BroadcastPresenceHandler : IRequestHandler<BroadcastPresenceCommand, Unit>
{
    private readonly IFriendshipRepository _friendships;
    private readonly IRealtimeNotifier _notifier;

    public BroadcastPresenceHandler(IFriendshipRepository friendships, IRealtimeNotifier notifier)
    {
        _friendships = friendships;
        _notifier = notifier;
    }

    public async Task<Unit> Handle(BroadcastPresenceCommand command, CancellationToken ct)
    {
        var friendIds = await _friendships.ListFriendIdsOfAsync(command.UserId, ct).ConfigureAwait(false);
        if (friendIds.Count > 0)
        {
            await _notifier
                .NotifyPresenceAsync(command.UserId, friendIds, command.Online, ct)
                .ConfigureAwait(false);
        }

        return Unit.Value;
    }
}

/// <summary>
/// 上线后补发各群已读游标之后的消息（每群上限 100 条，防游标异常刷屏）。
/// </summary>
public sealed class SendGroupBacklogCommand : ICommand<Unit>
{
    /// <summary>每群补发上限</summary>
    public const int PerGroupLimit = 100;

    public int UserId { get; set; }
}

internal sealed class SendGroupBacklogHandler : IRequestHandler<SendGroupBacklogCommand, Unit>
{
    private readonly IGroupMemberRepository _members;
    private readonly IGroupMessageRepository _messages;
    private readonly IUserRepository _users;
    private readonly IRealtimeNotifier _notifier;

    public SendGroupBacklogHandler(
        IGroupMemberRepository members,
        IGroupMessageRepository messages,
        IUserRepository users,
        IRealtimeNotifier notifier)
    {
        _members = members;
        _messages = messages;
        _users = users;
        _notifier = notifier;
    }

    public async Task<Unit> Handle(SendGroupBacklogCommand command, CancellationToken ct)
    {
        var memberships = await _members.ListOfUserAsync(command.UserId, ct).ConfigureAwait(false);
        if (memberships.Count == 0) return Unit.Value;

        foreach (var membership in memberships)
        {
            var backlog = await _messages
                .ListAfterCursorAsync(
                    membership.GroupId, membership.LastReadMessageId,
                    SendGroupBacklogCommand.PerGroupLimit, ct)
                .ConfigureAwait(false);

            if (backlog.Count == 0) continue;

            var senders = await _users
                .GetManyAsync(backlog.Select(m => m.SenderId).Distinct(), ct)
                .ConfigureAwait(false);

            var messages = backlog.Select(m =>
            {
                var sender = senders.GetValueOrDefault(m.SenderId);
                return new RealtimeMessage
                {
                    SessionType = ChatSessionType.Group,
                    SenderId = m.SenderId,
                    TargetId = membership.GroupId,
                    Content = m.Content,
                    Kind = m.Kind,
                    MessageId = m.PublicMessageId,
                    SenderName = sender?.Nickname ?? "未知",
                    SenderAvatar = sender?.Avatar,
                    SentAt = m.SentAtUtc,
                    Mentions = m.MentionedUsers,
                    Reply = m.Reply
                };
            }).ToList();

            await _notifier
                .PushGroupBacklogAsync(command.UserId, messages, ct)
                .ConfigureAwait(false);
        }

        return Unit.Value;
    }
}
