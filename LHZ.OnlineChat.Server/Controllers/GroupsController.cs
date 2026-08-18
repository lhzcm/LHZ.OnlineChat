using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LHZ.OnlineChat.Server.Controllers;

/// <summary>
/// 群组管理控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GroupsController : ControllerBase
{
    private readonly GroupService _groupService;
    private readonly BotService _botService;

    public GroupsController(GroupService groupService, BotService botService)
    {
        _groupService = groupService;
        _botService = botService;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }

    /// <summary>
    /// 创建群组
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request)
    {
        var result = await _groupService.CreateGroupAsync(GetCurrentUserId(), request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 获取我的群组列表
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMyGroups()
    {
        var result = await _groupService.GetMyGroupsAsync(GetCurrentUserId());
        return Ok(result);
    }

    /// <summary>
    /// 获取群成员列表
    /// </summary>
    [HttpGet("{groupId}/members")]
    public async Task<IActionResult> GetGroupMembers(long groupId)
    {
        var result = await _groupService.GetGroupMembersAsync(groupId);
        return Ok(result);
    }

    /// <summary>
    /// 邀请好友加入群组（仅群主/管理员）
    /// </summary>
    [HttpPost("{groupId}/invite")]
    public async Task<IActionResult> InviteMembers(long groupId, [FromBody] InviteMembersRequest request)
    {
        var result = await _groupService.InviteMembersAsync(groupId, GetCurrentUserId(), request.UserIds);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 加入群组
    /// </summary>
    [HttpPost("{groupId}/join")]
    public async Task<IActionResult> JoinGroup(long groupId)
    {
        var result = await _groupService.JoinGroupAsync(groupId, GetCurrentUserId());
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 退出群组
    /// </summary>
    [HttpDelete("{groupId}/leave")]
    public async Task<IActionResult> LeaveGroup(long groupId)
    {
        var result = await _groupService.LeaveGroupAsync(groupId, GetCurrentUserId());
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 踢出成员
    /// </summary>
    [HttpDelete("{groupId}/members/{userId:int}")]
    public async Task<IActionResult> KickMember(long groupId, int userId)
    {
        var result = await _groupService.KickMemberAsync(groupId, GetCurrentUserId(), userId);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 设置/清除群公告（仅群主/管理员）
    /// </summary>
    [HttpPut("{groupId}/announcement")]
    public async Task<IActionResult> SetAnnouncement(long groupId, [FromBody] SetAnnouncementRequest request)
    {
        var result = await _groupService.SetAnnouncementAsync(groupId, GetCurrentUserId(), request.Announcement);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 设置/取消管理员（仅群主）
    /// </summary>
    [HttpPut("{groupId}/admin")]
    public async Task<IActionResult> SetAdmin(long groupId, [FromBody] SetAdminRequest request)
    {
        var result = await _groupService.SetAdminAsync(groupId, GetCurrentUserId(), request.UserId, request.IsAdmin);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 添加机器人到群（群主/管理员，仅限自己的机器人）
    /// </summary>
    [HttpPost("{groupId}/robots")]
    public async Task<IActionResult> AddGroupRobot(long groupId, [FromBody] AddGroupRobotRequest request)
    {
        var result = await _botService.AddGroupRobotAsync(groupId, GetCurrentUserId(), request.UserId);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 从群移除机器人（群主/管理员）
    /// </summary>
    [HttpDelete("{groupId}/robots/{userId:int}")]
    public async Task<IActionResult> RemoveGroupRobot(long groupId, int userId)
    {
        var result = await _botService.RemoveGroupRobotAsync(groupId, GetCurrentUserId(), userId);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 群内机器人列表
    /// </summary>
    [HttpGet("{groupId}/robots")]
    public async Task<IActionResult> GetGroupRobots(long groupId)
    {
        var result = await _botService.GetGroupRobotsAsync(groupId);
        return Ok(result);
    }

    /// <summary>
    /// 解散群组
    /// </summary>
    [HttpDelete("{groupId}")]
    public async Task<IActionResult> DismissGroup(long groupId)
    {
        var result = await _groupService.DismissGroupAsync(groupId, GetCurrentUserId());
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
