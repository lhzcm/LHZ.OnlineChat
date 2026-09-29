using System.Globalization;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Application.Messaging.Commands;
using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Admins.Commands;

/// <summary>
/// 强制解散群（删群 + 成员 + 消息）。
/// 通知在线成员与清理会话设置由 GroupDissolved 事件的订阅方完成，
/// 与用户自行解散走完全相同的后续流程。
/// </summary>
public sealed class DissolveGroupByAdminCommand : ICommand<ApiResponse>
{
    public int AdminId { get; set; }

    public long GroupId { get; set; }
}

internal sealed class DissolveGroupByAdminHandler : IRequestHandler<DissolveGroupByAdminCommand, ApiResponse>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;
    private readonly IGroupMessageRepository _messages;
    private readonly IDomainEventDispatcher _events;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;

    public DissolveGroupByAdminHandler(
        IGroupRepository groups,
        IGroupMemberRepository members,
        IGroupMessageRepository messages,
        IDomainEventDispatcher events,
        IAuditLogger audit,
        IClock clock)
    {
        _groups = groups;
        _members = members;
        _messages = messages;
        _events = events;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(DissolveGroupByAdminCommand command, CancellationToken ct)
    {
        var group = await _groups.FindByIdAsync(command.GroupId, ct).ConfigureAwait(false)
                    ?? throw new EntityNotFoundException("群不存在");

        var memberIds = await _members.ListMemberIdsAsync(command.GroupId, ct).ConfigureAwait(false);

        await _messages.DeleteAllOfGroupAsync(command.GroupId, ct).ConfigureAwait(false);
        await _members.DeleteAllOfGroupAsync(command.GroupId, ct).ConfigureAwait(false);
        await _groups.DeleteAsync(command.GroupId, ct).ConfigureAwait(false);

        group.Dissolve(memberIds, _clock.UtcNow);
        await _events.DispatchEventsOfAsync(group, ct).ConfigureAwait(false);

        await _audit.RecordAsync(
            command.AdminId, AuditActions.GroupDissolve, AuditActions.TargetGroup,
            command.GroupId.ToString(CultureInfo.InvariantCulture),
            $"解散群「{group.Name}」（{memberIds.Count} 人）", ct).ConfigureAwait(false);

        return ApiResponse.Ok($"群已解散（{memberIds.Count} 名成员）");
    }
}

/// <summary>强制移除群成员</summary>
public sealed class RemoveGroupMemberByAdminCommand : ICommand<ApiResponse>
{
    public int AdminId { get; set; }

    public long GroupId { get; set; }

    public int UserId { get; set; }
}

internal sealed class RemoveGroupMemberByAdminHandler
    : IRequestHandler<RemoveGroupMemberByAdminCommand, ApiResponse>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;
    private readonly IUserRepository _users;
    private readonly IAuditLogger _audit;

    public RemoveGroupMemberByAdminHandler(
        IGroupRepository groups,
        IGroupMemberRepository members,
        IUserRepository users,
        IAuditLogger audit)
    {
        _groups = groups;
        _members = members;
        _users = users;
        _audit = audit;
    }

    public async Task<ApiResponse> Handle(RemoveGroupMemberByAdminCommand command, CancellationToken ct)
    {
        var group = await _groups.FindByIdAsync(command.GroupId, ct).ConfigureAwait(false)
                    ?? throw new EntityNotFoundException("群不存在");

        DomainException.Ensure(!group.IsOwnedBy(command.UserId), "不能移除群主，请先转让群主");

        var removed = await _members.RemoveAsync(command.GroupId, command.UserId, ct).ConfigureAwait(false);
        DomainException.Ensure(removed > 0, "该用户不在群中");

        var user = await _users.FindByIdAsync(command.UserId, ct).ConfigureAwait(false);
        await _audit.RecordAsync(
            command.AdminId, AuditActions.GroupRemoveMember, AuditActions.TargetGroup,
            command.GroupId.ToString(CultureInfo.InvariantCulture),
            $"从群「{group.Name}」移除成员 {DescribeUser(user, command.UserId)}", ct).ConfigureAwait(false);

        return ApiResponse.Ok("已移除该成员");
    }

    internal static string DescribeUser(User? user, int userId)
        => user?.Nickname ?? $"用户{userId.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>禁言 / 解除禁言群成员</summary>
public sealed class MuteGroupMemberCommand : ICommand<ApiResponse>
{
    public int AdminId { get; set; }

    public long GroupId { get; set; }

    public int UserId { get; set; }

    /// <summary>禁言截止时间（UTC）；null 或已过去 = 解除禁言</summary>
    public DateTime? MutedUntil { get; set; }
}

internal sealed class MuteGroupMemberHandler : IRequestHandler<MuteGroupMemberCommand, ApiResponse>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;
    private readonly IUserRepository _users;
    private readonly IDomainEventDispatcher _events;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;

    public MuteGroupMemberHandler(
        IGroupRepository groups,
        IGroupMemberRepository members,
        IUserRepository users,
        IDomainEventDispatcher events,
        IAuditLogger audit,
        IClock clock)
    {
        _groups = groups;
        _members = members;
        _users = users;
        _events = events;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(MuteGroupMemberCommand command, CancellationToken ct)
    {
        var group = await _groups.FindByIdAsync(command.GroupId, ct).ConfigureAwait(false)
                    ?? throw new EntityNotFoundException("群不存在");

        var member = await _members.FindAsync(command.GroupId, command.UserId, ct).ConfigureAwait(false)
                     ?? throw new EntityNotFoundException("该用户不在群中");

        var now = _clock.UtcNow;
        // 「不能禁言群主」在聚合根里（群主判定同时看 Group.OwnerId 与成员角色）
        DomainException.Ensure(!group.IsOwnedBy(command.UserId), "不能禁言群主");
        var muted = member.SetMute(command.MutedUntil, now);

        await _members.UpdateAsync(member, ct).ConfigureAwait(false);
        await _events
            .DispatchAsync(new GroupMemberMuteChanged(
                command.GroupId, command.UserId, member.MutedUntil, now), ct)
            .ConfigureAwait(false);

        var user = await _users.FindByIdAsync(command.UserId, ct).ConfigureAwait(false);
        var detail = MuteAuditFormatter.Describe(
            member.MutedUntil, RemoveGroupMemberByAdminHandler.DescribeUser(user, command.UserId));

        await _audit.RecordAsync(
            command.AdminId, AuditActions.GroupMute, AuditActions.TargetGroup,
            command.GroupId.ToString(CultureInfo.InvariantCulture),
            $"群「{group.Name}」{detail}", ct).ConfigureAwait(false);

        return ApiResponse.Ok(muted ? "已禁言" : "已解除禁言");
    }
}

/// <summary>转让群主（原群主降为普通成员）</summary>
public sealed class TransferGroupOwnerCommand : ICommand<ApiResponse>
{
    public int AdminId { get; set; }

    public long GroupId { get; set; }

    public int NewOwnerId { get; set; }
}

internal sealed class TransferGroupOwnerHandler : IRequestHandler<TransferGroupOwnerCommand, ApiResponse>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;
    private readonly IDomainEventDispatcher _events;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;

    public TransferGroupOwnerHandler(
        IGroupRepository groups,
        IGroupMemberRepository members,
        IDomainEventDispatcher events,
        IAuditLogger audit,
        IClock clock)
    {
        _groups = groups;
        _members = members;
        _events = events;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(TransferGroupOwnerCommand command, CancellationToken ct)
    {
        var group = await _groups.FindByIdAsync(command.GroupId, ct).ConfigureAwait(false)
                    ?? throw new EntityNotFoundException("群不存在");

        var newOwner = await _members
            .FindAsync(command.GroupId, command.NewOwnerId, ct)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException("新群主必须是群成员");

        var previousOwnerId = group.OwnerId;
        group.TransferOwnership(command.NewOwnerId, _clock.UtcNow);
        await _groups.UpdateAsync(group, ct).ConfigureAwait(false);

        newOwner.PromoteToOwner();
        await _members.UpdateAsync(newOwner, ct).ConfigureAwait(false);

        var previousOwner = await _members
            .FindAsync(command.GroupId, previousOwnerId, ct)
            .ConfigureAwait(false);
        if (previousOwner is not null)
        {
            previousOwner.DemoteFromOwner();
            await _members.UpdateAsync(previousOwner, ct).ConfigureAwait(false);
        }

        await _events.DispatchEventsOfAsync(group, ct).ConfigureAwait(false);

        await _audit.RecordAsync(
            command.AdminId, AuditActions.GroupTransfer, AuditActions.TargetGroup,
            command.GroupId.ToString(CultureInfo.InvariantCulture),
            $"群「{group.Name}」群主转让为 #{command.NewOwnerId.ToString(CultureInfo.InvariantCulture)}", ct)
            .ConfigureAwait(false);

        return ApiResponse.Ok("群主已转让");
    }
}
