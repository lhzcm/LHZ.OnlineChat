using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using MediatR;

namespace LHZ.OnlineChat.Application.Messaging.Commands;

/// <summary>标记单条私聊消息已读</summary>
public sealed class MarkMessageReadCommand : ICommand<ApiResponse>
{
    public long MessageId { get; set; }

    public int UserId { get; set; }
}

internal sealed class MarkMessageReadHandler : IRequestHandler<MarkMessageReadCommand, ApiResponse>
{
    private readonly IPrivateMessageRepository _messages;

    public MarkMessageReadHandler(IPrivateMessageRepository messages) => _messages = messages;

    public async Task<ApiResponse> Handle(MarkMessageReadCommand command, CancellationToken ct)
    {
        var message = await _messages.FindByIdAsync(command.MessageId, ct).ConfigureAwait(false)
                      ?? throw new EntityNotFoundException("消息不存在或无权操作");

        message.MarkAsRead(command.UserId);
        await _messages.UpdateAsync(message, ct).ConfigureAwait(false);

        return ApiResponse.Ok("已标记已读");
    }
}

/// <summary>把某人发给我的全部未读标记已读</summary>
public sealed class MarkConversationReadCommand : ICommand<ApiResponse>
{
    public int SenderId { get; set; }

    public int UserId { get; set; }
}

internal sealed class MarkConversationReadHandler : IRequestHandler<MarkConversationReadCommand, ApiResponse>
{
    private readonly IPrivateMessageRepository _messages;

    public MarkConversationReadHandler(IPrivateMessageRepository messages) => _messages = messages;

    public async Task<ApiResponse> Handle(MarkConversationReadCommand command, CancellationToken ct)
    {
        await _messages.MarkAllReadAsync(command.SenderId, command.UserId, ct).ConfigureAwait(false);
        return ApiResponse.Ok("已全部标记已读");
    }
}

/// <summary>标记群消息已读（把已读游标推进到群内最新消息）</summary>
public sealed class MarkGroupReadCommand : ICommand<ApiResponse>
{
    public long GroupId { get; set; }

    public int UserId { get; set; }
}

internal sealed class MarkGroupReadHandler : IRequestHandler<MarkGroupReadCommand, ApiResponse>
{
    private readonly IGroupMemberRepository _members;
    private readonly IGroupMessageRepository _messages;

    public MarkGroupReadHandler(IGroupMemberRepository members, IGroupMessageRepository messages)
    {
        _members = members;
        _messages = messages;
    }

    public async Task<ApiResponse> Handle(MarkGroupReadCommand command, CancellationToken ct)
    {
        var member = await _members
            .GetRequiredAsync(command.GroupId, command.UserId, ct: ct)
            .ConfigureAwait(false);

        var latestId = await _messages.MaxIdOfGroupAsync(command.GroupId, ct).ConfigureAwait(false);
        member.AdvanceReadCursor(latestId);
        await _members.UpdateAsync(member, ct).ConfigureAwait(false);

        return ApiResponse.Ok("已标记群消息已读");
    }
}

/// <summary>已读回执（WebSocket 入站）：标记已读并转发给被读方</summary>
public sealed class SendReadReceiptCommand : ICommand<Unit>
{
    public int ReaderId { get; set; }

    public int TargetUserId { get; set; }

    /// <summary>单条消息的数据库 ID（兼容旧协议；解析失败则只转发不落库）</summary>
    public string? MessageId { get; set; }
}

internal sealed class SendReadReceiptHandler : IRequestHandler<SendReadReceiptCommand, Unit>
{
    private readonly IPrivateMessageRepository _messages;
    private readonly IRealtimeNotifier _notifier;

    public SendReadReceiptHandler(IPrivateMessageRepository messages, IRealtimeNotifier notifier)
    {
        _messages = messages;
        _notifier = notifier;
    }

    public async Task<Unit> Handle(SendReadReceiptCommand command, CancellationToken ct)
    {
        if (long.TryParse(command.MessageId, System.Globalization.CultureInfo.InvariantCulture, out var id))
        {
            var message = await _messages.FindByIdAsync(id, ct).ConfigureAwait(false);
            if (message is not null && message.ReceiverId == command.ReaderId)
            {
                message.MarkAsRead(command.ReaderId);
                await _messages.UpdateAsync(message, ct).ConfigureAwait(false);
            }
        }

        if (command.TargetUserId > 0)
        {
            await _notifier
                .ForwardReadReceiptAsync(command.ReaderId, command.TargetUserId, command.MessageId, ct)
                .ConfigureAwait(false);
        }

        return Unit.Value;
    }
}

/// <summary>「正在输入」转发（WebSocket 入站）</summary>
public sealed class SendTypingCommand : ICommand<Unit>
{
    public int FromUserId { get; set; }

    public int ToUserId { get; set; }
}

internal sealed class SendTypingHandler : IRequestHandler<SendTypingCommand, Unit>
{
    private readonly IRealtimeNotifier _notifier;

    public SendTypingHandler(IRealtimeNotifier notifier) => _notifier = notifier;

    public async Task<Unit> Handle(SendTypingCommand command, CancellationToken ct)
    {
        if (command.ToUserId > 0)
            await _notifier.ForwardTypingAsync(command.FromUserId, command.ToUserId, ct).ConfigureAwait(false);

        return Unit.Value;
    }
}

/// <summary>更新会话设置（置顶 / 免打扰）</summary>
public sealed class UpdateSessionSettingCommand : ICommand<ApiResponse>
{
    public int UserId { get; set; }

    /// <summary>private | group</summary>
    public string Type { get; set; } = string.Empty;

    public long Id { get; set; }

    public bool? IsPinned { get; set; }

    public bool? Muted { get; set; }
}

internal sealed class UpdateSessionSettingHandler : IRequestHandler<UpdateSessionSettingCommand, ApiResponse>
{
    private readonly ISessionSettingRepository _settings;
    private readonly IClock _clock;

    public UpdateSessionSettingHandler(ISessionSettingRepository settings, IClock clock)
    {
        _settings = settings;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(UpdateSessionSettingCommand command, CancellationToken ct)
    {
        var type = ChatSessionTypeNames.TryParse(command.Type)
                   ?? throw new DomainException("无效的会话类型");
        DomainException.Ensure(command.Id > 0, "无效的会话 ID");
        DomainException.Ensure(command.IsPinned.HasValue || command.Muted.HasValue, "没有需要更新的设置");

        var now = _clock.UtcNow;
        var setting = await _settings.FindAsync(command.UserId, type, command.Id, ct).ConfigureAwait(false);

        if (setting is null)
        {
            setting = SessionSetting.Create(
                command.UserId, type, command.Id,
                command.IsPinned ?? false, command.Muted ?? false, now);
            await _settings.AddAsync(setting, ct).ConfigureAwait(false);
        }
        else
        {
            setting.Update(command.IsPinned, command.Muted, now);
            await _settings.UpdateAsync(setting, ct).ConfigureAwait(false);
        }

        return ApiResponse.Ok("设置已保存");
    }
}
