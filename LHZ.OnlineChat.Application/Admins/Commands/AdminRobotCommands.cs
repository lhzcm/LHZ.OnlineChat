using System.Globalization;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Application.Robots.Commands;
using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Robots;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Admins.Commands;

/// <summary>管理后台机器人列表</summary>
public sealed class ListRobotsQuery : IQuery<ApiResponse<PagedResult<AdminRobotDto>>>
{
    public string? Keyword { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

internal sealed class ListRobotsHandler : IRequestHandler<ListRobotsQuery, ApiResponse<PagedResult<AdminRobotDto>>>
{
    private readonly IRobotRepository _robots;
    private readonly IUserRepository _users;

    public ListRobotsHandler(IRobotRepository robots, IUserRepository users)
    {
        _robots = robots;
        _users = users;
    }

    public async Task<ApiResponse<PagedResult<AdminRobotDto>>> Handle(
        ListRobotsQuery query, CancellationToken ct)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var (robots, total) = await _robots.SearchAsync(query.Keyword, page, ct).ConfigureAwait(false);

        if (robots.Count == 0)
            return ApiResponse<PagedResult<AdminRobotDto>>.Ok(PagedResult<AdminRobotDto>.Empty(page));

        var owners = await _users
            .GetManyAsync(robots.Select(r => r.OwnerId).Distinct(), ct)
            .ConfigureAwait(false);

        var items = robots.Select(r => new AdminRobotDto
        {
            Id = r.Id,
            UserId = r.UserId,
            Name = r.Name,
            OwnerId = r.OwnerId,
            OwnerName = owners.GetValueOrDefault(r.OwnerId)?.Nickname ?? $"用户{r.OwnerId}",
            WebhookUrl = r.WebhookUrlValue,
            Enabled = r.Enabled,
            PushCount = r.PushCount,
            CallbackFailCount = r.CallbackFailCount,
            CreatedAt = UtcTime.Normalize(r.CreatedAt)
        });

        return ApiResponse<PagedResult<AdminRobotDto>>.Ok(
            PagedResult<AdminRobotDto>.Create(items, total, page));
    }
}

/// <summary>启用 / 停用机器人</summary>
public sealed class SetRobotEnabledCommand : ICommand<ApiResponse>
{
    public int AdminId { get; set; }

    public long RobotId { get; set; }

    public bool Enabled { get; set; }
}

internal sealed class SetRobotEnabledHandler : IRequestHandler<SetRobotEnabledCommand, ApiResponse>
{
    private readonly IRobotRepository _robots;
    private readonly IAuditLogger _audit;

    public SetRobotEnabledHandler(IRobotRepository robots, IAuditLogger audit)
    {
        _robots = robots;
        _audit = audit;
    }

    public async Task<ApiResponse> Handle(SetRobotEnabledCommand command, CancellationToken ct)
    {
        var robot = await _robots.FindByIdAsync(command.RobotId, ct).ConfigureAwait(false)
                    ?? throw new EntityNotFoundException("机器人不存在");

        robot.SetEnabled(command.Enabled);
        await _robots.UpdateAsync(robot, ct).ConfigureAwait(false);

        await _audit.RecordAsync(
            command.AdminId, AuditActions.RobotSetEnabled, AuditActions.TargetRobot,
            command.RobotId.ToString(CultureInfo.InvariantCulture),
            $"{(command.Enabled ? "启用" : "停用")}机器人「{robot.Name}」", ct).ConfigureAwait(false);

        return ApiResponse.Ok(command.Enabled ? "机器人已启用" : "机器人已停用");
    }
}

/// <summary>管理后台删除机器人（绕过创建者校验）</summary>
public sealed class DeleteRobotByAdminCommand : ICommand<ApiResponse>
{
    public int AdminId { get; set; }

    public long RobotId { get; set; }
}

internal sealed class DeleteRobotByAdminHandler : IRequestHandler<DeleteRobotByAdminCommand, ApiResponse>
{
    private readonly ISender _sender;
    private readonly IAuditLogger _audit;

    public DeleteRobotByAdminHandler(ISender sender, IAuditLogger audit)
    {
        _sender = sender;
        _audit = audit;
    }

    public async Task<ApiResponse> Handle(DeleteRobotByAdminCommand command, CancellationToken ct)
    {
        // 复用用户侧的删除用例（OwnerId=null 表示不校验创建者）
        var result = await _sender
            .Send(new DeleteRobotCommand { RobotId = command.RobotId, OwnerId = null }, ct)
            .ConfigureAwait(false);

        if (result.Success)
        {
            await _audit.RecordAsync(
                command.AdminId, AuditActions.RobotDelete, AuditActions.TargetRobot,
                command.RobotId.ToString(CultureInfo.InvariantCulture),
                "删除机器人", ct).ConfigureAwait(false);
        }

        return result;
    }
}
