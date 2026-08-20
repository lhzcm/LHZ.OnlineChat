using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace LHZ.OnlineChat.Server.Controllers.Admin;

/// <summary>
/// 管理后台：管理员管理（仅超管）与审计日志（仅超管）
/// </summary>
[ApiController]
[AdminAuthorize(SuperOnly = true)]
public class AdminAdminsController : AdminControllerBase
{
    private readonly AdminService _adminService;

    public AdminAdminsController(AdminService adminService)
    {
        _adminService = adminService;
    }

    /// <summary>管理员列表</summary>
    [HttpGet("api/admin/admins")]
    public async Task<IActionResult> List()
    {
        var result = await _adminService.ListAdminsAsync();
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>创建管理员</summary>
    [HttpPost("api/admin/admins")]
    public async Task<IActionResult> Create([FromBody] AdminCreateRequest request)
    {
        var result = await _adminService.CreateAdminAsync(AdminId, request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>更新管理员（角色/状态）</summary>
    [HttpPut("api/admin/admins/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AdminUpdateRequest request)
    {
        var result = await _adminService.UpdateAdminAsync(AdminId, id, request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>删除管理员</summary>
    [HttpDelete("api/admin/admins/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _adminService.DeleteAdminAsync(AdminId, id);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>审计日志（分页）</summary>
    [HttpGet("api/admin/logs")]
    public async Task<IActionResult> Logs([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? action = null)
    {
        var result = await _adminService.ListLogsAsync(page, pageSize, action);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
