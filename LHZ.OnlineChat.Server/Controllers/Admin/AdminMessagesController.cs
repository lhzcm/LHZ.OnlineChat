using LHZ.OnlineChat.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace LHZ.OnlineChat.Server.Controllers.Admin;

/// <summary>
/// 管理后台：消息检索与强制删除
/// </summary>
[ApiController]
[Route("api/admin/messages")]
[AdminAuthorize]
public class AdminMessagesController : AdminControllerBase
{
    private readonly AdminService _adminService;

    public AdminMessagesController(AdminService adminService)
    {
        _adminService = adminService;
    }

    /// <summary>消息检索（关键词/用户/群过滤，私聊+群聊合并，时间倒序）</summary>
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? keyword, [FromQuery] int? userId, [FromQuery] long? groupId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _adminService.SearchMessagesAdminAsync(keyword, userId, groupId, page, pageSize);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>强制删除消息（type=private|group，历史与搜索即隐藏，在线端同步撤回）</summary>
    [HttpDelete("{type}/{id:long}")]
    public async Task<IActionResult> Delete(string type, long id)
    {
        var result = await _adminService.DeleteMessageAdminAsync(AdminId, type, id);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
