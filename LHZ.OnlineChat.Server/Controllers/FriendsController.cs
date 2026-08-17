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

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }

    /// <summary>
    /// 发送好友申请（按账号 ID）
    /// </summary>
    [HttpPost("request")]
    public async Task<IActionResult> SendRequest([FromBody] AddFriendRequest request)
    {
        var result = await _friendService.SendFriendRequestAsync(GetCurrentUserId(), request.AccountId);
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
    [HttpDelete("{friendId:int}")]
    public async Task<IActionResult> DeleteFriend(int friendId)
    {
        var result = await _friendService.DeleteFriendAsync(GetCurrentUserId(), friendId);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 设置好友备注（空 = 清除）
    /// </summary>
    [HttpPut("{friendId:int}/remark")]
    public async Task<IActionResult> SetRemark(int friendId, [FromBody] SetFriendRemarkRequest request)
    {
        var result = await _friendService.SetFriendRemarkAsync(GetCurrentUserId(), friendId, request.Remark);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 设置好友分类标签（空 = 清除，未分组）
    /// </summary>
    [HttpPut("{friendId:int}/category")]
    public async Task<IActionResult> SetCategory(int friendId, [FromBody] SetFriendCategoryRequest request)
    {
        var result = await _friendService.SetFriendCategoryAsync(GetCurrentUserId(), friendId, request.Category);
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
