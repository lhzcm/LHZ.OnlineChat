using System.Globalization;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Messaging.Commands;

/// <summary>发送群聊消息（WebSocket 入站路径）</summary>
public sealed class SendGroupMessageCommand : ICommand<SendMessageResult>
{
    public int SenderId { get; set; }

    public long GroupId { get; set; }

    public string Content { get; set; } = string.Empty;

    public MessageKind Kind { get; set; } = MessageKind.Text;

    public string? ClientMessageId { get; set; }

    public List<int> Mentions { get; set; } = new();

    public string? ReplyToMessageId { get; set; }

    public string? ReplyPreview { get; set; }

    public string? ReplySenderName { get; set; }
}

internal sealed class SendGroupMessageHandler : IRequestHandler<SendGroupMessageCommand, SendMessageResult>
{
    private readonly IGroupMessageRepository _messages;
    private readonly IGroupMemberRepository _members;
    private readonly IUserRepository _users;
    private readonly IRecentMessageCache _cache;
    private readonly IRealtimeNotifier _notifier;
    private readonly IDomainEventDispatcher _events;
    private readonly IMuteMessageFormatter _muteFormatter;
    private readonly IClock _clock;

    public SendGroupMessageHandler(
        IGroupMessageRepository messages,
        IGroupMemberRepository members,
        IUserRepository users,
        IRecentMessageCache cache,
        IRealtimeNotifier notifier,
        IDomainEventDispatcher events,
        IMuteMessageFormatter muteFormatter,
        IClock clock)
    {
        _messages = messages;
        _members = members;
        _users = users;
        _cache = cache;
        _notifier = notifier;
        _events = events;
        _muteFormatter = muteFormatter;
        _clock = clock;
    }

    public async Task<SendMessageResult> Handle(SendGroupMessageCommand command, CancellationToken ct)
    {
        // 非群成员静默丢弃（与改造前一致：WS 路径不回错误，避免探测群成员关系）
        var sender = await _members.FindAsync(command.GroupId, command.SenderId, ct).ConfigureAwait(false);
        if (sender is null) return SendMessageResult.Rejected("你不是该群组成员");

        var now = _clock.UtcNow;

        // 禁言校验：规则和提示语格式分别在聚合根与格式化器里
        if (sender.IsMuted(now))
        {
            var reason = _muteFormatter.Format(sender.MutedUntil!.Value);
            await _notifier
                .NotifyMutedAsync(command.SenderId, command.GroupId, reason, command.ClientMessageId, ct)
                .ConfigureAwait(false);
            return SendMessageResult.Muted(reason, sender.MutedUntil.Value);
        }

        var mentions = MentionList.From(command.Mentions);
        var reply = MessageReply.Create(command.ReplyToMessageId, command.ReplyPreview, command.ReplySenderName);

        var message = GroupMessage.Send(
            command.GroupId, command.SenderId, command.Content, command.Kind,
            command.ClientMessageId, mentions, reply, now);

        await _messages.AddAsync(message, ct).ConfigureAwait(false);

        var senderUser = await _users.FindByIdAsync(command.SenderId, ct).ConfigureAwait(false);
        var realtime = new RealtimeMessage
        {
            SessionType = ChatSessionType.Group,
            SenderId = command.SenderId,
            TargetId = command.GroupId,
            Content = message.Content,
            Kind = message.Kind,
            MessageId = message.PublicMessageId,
            SenderName = senderUser?.Nickname ?? "未知",
            SenderAvatar = senderUser?.Avatar,
            SentAt = message.SentAtUtc,
            Mentions = mentions,
            Reply = reply
        };

        await _cache
            .AppendAsync(ChatSessionType.Group, command.GroupId, command.GroupId, realtime, ct)
            .ConfigureAwait(false);

        var memberIds = await _members.ListMemberIdsAsync(command.GroupId, ct).ConfigureAwait(false);
        await _notifier.PushGroupMessageAsync(realtime, memberIds, ct).ConfigureAwait(false);

        await _events
            .DispatchAsync(new GroupMessageSent(message.Id, command.GroupId, command.SenderId, now), ct)
            .ConfigureAwait(false);

        return SendMessageResult.Ok();
    }
}

/// <summary>
/// 禁言提示语格式化。
/// 单独成一个端口，因为它要把 UTC 截止时间按「中国标准时间」渲染给用户看
/// （TimeZoneInfo 查询依赖宿主的时区数据库，属基础设施关注点）。
/// </summary>
public interface IMuteMessageFormatter
{
    string Format(DateTime mutedUntilUtc);
}

/// <summary>管理后台审计里用的禁言时间描述（UTC 明示，给管理员看）</summary>
public static class MuteAuditFormatter
{
    public static string Describe(DateTime? mutedUntilUtc, string targetName)
        => mutedUntilUtc.HasValue
            ? $"禁言 {targetName} 至 {mutedUntilUtc.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}（UTC）"
            : $"解除 {targetName} 的禁言";
}
