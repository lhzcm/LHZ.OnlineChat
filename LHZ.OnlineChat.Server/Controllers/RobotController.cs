using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LHZ.OnlineChat.Server.Controllers;

/// <summary>
/// 机器人控制器（Webhook 机器人管理 + 群机器人 + 异步回复）
/// </summary>
[ApiController]
[Route("api/robots")]
[Authorize]
public class RobotController : ControllerBase
{
    private readonly BotService _botService;

    public RobotController(BotService botService)
    {
        _botService = botService;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }

    /// <summary>
    /// 创建机器人（自动生成机器人账号 + 与创建者建立好友关系）
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateRobot([FromBody] CreateRobotRequest request)
    {
        var result = await _botService.CreateRobotAsync(GetCurrentUserId(), request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 我的机器人列表
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMyRobots()
    {
        var result = await _botService.GetMyRobotsAsync(GetCurrentUserId());
        return Ok(result);
    }

    /// <summary>
    /// 更新机器人配置（仅创建者）
    /// </summary>
    [HttpPut("{robotId:long}")]
    public async Task<IActionResult> UpdateRobot(long robotId, [FromBody] UpdateRobotRequest request)
    {
        var result = await _botService.UpdateRobotAsync(GetCurrentUserId(), robotId, request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 删除机器人（仅创建者）
    /// </summary>
    [HttpDelete("{robotId:long}")]
    public async Task<IActionResult> DeleteRobot(long robotId)
    {
        var result = await _botService.DeleteRobotAsync(GetCurrentUserId(), robotId);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 测试触发机器人（模拟一条私聊消息，返回机器人同步回复）
    /// </summary>
    [HttpPost("{robotId:long}/test")]
    public async Task<IActionResult> TestRobot(long robotId, [FromBody] TestRobotRequest request)
    {
        var result = await _botService.TestRobotAsync(GetCurrentUserId(), robotId, request.Content);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 异步回复（Webhook 服务方调用；需携带 X-Bot-Signature: HMAC-SHA256(secret, rawBody)）
    /// </summary>
    [HttpPost("{robotId:long}/reply")]
    [AllowAnonymous]
    public async Task<IActionResult> AsyncReply(long robotId)
    {
        string rawBody;
        using (var reader = new StreamReader(Request.Body, System.Text.Encoding.UTF8))
        {
            rawBody = await reader.ReadToEndAsync();
        }

        var signature = Request.Headers.TryGetValue(BotService.SignatureHeader, out var values)
            ? values.ToString()
            : null;

        var result = await _botService.HandleAsyncReplyAsync(robotId, rawBody, signature);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
