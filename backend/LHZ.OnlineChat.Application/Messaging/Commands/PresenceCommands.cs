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
    private readonly IGroupMessageRepository _messages;
    private readonly IUserRepository _users;
    private readonly IRealtimeNotifier _notifier;

    public SendGroupBacklogHandler(
        IGroupMessageRepository messages,
        IUserRepository users,
        IRealtimeNotifier notifier)
    {
        _messages = messages;
        _users = users;
        _notifier = notifier;
    }

    /// <summary>
    /// 上线补发：固定 1～3 次查询。
    ///
    /// 原实现是「遍历我加入的每个群，各查一次游标之后的消息」——
    /// 20 个群就是 20 次数据库往返，而这条命令**每次 WebSocket 连接都会跑**
    /// （连上、断线重连都算），绝大多数时候一条补发都没有。
    /// 现在改成：一次窗口查询拿到所有群的待补发 ID（没有就到此为止），
    /// 再批量取消息正文与发件人，最后按群分组推送。
    ///
    /// 游标过滤（Id 大于 GroupMember.LastReadMessageId、排除已撤回）
    /// 与每群 100 条上限都保持不变，见 GroupMessageRepository.ListBacklogIdsAsync。
    /// </summary>
    public async Task<Unit> Handle(SendGroupBacklogCommand command, CancellationToken ct)
    {
        var ids = await _messages
            .ListBacklogIdsAsync(command.UserId, SendGroupBacklogCommand.PerGroupLimit, ct)
            .ConfigureAwait(false);

        // 常见路径：没有任何群需要补发，一次查询就结束
        if (ids.Count == 0) return Unit.Value;

        var backlog = await _messages.ListByIdsAsync(ids, ct).ConfigureAwait(false);
        if (backlog.Count == 0) return Unit.Value;

        var senders = await _users
            .GetManyAsync(backlog.Select(m => m.SenderId).Distinct(), ct)
            .ConfigureAwait(false);

        // 一次取回全部群的补发，按群分组推送（保持「每个群一个批次」的原有行为）
        foreach (var group in backlog.GroupBy(m => m.GroupId))
        {
            var messages = group
                .OrderBy(m => m.SentAt)
                .ThenBy(m => m.Id)
                .Select(m =>
                {
                    var sender = senders.GetValueOrDefault(m.SenderId);
                    return new RealtimeMessage
                    {
                        SessionType = ChatSessionType.Group,
                        SenderId = m.SenderId,
                        TargetId = m.GroupId,
                        Content = m.Content,
                        Kind = m.Kind,
                        MessageId = m.PublicMessageId,
                        SenderName = sender?.Nickname ?? "未知",
                        SenderAvatar = sender?.Avatar,
                        SentAt = m.SentAtUtc,
                        Mentions = m.MentionedUsers,
                        Reply = m.Reply
                    };
                })
                .ToList();

            await _notifier
                .PushGroupBacklogAsync(command.UserId, messages, ct)
                .ConfigureAwait(false);
        }

        return Unit.Value;
    }
}
