using LHZ.OnlineChat.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace LHZ.OnlineChat.Server.Controllers.Admin;

/// <summary>
/// 管理后台：机器人管理（列表/启停/删除/统计）
/// </summary>
[ApiController]
[Route("api/admin/robots")]
[AdminAuthorize]
public class AdminRobotsController : AdminControllerBase
{
    private readonly AdminService _adminService;

    public AdminRobotsController(AdminService adminService)
    {
        _adminService = adminService;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? keyword, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _adminService.ListRobotsAsync(keyword, page, pageSize);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("{robotId:long}/status")]
    public async Task<IActionResult> SetEnabled(long robotId, [FromBody] RobotStatusRequest request)
    {
        var result = await _adminService.SetRobotEnabledAsync(AdminId, robotId, request.Enabled);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("{robotId:long}")]
    public async Task<IActionResult> Delete(long robotId)
    {
        var result = await _adminService.DeleteRobotAsync(AdminId, robotId);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

public class RobotStatusRequest
{
    public bool Enabled { get; set; }
}
