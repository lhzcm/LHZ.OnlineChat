using System.Security.Cryptography;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Groups;
using MediatR;

namespace LHZ.OnlineChat.Application.Groups.Commands;

/// <summary>生成（或重置）群邀请链接</summary>
public sealed class CreateGroupInviteLinkCommand : ICommand<ApiResponse<GroupInviteLinkDto>>
{
    public long GroupId { get; set; }

    public int OperatorId { get; set; }
}

internal sealed class CreateGroupInviteLinkHandler
    : IRequestHandler<CreateGroupInviteLinkCommand, ApiResponse<GroupInviteLinkDto>>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;
    private readonly IClock _clock;

    public CreateGroupInviteLinkHandler(
        IGroupRepository groups, IGroupMemberRepository members, IClock clock)
    {
        _groups = groups;
        _members = members;
        _clock = clock;
    }

    public async Task<ApiResponse<GroupInviteLinkDto>> Handle(
        CreateGroupInviteLinkCommand command, CancellationToken ct)
    {
        var op = await _members
            .GetRequiredAsync(command.GroupId, command.OperatorId, ct: ct)
            .ConfigureAwait(false);
        op.EnsureCanManageGroup("生成邀请链接");

        var group = await _groups.GetRequiredAsync(command.GroupId, ct).ConfigureAwait(false);

        // 32 字节 CSPRNG → base64url：43 字符、约 192 bit 熵，枚举不可行。
        // token 是「持有即可申请入群」的凭证，绝不能写日志或响应以外的地方。
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        group.EnableInviteLink(token, _clock.UtcNow);
        await _groups.UpdateAsync(group, ct).ConfigureAwait(false);

        return ApiResponse<GroupInviteLinkDto>.Ok(new GroupInviteLinkDto
        {
            Token = group.InviteToken,
            ExpiresAt = UtcTime.Normalize(group.InviteTokenExpiresAt),
            RequiresApproval = group.JoinApprovalRequired
        }, "邀请链接已生成");
    }
}

/// <summary>关闭群邀请链接</summary>
public sealed class DisableGroupInviteLinkCommand : ICommand<ApiResponse>
{
    public long GroupId { get; set; }

    public int OperatorId { get; set; }
}

internal sealed class DisableGroupInviteLinkHandler : IRequestHandler<DisableGroupInviteLinkCommand, ApiResponse>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;

    public DisableGroupInviteLinkHandler(IGroupRepository groups, IGroupMemberRepository members)
    {
        _groups = groups;
        _members = members;
    }

    public async Task<ApiResponse> Handle(DisableGroupInviteLinkCommand command, CancellationToken ct)
    {
        var op = await _members
            .GetRequiredAsync(command.GroupId, command.OperatorId, ct: ct)
            .ConfigureAwait(false);
        op.EnsureCanManageGroup("关闭邀请链接");

        var group = await _groups.GetRequiredAsync(command.GroupId, ct).ConfigureAwait(false);
        group.DisableInviteLink();
        await _groups.UpdateAsync(group, ct).ConfigureAwait(false);

        return ApiResponse.Ok("邀请链接已关闭");
    }
}

/// <summary>设置凭邀请链接入群是否需要管理员审批</summary>
public sealed class SetGroupJoinApprovalCommand : ICommand<ApiResponse>
{
    public long GroupId { get; set; }

    public int OperatorId { get; set; }

    /// <summary>true=需要审批（默认），false=凭链接直接入群</summary>
    public bool Required { get; set; }
}

internal sealed class SetGroupJoinApprovalHandler : IRequestHandler<SetGroupJoinApprovalCommand, ApiResponse>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;

    public SetGroupJoinApprovalHandler(IGroupRepository groups, IGroupMemberRepository members)
    {
        _groups = groups;
        _members = members;
    }

    public async Task<ApiResponse> Handle(SetGroupJoinApprovalCommand command, CancellationToken ct)
    {
        var op = await _members
            .GetRequiredAsync(command.GroupId, command.OperatorId, ct: ct)
            .ConfigureAwait(false);
        op.EnsureCanManageGroup("修改入群审批");

        var group = await _groups.GetRequiredAsync(command.GroupId, ct).ConfigureAwait(false);
        group.SetJoinApprovalRequired(command.Required);
        await _groups.UpdateAsync(group, ct).ConfigureAwait(false);

        return ApiResponse.Ok(command.Required ? "凭邀请链接入群需要审批" : "凭邀请链接可直接入群");
    }
}

/// <summary>查看群邀请链接状态（仅群主/管理员）</summary>
public sealed class GetGroupInviteLinkQuery : IQuery<ApiResponse<GroupInviteLinkDto>>
{
    public long GroupId { get; set; }

    public int RequesterId { get; set; }
}

internal sealed class GetGroupInviteLinkHandler
    : IRequestHandler<GetGroupInviteLinkQuery, ApiResponse<GroupInviteLinkDto>>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupMemberRepository _members;
    private readonly IClock _clock;

    public GetGroupInviteLinkHandler(
        IGroupRepository groups, IGroupMemberRepository members, IClock clock)
    {
        _groups = groups;
        _members = members;
        _clock = clock;
    }

    public async Task<ApiResponse<GroupInviteLinkDto>> Handle(
        GetGroupInviteLinkQuery query, CancellationToken ct)
    {
        var op = await _members
            .GetRequiredAsync(query.GroupId, query.RequesterId, ct: ct)
            .ConfigureAwait(false);
        op.EnsureCanManageGroup("查看邀请链接");

        var group = await _groups.GetRequiredAsync(query.GroupId, ct).ConfigureAwait(false);
        var active = group.HasActiveInviteLink(_clock.UtcNow);

        return ApiResponse<GroupInviteLinkDto>.Ok(new GroupInviteLinkDto
        {
            // 过期后仍回 token（前端据此提示"已过期，请重新生成"），但不算 active
            Token = active ? group.InviteToken : null,
            ExpiresAt = UtcTime.Normalize(group.InviteTokenExpiresAt),
            RequiresApproval = group.JoinApprovalRequired
        });
    }
}
