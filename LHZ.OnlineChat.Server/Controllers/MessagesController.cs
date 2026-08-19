using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LHZ.OnlineChat.Server.Controllers;

/// <summary>
/// 消息控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly MessageService _messageService;

    public MessagesController(MessageService messageService)
    {
        _messageService = messageService;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }

    /// <summary>
    /// 获取私聊历史消息
    /// </summary>
    [HttpGet("private/{friendId:int}")]
    public async Task<IActionResult> GetPrivateHistory(
        int friendId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var result = await _messageService.GetPrivateHistoryAsync(
            GetCurrentUserId(), friendId, page, pageSize);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 获取群聊历史消息
    /// </summary>
    [HttpGet("group/{groupId}")]
    public async Task<IActionResult> GetGroupHistory(
        long groupId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var result = await _messageService.GetGroupHistoryAsync(
            groupId, GetCurrentUserId(), page, pageSize);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 标记群消息已读（推进已读游标）
    /// </summary>
    [HttpPut("group/{groupId}/read")]
    public async Task<IActionResult> MarkGroupAsRead(long groupId)
    {
        var result = await _messageService.MarkGroupAsReadAsync(groupId, GetCurrentUserId());
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 搜索消息（私聊 + 群聊；scopeType/scopeId 可选，限定在单个会话内搜索）
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchMessages(
        [FromQuery] string keyword, [FromQuery] int page = 1, [FromQuery] int pageSize = 30,
        [FromQuery] string? scopeType = null, [FromQuery] long? scopeId = null)
    {
        var result = await _messageService.SearchMessagesAsync(GetCurrentUserId(), keyword, page, pageSize, scopeType, scopeId);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 获取会话列表（私聊 + 群聊聚合）
    /// </summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions()
    {
        var result = await _messageService.GetSessionsAsync(GetCurrentUserId());
        return Ok(result);
    }

    /// <summary>
    /// 更新会话设置（置顶 / 免打扰）
    /// </summary>
    [HttpPut("session-setting")]
    public async Task<IActionResult> UpdateSessionSetting([FromBody] UpdateSessionSettingRequest request)
    {
        var result = await _messageService.UpdateSessionSettingAsync(GetCurrentUserId(), request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 标记消息已读
    /// </summary>
    [HttpPut("{messageId}/read")]
    public async Task<IActionResult> MarkAsRead(long messageId)
    {
        var result = await _messageService.MarkAsReadAsync(messageId, GetCurrentUserId());
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 批量标记某用户的消息已读
    /// </summary>
    [HttpPut("read-all/{senderId:int}")]
    public async Task<IActionResult> MarkAllAsRead(int senderId)
    {
        var result = await _messageService.MarkAllAsReadAsync(senderId, GetCurrentUserId());
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 获取未读消息数
    /// </summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var result = await _messageService.GetUnreadCountAsync(GetCurrentUserId());
        return Ok(result);
    }

    /// <summary>
    /// 获取离线消息（用户上线时调用）
    /// </summary>
    [HttpGet("offline")]
    public async Task<IActionResult> GetOfflineMessages()
    {
        var result = await _messageService.GetOfflineMessagesAsync(GetCurrentUserId());
        return Ok(result);
    }
}
