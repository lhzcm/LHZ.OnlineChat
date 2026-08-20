using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace LHZ.OnlineChat.Server.Controllers.Admin;

/// <summary>
/// 管理后台：认证
/// </summary>
[ApiController]
[Route("api/admin/auth")]
public class AdminAuthController : AdminControllerBase
{
    private readonly AdminService _adminService;

    public AdminAuthController(AdminService adminService)
    {
        _adminService = adminService;
    }

    /// <summary>管理员登录</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] AdminLoginRequest request)
    {
        var result = await _adminService.LoginAsync(request, ClientIp ?? string.Empty);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>当前管理员信息</summary>
    [HttpGet("me")]
    [AdminAuthorize]
    public async Task<IActionResult> Me()
    {
        var result = await _adminService.GetMeAsync(AdminId);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>修改自己的密码</summary>
    [HttpPut("password")]
    [AdminAuthorize]
    public async Task<IActionResult> ChangePassword([FromBody] AdminChangePasswordRequest request)
    {
        var result = await _adminService.ChangePasswordAsync(AdminId, request);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
