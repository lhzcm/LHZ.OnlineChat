using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace LHZ.OnlineChat.Server.Controllers.Admin;

/// <summary>
/// 管理后台：用户管理
/// </summary>
[ApiController]
[Route("api/admin/users")]
[AdminAuthorize]
public class AdminUsersController : AdminControllerBase
{
    private readonly AdminService _adminService;

    public AdminUsersController(AdminService adminService)
    {
        _adminService = adminService;
    }

    /// <summary>用户列表（搜索/筛选/分页）</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? keyword, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] bool? isBot = null, [FromQuery] bool? banned = null)
    {
        var result = await _adminService.ListUsersAsync(keyword, page, pageSize, isBot, banned);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>用户详情（含登录设备）</summary>
    [HttpGet("{userId:int}")]
    public async Task<IActionResult> Detail(int userId)
    {
        var result = await _adminService.GetUserDetailAsync(userId);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>封禁/解封（封禁即踢全部设备）</summary>
    [HttpPut("{userId:int}/ban")]
    public async Task<IActionResult> Ban(int userId, [FromBody] AdminBanRequest request)
    {
        var result = await _adminService.BanUserAsync(AdminId, userId, request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>强制下线（踢掉全部设备）</summary>
    [HttpPost("{userId:int}/kick")]
    public async Task<IActionResult> Kick(int userId)
    {
        var result = await _adminService.KickUserAsync(AdminId, userId);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>重置密码（免验证码，旧会话失效）</summary>
    [HttpPut("{userId:int}/password")]
    public async Task<IActionResult> ResetPassword(int userId, [FromBody] AdminResetPasswordRequest request)
    {
        var result = await _adminService.ResetUserPasswordAsync(AdminId, userId, request);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
