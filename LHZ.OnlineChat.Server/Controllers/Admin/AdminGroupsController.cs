using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace LHZ.OnlineChat.Server.Controllers.Admin;

/// <summary>
/// 管理后台：群管理（列表/详情/解散/移除成员/禁言/转让群主）
/// </summary>
[ApiController]
[Route("api/admin/groups")]
[AdminAuthorize]
public class AdminGroupsController : AdminControllerBase
{
    private readonly AdminService _adminService;

    public AdminGroupsController(AdminService adminService)
    {
        _adminService = adminService;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? keyword, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _adminService.ListGroupsAsync(keyword, page, pageSize);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("{groupId:long}")]
    public async Task<IActionResult> Detail(long groupId)
    {
        var result = await _adminService.GetGroupDetailAsync(groupId);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("{groupId:long}")]
    public async Task<IActionResult> Dissolve(long groupId)
    {
        var result = await _adminService.DissolveGroupAsync(AdminId, groupId);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("{groupId:long}/members/{userId:int}")]
    public async Task<IActionResult> RemoveMember(long groupId, int userId)
    {
        var result = await _adminService.RemoveGroupMemberAsync(AdminId, groupId, userId);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("{groupId:long}/members/{userId:int}/mute")]
    public async Task<IActionResult> MuteMember(long groupId, int userId, [FromBody] AdminMuteRequest request)
    {
        var result = await _adminService.MuteGroupMemberAsync(AdminId, groupId, userId, request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("{groupId:long}/owner")]
    public async Task<IActionResult> TransferOwner(long groupId, [FromBody] AdminTransferOwnerRequest request)
    {
        var result = await _adminService.TransferGroupOwnerAsync(AdminId, groupId, request);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
