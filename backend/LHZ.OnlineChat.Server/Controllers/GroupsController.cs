using LHZ.OnlineChat.Application.Groups.Commands;
using LHZ.OnlineChat.Application.Groups.Queries;
using LHZ.OnlineChat.Application.Robots.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LHZ.OnlineChat.Server.Controllers;

/// <summary>群组管理</summary>
[Route("api/[controller]")]
[Authorize]
public sealed class GroupsController : ApiControllerBase
{
    /// <summary>创建群组</summary>
    [HttpPost]
    public Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest body, CancellationToken ct)
        => Send(new CreateGroupCommand
        {
            OwnerId = UserId,
            Name = body.Name,
            Avatar = body.Avatar
        }, ct);

    /// <summary>我的群组列表</summary>
    [HttpGet]
    public Task<IActionResult> GetMyGroups(CancellationToken ct)
        => Send(new GetMyGroupsQuery { UserId = UserId }, ct);

    /// <summary>群成员列表（仅群成员可见）</summary>
    [HttpGet("{groupId:long}/members")]
    public Task<IActionResult> GetGroupMembers(long groupId, CancellationToken ct)
        => Send(new GetGroupMembersQuery { GroupId = groupId, RequesterId = UserId }, ct);

    /// <summary>设置入群方式（仅群主/管理员）：开放加入 或 仅限邀请</summary>
    [HttpPut("{groupId:long}/join-policy")]
    public Task<IActionResult> SetJoinPolicy(
        long groupId, [FromBody] SetJoinPolicyRequest body, CancellationToken ct)
        => Send(new SetGroupJoinPolicyCommand
        {
            GroupId = groupId,
            OperatorId = UserId,
            OpenToJoin = body.OpenToJoin
        }, ct);

    /// <summary>邀请好友入群（仅群主/管理员）</summary>
    [HttpPost("{groupId:long}/invite")]
    public Task<IActionResult> InviteMembers(
        long groupId, [FromBody] InviteMembersRequest body, CancellationToken ct)
        => Send(new InviteGroupMembersCommand
        {
            GroupId = groupId,
            OperatorId = UserId,
            UserIds = body.UserIds
        }, ct);

    /// <summary>加入群组</summary>
    [HttpPost("{groupId:long}/join")]
    public Task<IActionResult> JoinGroup(long groupId, CancellationToken ct)
        => Send(new JoinGroupCommand { GroupId = groupId, UserId = UserId }, ct);

    /// <summary>退出群组</summary>
    [HttpDelete("{groupId:long}/leave")]
    public Task<IActionResult> LeaveGroup(long groupId, CancellationToken ct)
        => Send(new LeaveGroupCommand { GroupId = groupId, UserId = UserId }, ct);

    /// <summary>踢出成员</summary>
    [HttpDelete("{groupId:long}/members/{userId:int}")]
    public Task<IActionResult> KickMember(long groupId, int userId, CancellationToken ct)
        => Send(new KickGroupMemberCommand
        {
            GroupId = groupId,
            OperatorId = UserId,
            TargetUserId = userId
        }, ct);

    /// <summary>设置/清除群公告（仅群主/管理员）</summary>
    [HttpPut("{groupId:long}/announcement")]
    public Task<IActionResult> SetAnnouncement(
        long groupId, [FromBody] SetAnnouncementRequest body, CancellationToken ct)
        => Send(new SetGroupAnnouncementCommand
        {
            GroupId = groupId,
            OperatorId = UserId,
            Announcement = body.Announcement
        }, ct);

    /// <summary>设置/取消管理员（仅群主）</summary>
    [HttpPut("{groupId:long}/admin")]
    public Task<IActionResult> SetAdmin(
        long groupId, [FromBody] SetAdminRequest body, CancellationToken ct)
        => Send(new SetGroupAdminCommand
        {
            GroupId = groupId,
            OperatorId = UserId,
            TargetUserId = body.UserId,
            IsAdmin = body.IsAdmin
        }, ct);

    /// <summary>解散群组（仅群主）</summary>
    [HttpDelete("{groupId:long}")]
    public Task<IActionResult> DismissGroup(long groupId, CancellationToken ct)
        => Send(new DismissGroupCommand { GroupId = groupId, UserId = UserId }, ct);

    // ==================== 群机器人 ====================

    /// <summary>群内机器人列表（仅群成员可见）</summary>
    [HttpGet("{groupId:long}/robots")]
    public Task<IActionResult> GetGroupRobots(long groupId, CancellationToken ct)
        => Send(new GetGroupRobotsQuery { GroupId = groupId, RequesterId = UserId }, ct);

    /// <summary>添加机器人到群（仅限自己创建的机器人）</summary>
    [HttpPost("{groupId:long}/robots")]
    public Task<IActionResult> AddGroupRobot(
        long groupId, [FromBody] AddGroupRobotRequest body, CancellationToken ct)
        => Send(new AddGroupRobotCommand
        {
            GroupId = groupId,
            OperatorId = UserId,
            RobotUserId = body.UserId
        }, ct);

    /// <summary>从群移除机器人</summary>
    [HttpDelete("{groupId:long}/robots/{userId:int}")]
    public Task<IActionResult> RemoveGroupRobot(long groupId, int userId, CancellationToken ct)
        => Send(new RemoveGroupRobotCommand
        {
            GroupId = groupId,
            OperatorId = UserId,
            RobotUserId = userId
        }, ct);
}

// ==================== 请求体 ====================

public sealed class CreateGroupRequest
{
    public string Name { get; set; } = string.Empty;

    public string? Avatar { get; set; }
}

public sealed class InviteMembersRequest
{
    /// <summary>要邀请的好友账号 ID 列表</summary>
    public List<int> UserIds { get; set; } = new();
}

public sealed class SetAnnouncementRequest
{
    /// <summary>公告内容；空字符串表示清除</summary>
    public string Announcement { get; set; } = string.Empty;
}

public sealed class SetAdminRequest
{
    public int UserId { get; set; }

    /// <summary>true=设为管理员，false=取消</summary>
    public bool IsAdmin { get; set; }
}

public sealed class SetJoinPolicyRequest
{
    /// <summary>true=开放加入（知道群 ID 即可加入），false=仅限邀请（默认）</summary>
    public bool OpenToJoin { get; set; }
}

public sealed class AddGroupRobotRequest
{
    /// <summary>机器人账号 ID（User.Id）</summary>
    public int UserId { get; set; }
}
