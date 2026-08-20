using LHZ.OnlineChat.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace LHZ.OnlineChat.Server.Controllers.Admin;

/// <summary>
/// 管理后台：仪表盘统计
/// </summary>
[ApiController]
[Route("api/admin/dashboard")]
[AdminAuthorize]
public class AdminDashboardController : AdminControllerBase
{
    private readonly AdminService _adminService;

    public AdminDashboardController(AdminService adminService)
    {
        _adminService = adminService;
    }

    /// <summary>概览统计（在线/总数/今日/7 日趋势）</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview()
    {
        var result = await _adminService.GetDashboardAsync();
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
