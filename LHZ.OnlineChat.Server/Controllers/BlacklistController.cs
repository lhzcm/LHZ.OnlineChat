using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LHZ.OnlineChat.Server.Controllers;

/// <summary>
/// 黑名单控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BlacklistController : ControllerBase
{
    private readonly BlacklistService _blacklistService;
    private readonly WsMessageHandler _wsMessageHandler;

    public BlacklistController(BlacklistService blacklistService, WsMessageHandler wsMessageHandler)
    {
        _blacklistService = blacklistService;
        _wsMessageHandler = wsMessageHandler;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }

    /// <summary>
    /// 获取我的黑名单
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBlacklist()
    {
        var result = await _blacklistService.GetBlacklistAsync(GetCurrentUserId());
        return Ok(result);
    }

    /// <summary>
    /// 拉黑用户（自动解除好友关系，通知被拉黑者）
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Block([FromBody] AddFriendRequest request)
    {
        var result = await _blacklistService.BlockAsync(GetCurrentUserId(), request.AccountId);
        if (result.Success)
        {
            _wsMessageHandler.NotifyBlockedAsync(request.AccountId, GetCurrentUserId(), "你已被对方拉黑");
        }
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 解除拉黑
    /// </summary>
    [HttpDelete("{userId:int}")]
    public async Task<IActionResult> Unblock(int userId)
    {
        var result = await _blacklistService.UnblockAsync(GetCurrentUserId(), userId);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
