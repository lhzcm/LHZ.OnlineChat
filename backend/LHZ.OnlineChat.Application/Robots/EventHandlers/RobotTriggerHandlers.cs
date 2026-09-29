using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Application.Robots.Commands;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Robots;
using LHZ.OnlineChat.Domain.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LHZ.OnlineChat.Application.Robots.EventHandlers;

/// <summary>
/// 私聊消息 → 若收件人是启用中的机器人，调度 Webhook。
///
/// 改造前是 WsMessageHandler 在处理完消息后直接 `await _botService.TryDispatchPrivateAsync(...)`，
/// 消息链路因此硬依赖机器人功能；现在机器人只是 PrivateMessageSent 的一个订阅方，
/// 机器人模块整个删掉也不影响消息收发。
/// </summary>
internal sealed class TriggerRobotOnPrivateMessage : DomainEventHandler<PrivateMessageSent>
{
    private readonly IRobotRepository _robots;
    private readonly IUserRepository _users;
    private readonly IPrivateMessageRepository _messages;
    private readonly IRobotConversationService _conversation;
    private readonly ILogger<TriggerRobotOnPrivateMessage> _logger;

    public TriggerRobotOnPrivateMessage(
        IRobotRepository robots,
        IUserRepository users,
        IPrivateMessageRepository messages,
        IRobotConversationService conversation,
        ILogger<TriggerRobotOnPrivateMessage> logger)
    {
        _robots = robots;
        _users = users;
        _messages = messages;
        _conversation = conversation;
        _logger = logger;
    }

    protected override async Task HandleAsync(PrivateMessageSent e, CancellationToken ct)
    {
        var robot = await _robots.FindByBotUserIdAsync(e.ReceiverId, ct).ConfigureAwait(false);
        if (robot is null || !robot.RespondsToMessages) return;

        // 机器人之间互不触发，防死循环
        var sender = await _users.FindByIdAsync(e.SenderId, ct).ConfigureAwait(false);
        if (sender is null || sender.IsBot) return;

        var message = await _messages.FindByIdAsync(e.MessageId, ct).ConfigureAwait(false);
        if (message is null) return;

        var payload = new WebhookEvent
        {
            Robot = new WebhookActor
            {
                UserId = robot.UserId, Name = robot.Name, Avatar = robot.Avatar, IsBot = true
            },
            Session = new WebhookSession
            {
                Type = ChatSessionType.Private, Id = e.SenderId, Name = sender.Nickname
            },
            From = new WebhookActor
            {
                UserId = e.SenderId, Name = sender.Nickname, Avatar = sender.Avatar
            },
            Message = new WebhookMessage
            {
                MessageId = message.PublicMessageId,
                Content = message.Content,
                Kind = message.Kind,
                Timestamp = Domain.Common.UtcTime.ToUnixMilliseconds(message.SentAt)
            }
        };

        _logger.LogInformation("私聊触发机器人：{SenderId} → {RobotUserId}", e.SenderId, robot.UserId);

        // 不阻塞消息链路：Webhook 可能要等 10 秒超时
        _conversation.DispatchInBackground(robot.Id, payload, new RobotReplyTarget
        {
            SessionType = ChatSessionType.Private,
            SessionId = e.SenderId,
            QuotedMessageId = message.PublicMessageId,
            QuotedContent = message.Content,
            QuotedSenderName = sender.Nickname
        });
    }
}

/// <summary>群消息 → 若 @ 了群内启用中的机器人，逐个调度 Webhook</summary>
internal sealed class TriggerRobotOnGroupMessage : DomainEventHandler<GroupMessageSent>
{
    private readonly IRobotRepository _robots;
    private readonly IUserRepository _users;
    private readonly IGroupRepository _groups;
    private readonly IGroupMessageRepository _messages;
    private readonly IRobotConversationService _conversation;
    private readonly ILogger<TriggerRobotOnGroupMessage> _logger;

    public TriggerRobotOnGroupMessage(
        IRobotRepository robots,
        IUserRepository users,
        IGroupRepository groups,
        IGroupMessageRepository messages,
        IRobotConversationService conversation,
        ILogger<TriggerRobotOnGroupMessage> logger)
    {
        _robots = robots;
        _users = users;
        _groups = groups;
        _messages = messages;
        _conversation = conversation;
        _logger = logger;
    }

    protected override async Task HandleAsync(GroupMessageSent e, CancellationToken ct)
    {
        var message = await _messages.FindByIdAsync(e.MessageId, ct).ConfigureAwait(false);
        if (message is null) return;

        var mentions = message.MentionedUsers;
        if (mentions.IsEmpty) return;

        var robots = await _robots.ListByBotUserIdsAsync(mentions.UserIds, ct).ConfigureAwait(false);
        var triggerable = robots.Where(r => r.RespondsToMessages).ToList();
        if (triggerable.Count == 0) return;

        var sender = await _users.FindByIdAsync(e.SenderId, ct).ConfigureAwait(false);
        if (sender is null || sender.IsBot) return;

        var group = await _groups.FindByIdAsync(e.GroupId, ct).ConfigureAwait(false);
        if (group is null) return;

        foreach (var robot in triggerable)
        {
            var payload = new WebhookEvent
            {
                Robot = new WebhookActor
                {
                    UserId = robot.UserId, Name = robot.Name, Avatar = robot.Avatar, IsBot = true
                },
                Session = new WebhookSession
                {
                    Type = ChatSessionType.Group, Id = e.GroupId, Name = group.Name
                },
                From = new WebhookActor
                {
                    UserId = e.SenderId, Name = sender.Nickname, Avatar = sender.Avatar
                },
                Message = new WebhookMessage
                {
                    MessageId = message.PublicMessageId,
                    Content = message.Content,
                    Kind = message.Kind,
                    Timestamp = Domain.Common.UtcTime.ToUnixMilliseconds(message.SentAt)
                },
                Mentions = mentions.UserIds
            };

            _logger.LogInformation(
                "群 @ 触发机器人：{SenderId} → {RobotUserId} @群 {GroupId}", e.SenderId, robot.UserId, e.GroupId);

            _conversation.DispatchInBackground(robot.Id, payload, new RobotReplyTarget
            {
                SessionType = ChatSessionType.Group,
                SessionId = e.GroupId,
                QuotedMessageId = message.PublicMessageId,
                QuotedContent = message.Content,
                QuotedSenderName = sender.Nickname
            });
        }
    }
}

/// <summary>
/// 机器人会话服务：后台投递 Webhook 并把同步回复转成一条真实消息。
/// 「后台执行」需要独立的 DI 作用域（事件处理器所在的请求作用域会先结束），
/// 因此实现放在基础设施层。
/// </summary>
public interface IRobotConversationService
{
    /// <summary>投递 Webhook 并在拿到同步回复后以机器人身份回复（不阻塞调用方）</summary>
    void DispatchInBackground(long robotId, WebhookEvent payload, RobotReplyTarget replyTarget);
}

/// <summary>机器人回复的目标会话与引用信息</summary>
public sealed class RobotReplyTarget
{
    public required ChatSessionType SessionType { get; init; }

    /// <summary>私聊为对方账号 ID，群聊为群 ID</summary>
    public required long SessionId { get; init; }

    public string? QuotedMessageId { get; init; }

    public string? QuotedContent { get; init; }

    public string? QuotedSenderName { get; init; }
}

/// <summary>
/// 以机器人身份发出一条消息（复用消息通道：落库 + 缓存 + 多端广播 + 推送埋点）。
/// 同步回复、异步回复、第三方主动推送三条入口共用它。
/// </summary>
public sealed class SendRobotMessageCommand : ICommand<Unit>
{
    public long RobotId { get; set; }

    public required ChatSessionType SessionType { get; set; }

    public long SessionId { get; set; }

    public string Content { get; set; } = string.Empty;

    public string? QuotedMessageId { get; set; }

    public string? QuotedContent { get; set; }

    public string? QuotedSenderName { get; set; }
}

internal sealed class SendRobotMessageHandler : IRequestHandler<SendRobotMessageCommand, Unit>
{
    private readonly IRobotRepository _robots;
    private readonly IUserRepository _users;
    private readonly IPrivateMessageRepository _privateMessages;
    private readonly IGroupMessageRepository _groupMessages;
    private readonly IGroupMemberRepository _members;
    private readonly IRecentMessageCache _cache;
    private readonly IRealtimeNotifier _notifier;
    private readonly Domain.Common.IClock _clock;

    public SendRobotMessageHandler(
        IRobotRepository robots,
        IUserRepository users,
        IPrivateMessageRepository privateMessages,
        IGroupMessageRepository groupMessages,
        IGroupMemberRepository members,
        IRecentMessageCache cache,
        IRealtimeNotifier notifier,
        Domain.Common.IClock clock)
    {
        _robots = robots;
        _users = users;
        _privateMessages = privateMessages;
        _groupMessages = groupMessages;
        _members = members;
        _cache = cache;
        _notifier = notifier;
        _clock = clock;
    }

    public async Task<Unit> Handle(SendRobotMessageCommand command, CancellationToken ct)
    {
        var robot = await _robots.GetRequiredAsync(command.RobotId, ct).ConfigureAwait(false);
        var botUser = await _users.FindByIdAsync(robot.UserId, ct).ConfigureAwait(false);
        var now = _clock.UtcNow;

        var reply = MessageReply.Create(
            command.QuotedMessageId, command.QuotedContent, command.QuotedSenderName);

        var realtime = command.SessionType == ChatSessionType.Private
            ? await SendPrivateAsync(robot, botUser, command, reply, now, ct).ConfigureAwait(false)
            : await SendGroupAsync(robot, botUser, command, reply, now, ct).ConfigureAwait(false);

        await _cache
            .AppendAsync(command.SessionType, realtime.SenderId, realtime.TargetId, realtime, ct)
            .ConfigureAwait(false);

        if (command.SessionType == ChatSessionType.Private)
        {
            await _notifier.PushPrivateMessageAsync(realtime, ct).ConfigureAwait(false);
        }
        else
        {
            var memberIds = await _members.ListMemberIdsAsync(command.SessionId, ct).ConfigureAwait(false);
            await _notifier.PushGroupMessageAsync(realtime, memberIds, ct).ConfigureAwait(false);
        }

        // 推送埋点（管理后台统计）
        robot.RecordPush();
        await _robots.UpdateAsync(robot, ct).ConfigureAwait(false);

        return Unit.Value;
    }

    private async Task<RealtimeMessage> SendPrivateAsync(
        Robot robot, User? botUser, SendRobotMessageCommand command,
        MessageReply? reply, DateTime now, CancellationToken ct)
    {
        var message = PrivateMessage.Send(
            robot.UserId, (int)command.SessionId, command.Content,
            MessageKind.Text, null, reply, now);

        await _privateMessages.AddAsync(message, ct).ConfigureAwait(false);

        return new RealtimeMessage
        {
            SessionType = ChatSessionType.Private,
            SenderId = robot.UserId,
            TargetId = command.SessionId,
            Content = message.Content,
            Kind = message.Kind,
            MessageId = message.PublicMessageId,
            SenderName = botUser?.Nickname ?? robot.Name,
            SenderAvatar = botUser?.Avatar ?? robot.Avatar,
            SentAt = message.SentAtUtc,
            Reply = reply
        };
    }

    private async Task<RealtimeMessage> SendGroupAsync(
        Robot robot, User? botUser, SendRobotMessageCommand command,
        MessageReply? reply, DateTime now, CancellationToken ct)
    {
        var message = GroupMessage.Send(
            command.SessionId, robot.UserId, command.Content,
            MessageKind.Text, null, MentionList.Empty, reply, now);

        await _groupMessages.AddAsync(message, ct).ConfigureAwait(false);

        return new RealtimeMessage
        {
            SessionType = ChatSessionType.Group,
            SenderId = robot.UserId,
            TargetId = command.SessionId,
            Content = message.Content,
            Kind = message.Kind,
            MessageId = message.PublicMessageId,
            SenderName = botUser?.Nickname ?? robot.Name,
            SenderAvatar = botUser?.Avatar ?? robot.Avatar,
            SentAt = message.SentAtUtc,
            Reply = reply
        };
    }
}
