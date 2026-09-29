using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using MediatR;

namespace LHZ.OnlineChat.Application.Messaging.Commands;

/// <summary>
/// 撤回消息（WebSocket 入站）。
/// 先按私聊找，找不到再按群聊找 —— 与改造前的行为一致（to 既可能是用户 ID 也可能是群 ID）。
/// </summary>
public sealed class RecallMessageCommand : ICommand<bool>
{
    public int OperatorId { get; set; }

    /// <summary>私聊时为对方账号 ID，群聊时为群 ID</summary>
    public long TargetId { get; set; }

    /// <summary>要撤回的消息标识（客户端 ID 或数据库 ID）</summary>
    public string MessageId { get; set; } = string.Empty;
}

internal sealed class RecallMessageHandler : IRequestHandler<RecallMessageCommand, bool>
{
    private readonly IPrivateMessageRepository _privateMessages;
    private readonly IGroupMessageRepository _groupMessages;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public RecallMessageHandler(
        IPrivateMessageRepository privateMessages,
        IGroupMessageRepository groupMessages,
        IDomainEventDispatcher events,
        IClock clock)
    {
        _privateMessages = privateMessages;
        _groupMessages = groupMessages;
        _events = events;
        _clock = clock;
    }

    public async Task<bool> Handle(RecallMessageCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.MessageId)) return false;

        var now = _clock.UtcNow;
        var earliest = RecallPolicy.EarliestSentAt(now);

        // ===== 私聊 =====
        var privateMessage = await _privateMessages
            .FindRecallableAsync(command.OperatorId, (int)command.TargetId, command.MessageId, earliest, ct)
            .ConfigureAwait(false);

        if (privateMessage is not null)
        {
            privateMessage.Recall(command.OperatorId, now);
            await _privateMessages.UpdateAsync(privateMessage, ct).ConfigureAwait(false);

            await _events.DispatchAsync(new MessageRecalled(
                command.MessageId, privateMessage.Id, ChatSessionType.Private,
                command.OperatorId, (int)command.TargetId, null, ByAdmin: false, now), ct).ConfigureAwait(false);

            return true;
        }

        // ===== 群聊 =====
        var groupMessage = await _groupMessages
            .FindRecallableAsync(command.TargetId, command.OperatorId, command.MessageId, earliest, ct)
            .ConfigureAwait(false);

        if (groupMessage is null) return false;

        groupMessage.Recall(command.OperatorId, now);
        await _groupMessages.UpdateAsync(groupMessage, ct).ConfigureAwait(false);

        await _events.DispatchAsync(new MessageRecalled(
            command.MessageId, groupMessage.Id, ChatSessionType.Group,
            command.OperatorId, null, command.TargetId, ByAdmin: false, now), ct).ConfigureAwait(false);

        return true;
    }
}

/// <summary>
/// 撤回 → 清缓存 + 广播。
/// 私聊推给收发双方，群聊推给全部成员。管理后台强制删除走同一个事件，行为自动一致。
/// </summary>
internal sealed class HandleMessageRecalled : DomainEventHandler<MessageRecalled>
{
    private readonly IRecentMessageCache _cache;
    private readonly IRealtimeNotifier _notifier;
    private readonly IGroupMemberRepository _members;

    public HandleMessageRecalled(
        IRecentMessageCache cache, IRealtimeNotifier notifier, IGroupMemberRepository members)
    {
        _cache = cache;
        _notifier = notifier;
        _members = members;
    }

    protected override async Task HandleAsync(MessageRecalled e, CancellationToken ct)
    {
        IReadOnlyList<int> audience;

        if (e.SessionType == ChatSessionType.Group)
        {
            var groupId = e.GroupId!.Value;
            await _cache
                .RemoveAsync(ChatSessionType.Group, groupId, groupId, e.PublicMessageId, e.DatabaseId, ct)
                .ConfigureAwait(false);
            audience = await _members.ListMemberIdsAsync(groupId, ct).ConfigureAwait(false);
        }
        else
        {
            var peerId = e.PeerUserId ?? 0;
            await _cache
                .RemoveAsync(ChatSessionType.Private, e.SenderId, peerId, e.PublicMessageId, e.DatabaseId, ct)
                .ConfigureAwait(false);
            audience = new[] { e.SenderId, peerId }.Where(id => id > 0).Distinct().ToArray();
        }

        await _notifier.NotifyMessageRecalledAsync(e, audience, ct).ConfigureAwait(false);
    }
}
