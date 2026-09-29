using System.Globalization;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Admins.Commands;

/// <summary>
/// 强制删除消息（标记 IsDeleted + 广播撤回，历史与搜索即隐藏）。
/// 复用 MessageRecalled 事件，因此在线端的隐藏逻辑与用户自行撤回完全一致。
/// </summary>
public sealed class DeleteMessageByAdminCommand : ICommand<ApiResponse>
{
    public int AdminId { get; set; }

    /// <summary>private | group</summary>
    public string Type { get; set; } = string.Empty;

    public long MessageId { get; set; }
}

internal sealed class DeleteMessageByAdminHandler : IRequestHandler<DeleteMessageByAdminCommand, ApiResponse>
{
    private readonly IPrivateMessageRepository _privateMessages;
    private readonly IGroupMessageRepository _groupMessages;
    private readonly IDomainEventDispatcher _events;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;

    public DeleteMessageByAdminHandler(
        IPrivateMessageRepository privateMessages,
        IGroupMessageRepository groupMessages,
        IDomainEventDispatcher events,
        IAuditLogger audit,
        IClock clock)
    {
        _privateMessages = privateMessages;
        _groupMessages = groupMessages;
        _events = events;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(DeleteMessageByAdminCommand command, CancellationToken ct)
    {
        var type = ChatSessionTypeNames.TryParse(command.Type)
                   ?? throw new DomainException("无效的消息类型");

        var now = _clock.UtcNow;
        var idText = command.MessageId.ToString(CultureInfo.InvariantCulture);

        if (type == ChatSessionType.Private)
        {
            var message = await _privateMessages.FindByIdAsync(command.MessageId, ct).ConfigureAwait(false)
                          ?? throw new EntityNotFoundException("消息不存在");

            message.ForceDelete();
            await _privateMessages.UpdateAsync(message, ct).ConfigureAwait(false);

            await _events.DispatchAsync(new MessageRecalled(
                idText, message.Id, ChatSessionType.Private,
                message.SenderId, message.ReceiverId, null, ByAdmin: true, now), ct).ConfigureAwait(false);

            await _audit.RecordAsync(
                command.AdminId, AuditActions.MessageDelete, AuditActions.TargetMessage, idText,
                $"删除私聊消息 #{idText}（发送者 #{message.SenderId.ToString(CultureInfo.InvariantCulture)}）", ct)
                .ConfigureAwait(false);
        }
        else
        {
            var message = await _groupMessages.FindByIdAsync(command.MessageId, ct).ConfigureAwait(false)
                          ?? throw new EntityNotFoundException("消息不存在");

            message.ForceDelete();
            await _groupMessages.UpdateAsync(message, ct).ConfigureAwait(false);

            await _events.DispatchAsync(new MessageRecalled(
                idText, message.Id, ChatSessionType.Group,
                message.SenderId, null, message.GroupId, ByAdmin: true, now), ct).ConfigureAwait(false);

            await _audit.RecordAsync(
                command.AdminId, AuditActions.MessageDelete, AuditActions.TargetMessage, idText,
                $"删除群消息 #{idText}（群 {message.GroupId.ToString(CultureInfo.InvariantCulture)}）", ct)
                .ConfigureAwait(false);
        }

        return ApiResponse.Ok("消息已删除");
    }
}

/// <summary>
/// 管理后台消息检索（关键词/用户/群过滤；私聊 + 群聊合并，时间倒序）。
/// </summary>
public sealed class SearchMessagesByAdminQuery : IQuery<ApiResponse<PagedResult<AdminMessageDto>>>
{
    public string? Keyword { get; set; }

    public int? UserId { get; set; }

    public long? GroupId { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

internal sealed class SearchMessagesByAdminHandler
    : IRequestHandler<SearchMessagesByAdminQuery, ApiResponse<PagedResult<AdminMessageDto>>>
{
    private readonly IPrivateMessageRepository _privateMessages;
    private readonly IGroupMessageRepository _groupMessages;
    private readonly IUserRepository _users;

    public SearchMessagesByAdminHandler(
        IPrivateMessageRepository privateMessages,
        IGroupMessageRepository groupMessages,
        IUserRepository users)
    {
        _privateMessages = privateMessages;
        _groupMessages = groupMessages;
        _users = users;
    }

    public async Task<ApiResponse<PagedResult<AdminMessageDto>>> Handle(
        SearchMessagesByAdminQuery query, CancellationToken ct)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var take = page.TakeThroughCurrentPage;

        var (groupMessages, groupTotal) = await _groupMessages
            .SearchForAdminAsync(query.Keyword, query.UserId, query.GroupId, take, ct)
            .ConfigureAwait(false);

        // 指定了群时不掺入私聊结果（否则「按群过滤」形同虚设）
        IReadOnlyList<PrivateMessage> privateMessages = Array.Empty<PrivateMessage>();
        var privateTotal = 0;
        if (!query.GroupId.HasValue)
        {
            (privateMessages, privateTotal) = await _privateMessages
                .SearchForAdminAsync(query.Keyword, query.UserId, take, ct)
                .ConfigureAwait(false);
        }

        var userIds = groupMessages.Select(m => m.SenderId)
            .Concat(privateMessages.Select(m => m.SenderId))
            .Concat(privateMessages.Select(m => m.ReceiverId))
            .Distinct()
            .ToList();
        var users = await _users.GetManyAsync(userIds, ct).ConfigureAwait(false);

        var merged = new List<(DateTime SentAt, AdminMessageDto Dto)>(
            groupMessages.Count + privateMessages.Count);

        foreach (var m in groupMessages)
        {
            merged.Add((m.SentAtUtc, new AdminMessageDto
            {
                Id = m.Id,
                MessageId = m.PublicMessageId,
                Type = ChatSessionTypeNames.Group,
                SenderId = m.SenderId,
                SenderName = users.GetValueOrDefault(m.SenderId)?.Nickname ?? $"用户{m.SenderId}",
                SenderAvatar = users.GetValueOrDefault(m.SenderId)?.Avatar,
                Content = m.Content,
                MessageType = (int)m.Kind,
                SessionId = m.GroupId,
                IsDeleted = m.IsDeleted,
                SentAt = m.SentAtUtc
            }));
        }

        foreach (var m in privateMessages)
        {
            // 会话列显示「相对被筛选用户的对端」；未指定用户时退化为发送者视角
            var peerId = query.UserId.HasValue && m.SenderId == query.UserId.Value
                ? m.ReceiverId
                : m.SenderId;

            merged.Add((m.SentAtUtc, new AdminMessageDto
            {
                Id = m.Id,
                MessageId = m.PublicMessageId,
                Type = ChatSessionTypeNames.Private,
                SenderId = m.SenderId,
                SenderName = users.GetValueOrDefault(m.SenderId)?.Nickname ?? $"用户{m.SenderId}",
                SenderAvatar = users.GetValueOrDefault(m.SenderId)?.Avatar,
                Content = m.Content,
                MessageType = (int)m.Kind,
                SessionId = peerId,
                IsDeleted = m.IsDeleted,
                SentAt = m.SentAtUtc
            }));
        }

        var items = merged
            .OrderByDescending(x => x.SentAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(x => x.Dto);

        return ApiResponse<PagedResult<AdminMessageDto>>.Ok(
            PagedResult<AdminMessageDto>.Create(items, groupTotal + privateTotal, page));
    }
}
