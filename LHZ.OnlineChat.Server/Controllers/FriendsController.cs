using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LHZ.OnlineChat.Server.Controllers;

/// <summary>
/// 好友管理控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FriendsController : ControllerBase
{
    private readonly FriendService _friendService;

    public FriendsController(FriendService friendService)
    {
        _friendService = friendService;
    }

    private long GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(claim, out var id) ? id : 0;
    }

    /// <summary>
    /// 发送好友申请
    /// </summary>
    [HttpPost("request")]
    public async Task<IActionResult> SendRequest([FromBody] AddFriendRequest request)
    {
        var result = await _friendService.SendFriendRequestAsync(GetCurrentUserId(), request.Username);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 接受好友申请
    /// </summary>
    [HttpPut("accept/{requestId}")]
    public async Task<IActionResult> AcceptRequest(long requestId)
    {
        var result = await _friendService.AcceptFriendRequestAsync(requestId, GetCurrentUserId());
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 拒绝好友申请
    /// </summary>
    [HttpDelete("reject/{requestId}")]
    public async Task<IActionResult> RejectRequest(long requestId)
    {
        var result = await _friendService.RejectFriendRequestAsync(requestId, GetCurrentUserId());
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 删除好友
    /// </summary>
    [HttpDelete("{friendId}")]
    public async Task<IActionResult> DeleteFriend(long friendId)
    {
        var result = await _friendService.DeleteFriendAsync(GetCurrentUserId(), friendId);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 获取好友列表
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetFriends()
    {
        var result = await _friendService.GetFriendsAsync(GetCurrentUserId());
        return Ok(result);
    }

    /// <summary>
    /// 获取待处理的好友申请
    /// </summary>
    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingRequests()
    {
        var result = await _friendService.GetPendingRequestsAsync(GetCurrentUserId());
        return Ok(result);
    }
}
