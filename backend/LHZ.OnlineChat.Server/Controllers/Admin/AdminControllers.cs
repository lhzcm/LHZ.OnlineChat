using LHZ.OnlineChat.Application.Admins.Commands;
using LHZ.OnlineChat.Application.Admins.Queries;
using LHZ.OnlineChat.Server.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace LHZ.OnlineChat.Server.Controllers.Admin;

/// <summary>管理后台：认证</summary>
[Route("api/admin/auth")]
public sealed class AdminAuthController : ApiControllerBase
{
    /// <summary>管理员登录</summary>
    [HttpPost("login")]
    public Task<IActionResult> Login([FromBody] AdminLoginCommand command, CancellationToken ct)
        => Send(command, ct);

    /// <summary>当前管理员信息</summary>
    [HttpGet("me")]
    [AdminAuthorize]
    public Task<IActionResult> Me(CancellationToken ct)
        => Send(new GetAdminProfileQuery { AdminId = AdminId }, ct);

    /// <summary>修改自己的密码</summary>
    [HttpPut("password")]
    [AdminAuthorize]
    public Task<IActionResult> ChangePassword(
        [FromBody] ChangeAdminPasswordCommand command, CancellationToken ct)
    {
        command.AdminId = AdminId;
        return Send(command, ct);
    }
}

/// <summary>管理后台：用户管理</summary>
[Route("api/admin/users")]
[AdminAuthorize]
public sealed class AdminUsersController : ApiControllerBase
{
    /// <summary>用户列表（搜索 / 筛选 / 分页）</summary>
    [HttpGet]
    public Task<IActionResult> List(
        [FromQuery] string? keyword,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isBot = null,
        [FromQuery] bool? banned = null,
        CancellationToken ct = default)
        => Send(new ListUsersQuery
        {
            Keyword = keyword,
            Page = page,
            PageSize = pageSize,
            IsBot = isBot,
            Banned = banned
        }, ct);

    /// <summary>用户详情（含登录设备）</summary>
    [HttpGet("{userId:int}")]
    public Task<IActionResult> Detail(int userId, CancellationToken ct)
        => Send(new GetUserDetailQuery { UserId = userId }, ct);

    /// <summary>封禁 / 解封（封禁即踢全部设备）</summary>
    [HttpPut("{userId:int}/ban")]
    public Task<IActionResult> Ban(
        int userId, [FromBody] AdminBanRequest body, CancellationToken ct)
        => Send(new BanUserCommand
        {
            AdminId = AdminId,
            UserId = userId,
            Banned = body.Banned,
            Reason = body.Reason
        }, ct);

    /// <summary>强制下线（踢掉全部设备，不动账号状态）</summary>
    [HttpPost("{userId:int}/kick")]
    public Task<IActionResult> Kick(int userId, CancellationToken ct)
        => Send(new KickUserCommand { AdminId = AdminId, UserId = userId }, ct);

    /// <summary>重置密码（免验证码，旧会话失效）</summary>
    [HttpPut("{userId:int}/password")]
    public Task<IActionResult> ResetPassword(
        int userId, [FromBody] AdminResetPasswordRequest body, CancellationToken ct)
        => Send(new ResetUserPasswordCommand
        {
            AdminId = AdminId,
            UserId = userId,
            NewPassword = body.NewPassword
        }, ct);
}

/// <summary>管理后台：管理员管理与审计日志（均限超管）</summary>
[AdminAuthorize(SuperOnly = true)]
public sealed class AdminAdminsController : ApiControllerBase
{
    /// <summary>管理员列表</summary>
    [HttpGet("api/admin/admins")]
    public Task<IActionResult> List(CancellationToken ct)
        => Send(new ListAdminsQuery(), ct);

    /// <summary>创建管理员</summary>
    [HttpPost("api/admin/admins")]
    public Task<IActionResult> Create([FromBody] CreateAdminCommand command, CancellationToken ct)
    {
        command.OperatorId = AdminId;
        return Send(command, ct);
    }

    /// <summary>更新管理员（角色 / 状态）</summary>
    [HttpPut("api/admin/admins/{id:int}")]
    public Task<IActionResult> Update(
        int id, [FromBody] AdminUpdateRequest body, CancellationToken ct)
        => Send(new UpdateAdminCommand
        {
            OperatorId = AdminId,
            TargetId = id,
            Role = body.Role,
            Status = body.Status
        }, ct);

    /// <summary>删除管理员</summary>
    [HttpDelete("api/admin/admins/{id:int}")]
    public Task<IActionResult> Delete(int id, CancellationToken ct)
        => Send(new DeleteAdminCommand { OperatorId = AdminId, TargetId = id }, ct);

    /// <summary>审计日志（分页）</summary>
    [HttpGet("api/admin/logs")]
    public Task<IActionResult> Logs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? action = null,
        CancellationToken ct = default)
        => Send(new ListAuditLogsQuery { Page = page, PageSize = pageSize, Action = action }, ct);
}

/// <summary>管理后台：仪表盘</summary>
[Route("api/admin/dashboard")]
[AdminAuthorize]
public sealed class AdminDashboardController : ApiControllerBase
{
    /// <summary>概览统计（卡片 + 趋势 + TOP 排行）</summary>
    [HttpGet("overview")]
    public Task<IActionResult> Overview(CancellationToken ct)
        => Send(new GetDashboardQuery(), ct);
}

/// <summary>管理后台：群管理</summary>
[Route("api/admin/groups")]
[AdminAuthorize]
public sealed class AdminGroupsController : ApiControllerBase
{
    [HttpGet]
    public Task<IActionResult> List(
        [FromQuery] string? keyword,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Send(new ListGroupsQuery { Keyword = keyword, Page = page, PageSize = pageSize }, ct);

    [HttpGet("{groupId:long}")]
    public Task<IActionResult> Detail(long groupId, CancellationToken ct)
        => Send(new GetGroupDetailQuery { GroupId = groupId }, ct);

    /// <summary>强制解散群</summary>
    [HttpDelete("{groupId:long}")]
    public Task<IActionResult> Dissolve(long groupId, CancellationToken ct)
        => Send(new DissolveGroupByAdminCommand { AdminId = AdminId, GroupId = groupId }, ct);

    /// <summary>强制移除成员</summary>
    [HttpDelete("{groupId:long}/members/{userId:int}")]
    public Task<IActionResult> RemoveMember(long groupId, int userId, CancellationToken ct)
        => Send(new RemoveGroupMemberByAdminCommand
        {
            AdminId = AdminId,
            GroupId = groupId,
            UserId = userId
        }, ct);

    /// <summary>禁言 / 解除禁言</summary>
    [HttpPut("{groupId:long}/members/{userId:int}/mute")]
    public Task<IActionResult> MuteMember(
        long groupId, int userId, [FromBody] AdminMuteRequest body, CancellationToken ct)
        => Send(new MuteGroupMemberCommand
        {
            AdminId = AdminId,
            GroupId = groupId,
            UserId = userId,
            MutedUntil = body.MutedUntil
        }, ct);

    /// <summary>转让群主</summary>
    [HttpPut("{groupId:long}/owner")]
    public Task<IActionResult> TransferOwner(
        long groupId, [FromBody] AdminTransferOwnerRequest body, CancellationToken ct)
        => Send(new TransferGroupOwnerCommand
        {
            AdminId = AdminId,
            GroupId = groupId,
            NewOwnerId = body.NewOwnerId
        }, ct);
}

/// <summary>管理后台：消息检索与强制删除</summary>
[Route("api/admin/messages")]
[AdminAuthorize]
public sealed class AdminMessagesController : ApiControllerBase
{
    /// <summary>消息检索（关键词 / 用户 / 群过滤，私聊 + 群聊合并倒序）</summary>
    [HttpGet]
    public Task<IActionResult> Search(
        [FromQuery] string? keyword,
        [FromQuery] int? userId,
        [FromQuery] long? groupId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Send(new SearchMessagesByAdminQuery
        {
            Keyword = keyword,
            UserId = userId,
            GroupId = groupId,
            Page = page,
            PageSize = pageSize
        }, ct);

    /// <summary>强制删除消息（type=private|group）</summary>
    [HttpDelete("{type}/{id:long}")]
    public Task<IActionResult> Delete(string type, long id, CancellationToken ct)
        => Send(new DeleteMessageByAdminCommand { AdminId = AdminId, Type = type, MessageId = id }, ct);
}

/// <summary>管理后台：机器人管理</summary>
[Route("api/admin/robots")]
[AdminAuthorize]
public sealed class AdminRobotsController : ApiControllerBase
{
    [HttpGet]
    public Task<IActionResult> List(
        [FromQuery] string? keyword,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Send(new ListRobotsQuery { Keyword = keyword, Page = page, PageSize = pageSize }, ct);

    /// <summary>启用 / 停用</summary>
    [HttpPut("{robotId:long}/status")]
    public Task<IActionResult> SetEnabled(
        long robotId, [FromBody] RobotStatusRequest body, CancellationToken ct)
        => Send(new SetRobotEnabledCommand
        {
            AdminId = AdminId,
            RobotId = robotId,
            Enabled = body.Enabled
        }, ct);

    [HttpDelete("{robotId:long}")]
    public Task<IActionResult> Delete(long robotId, CancellationToken ct)
        => Send(new DeleteRobotByAdminCommand { AdminId = AdminId, RobotId = robotId }, ct);
}

// ==================== 管理后台请求体（字段名与改造前一致） ====================

public sealed class AdminBanRequest
{
    public bool Banned { get; set; }

    public string? Reason { get; set; }
}

public sealed class AdminResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class AdminUpdateRequest
{
    public int? Role { get; set; }

    public int? Status { get; set; }
}

public sealed class AdminMuteRequest
{
    /// <summary>禁言截止时间（UTC）；null 或过去时间 = 解除禁言</summary>
    public DateTime? MutedUntil { get; set; }
}

public sealed class AdminTransferOwnerRequest
{
    public int NewOwnerId { get; set; }
}

/// <summary>启停机器人请求体（用对象包装，避免 axios 传裸 bool 不被序列化）</summary>
public sealed class RobotStatusRequest
{
    public bool Enabled { get; set; }
}
