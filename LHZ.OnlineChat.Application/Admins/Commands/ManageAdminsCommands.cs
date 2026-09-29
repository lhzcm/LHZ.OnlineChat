using System.Globalization;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Admins.Commands;

/// <summary>管理员列表（仅超管）</summary>
public sealed class ListAdminsQuery : IQuery<ApiResponse<List<AdminInfo>>>
{
}

internal sealed class ListAdminsHandler : IRequestHandler<ListAdminsQuery, ApiResponse<List<AdminInfo>>>
{
    private readonly IAdminRepository _admins;

    public ListAdminsHandler(IAdminRepository admins) => _admins = admins;

    public async Task<ApiResponse<List<AdminInfo>>> Handle(ListAdminsQuery query, CancellationToken ct)
    {
        var admins = await _admins.ListAllAsync(ct).ConfigureAwait(false);
        return ApiResponse<List<AdminInfo>>.Ok(admins.Select(a => a.ToInfo()).ToList());
    }
}

/// <summary>创建管理员（仅超管）</summary>
public sealed class CreateAdminCommand : ICommand<ApiResponse>
{
    public int OperatorId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>0=超管 1=运营</summary>
    public int Role { get; set; } = 1;
}

internal sealed class CreateAdminHandler : IRequestHandler<CreateAdminCommand, ApiResponse>
{
    private readonly IAdminRepository _admins;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;

    public CreateAdminHandler(
        IAdminRepository admins, IPasswordHasher hasher, IAuditLogger audit, IClock clock)
    {
        _admins = admins;
        _hasher = hasher;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(CreateAdminCommand command, CancellationToken ct)
    {
        var username = Admin.NormalizeUsername(command.Username);
        PasswordHash.EnsureRawPasswordValid(command.Password);

        var role = ParseRole(command.Role);

        var exists = await _admins.UsernameExistsAsync(username, ct).ConfigureAwait(false);
        DomainException.Ensure(!exists, "该管理员账号已存在");

        var admin = Admin.Create(username, _hasher.Hash(command.Password), role, _clock.UtcNow);
        await _admins.AddAsync(admin, ct).ConfigureAwait(false);

        await _audit.RecordAsync(
            command.OperatorId, AuditActions.AdminCreate, AuditActions.TargetAdmin,
            admin.Id.ToString(CultureInfo.InvariantCulture),
            $"创建管理员 {username}（角色 {(role == AdminRole.Super ? "超管" : "运营")}）", ct).ConfigureAwait(false);

        return ApiResponse.Ok("管理员已创建");
    }

    internal static AdminRole ParseRole(int raw)
    {
        DomainException.Ensure(raw is 0 or 1, "无效的角色");
        return (AdminRole)raw;
    }
}

/// <summary>更新管理员角色/状态（仅超管）</summary>
public sealed class UpdateAdminCommand : ICommand<ApiResponse>
{
    public int OperatorId { get; set; }

    public int TargetId { get; set; }

    public int? Role { get; set; }

    public int? Status { get; set; }
}

internal sealed class UpdateAdminHandler : IRequestHandler<UpdateAdminCommand, ApiResponse>
{
    private readonly IAdminRepository _admins;
    private readonly IAuditLogger _audit;

    public UpdateAdminHandler(IAdminRepository admins, IAuditLogger audit)
    {
        _admins = admins;
        _audit = audit;
    }

    public async Task<ApiResponse> Handle(UpdateAdminCommand command, CancellationToken ct)
    {
        var target = await _admins.FindByIdAsync(command.TargetId, ct).ConfigureAwait(false)
                     ?? throw new EntityNotFoundException("管理员不存在");

        AdminRole? role = command.Role.HasValue ? CreateAdminHandler.ParseRole(command.Role.Value) : null;
        AdminStatus? status = null;
        if (command.Status.HasValue)
        {
            DomainException.Ensure(command.Status.Value is 0 or 1, "无效的状态");
            status = (AdminStatus)command.Status.Value;
        }

        // 「不能停用或降级自己」在聚合根里
        target.ChangeRoleAndStatus(role, status, command.OperatorId);
        await _admins.UpdateAsync(target, ct).ConfigureAwait(false);

        await _audit.RecordAsync(
            command.OperatorId, AuditActions.AdminUpdate, AuditActions.TargetAdmin,
            command.TargetId.ToString(CultureInfo.InvariantCulture),
            $"更新管理员 {target.Username}（role={command.Role} status={command.Status}）", ct).ConfigureAwait(false);

        return ApiResponse.Ok("已更新");
    }
}

/// <summary>删除管理员（仅超管；不能删自己、不能删超管）</summary>
public sealed class DeleteAdminCommand : ICommand<ApiResponse>
{
    public int OperatorId { get; set; }

    public int TargetId { get; set; }
}

internal sealed class DeleteAdminHandler : IRequestHandler<DeleteAdminCommand, ApiResponse>
{
    private readonly IAdminRepository _admins;
    private readonly IAuditLogger _audit;

    public DeleteAdminHandler(IAdminRepository admins, IAuditLogger audit)
    {
        _admins = admins;
        _audit = audit;
    }

    public async Task<ApiResponse> Handle(DeleteAdminCommand command, CancellationToken ct)
    {
        var target = await _admins.FindByIdAsync(command.TargetId, ct).ConfigureAwait(false)
                     ?? throw new EntityNotFoundException("管理员不存在");

        target.EnsureDeletableBy(command.OperatorId);
        await _admins.DeleteAsync(command.TargetId, ct).ConfigureAwait(false);

        await _audit.RecordAsync(
            command.OperatorId, AuditActions.AdminDelete, AuditActions.TargetAdmin,
            command.TargetId.ToString(CultureInfo.InvariantCulture),
            $"删除管理员 {target.Username}", ct).ConfigureAwait(false);

        return ApiResponse.Ok("已删除");
    }
}

/// <summary>审计日志（仅超管，分页）</summary>
public sealed class ListAuditLogsQuery : IQuery<ApiResponse<PagedResult<AdminLogDto>>>
{
    private const int MaxPageSize = 100;

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public string? Action { get; set; }

    internal PageRequest ToPageRequest() => new(Page, PageSize, MaxPageSize);
}

internal sealed class ListAuditLogsHandler
    : IRequestHandler<ListAuditLogsQuery, ApiResponse<PagedResult<AdminLogDto>>>
{
    private readonly IAdminAuditLogRepository _logs;

    public ListAuditLogsHandler(IAdminAuditLogRepository logs) => _logs = logs;

    public async Task<ApiResponse<PagedResult<AdminLogDto>>> Handle(
        ListAuditLogsQuery query, CancellationToken ct)
    {
        var page = query.ToPageRequest();
        var (items, total) = await _logs
            .PageAsync(page, query.Action?.Trim(), ct)
            .ConfigureAwait(false);

        return ApiResponse<PagedResult<AdminLogDto>>.Ok(
            PagedResult<AdminLogDto>.Create(items.Select(l => l.ToDto()), total, page));
    }
}
