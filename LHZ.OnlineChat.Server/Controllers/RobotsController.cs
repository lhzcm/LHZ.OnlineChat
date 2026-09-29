using System.Text;
using LHZ.FastJson;
using LHZ.OnlineChat.Application.Robots;
using LHZ.OnlineChat.Application.Robots.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LHZ.OnlineChat.Server.Controllers;

/// <summary>机器人管理与第三方推送入口</summary>
[Route("api/robots")]
[Authorize]
public sealed class RobotsController : ApiControllerBase
{
    /// <summary>签名请求头（与基础设施层的出站签名同名）</summary>
    private const string SignatureHeader = "X-Bot-Signature";

    /// <summary>创建机器人</summary>
    [HttpPost]
    public Task<IActionResult> CreateRobot([FromBody] CreateRobotCommand command, CancellationToken ct)
    {
        command.OwnerId = UserId;
        return Send(command, ct);
    }

    /// <summary>我的机器人列表</summary>
    [HttpGet]
    public Task<IActionResult> GetMyRobots(CancellationToken ct)
        => Send(new GetMyRobotsQuery { OwnerId = UserId }, ct);

    /// <summary>更新机器人配置（仅创建者）</summary>
    [HttpPut("{robotId:long}")]
    public Task<IActionResult> UpdateRobot(
        long robotId, [FromBody] UpdateRobotCommand command, CancellationToken ct)
    {
        command.OwnerId = UserId;
        command.RobotId = robotId;
        return Send(command, ct);
    }

    /// <summary>删除机器人（仅创建者）</summary>
    [HttpDelete("{robotId:long}")]
    public Task<IActionResult> DeleteRobot(long robotId, CancellationToken ct)
        => Send(new DeleteRobotCommand { RobotId = robotId, OwnerId = UserId }, ct);

    /// <summary>测试触发（模拟一条私聊，返回机器人的同步回复）</summary>
    [HttpPost("{robotId:long}/test")]
    public Task<IActionResult> TestRobot(
        long robotId, [FromBody] TestRobotRequest body, CancellationToken ct)
        => Send(new TestRobotCommand
        {
            OwnerId = UserId,
            RobotId = robotId,
            Content = body.Content
        }, ct);

    /// <summary>
    /// 异步回复 / 主动推送（第三方调用）。
    /// URL 里是加密令牌；签名可选 —— 机器人配置了 WebhookSecret 才强制验签。
    /// 必须读原始请求体：验签是对原文做 HMAC，反序列化再序列化会改变字节。
    /// </summary>
    [HttpPost("{robotToken}/reply")]
    [AllowAnonymous]
    public async Task<IActionResult> AsyncReply(string robotToken, CancellationToken ct)
    {
        string rawBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            rawBody = await reader.ReadToEndAsync(ct);
        }

        var signature = Request.Headers.TryGetValue(SignatureHeader, out var values)
            ? values.ToString()
            : null;

        return await Send(new HandleRobotReplyCommand
        {
            Token = robotToken,
            RawBody = rawBody,
            Signature = signature,
            Payload = TryParsePayload(rawBody)
        }, ct);
    }

    /// <summary>解析失败返回 null，由用例给出「请求格式错误」</summary>
    private static RobotReplyPayload? TryParsePayload(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody)) return null;

        try
        {
            var wire = JsonConvert.Deserialize<RobotReplyWire>(rawBody);
            if (wire is null) return null;

            return new RobotReplyPayload
            {
                SessionType = wire.SessionType,
                SessionId = wire.SessionId,
                Content = wire.Content,
                ReplyTo = wire.ReplyTo
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>入站 JSON 契约（camelCase，对第三方公开）</summary>
    private sealed class RobotReplyWire
    {
        [LHZ.FastJson.Json.Attributes.JsonProperty("sessionType")]
        public string SessionType { get; set; } = "private";

        [LHZ.FastJson.Json.Attributes.JsonProperty("sessionId")]
        public long SessionId { get; set; }

        [LHZ.FastJson.Json.Attributes.JsonProperty("content")]
        public string Content { get; set; } = string.Empty;

        [LHZ.FastJson.Json.Attributes.JsonProperty("replyTo")]
        public string? ReplyTo { get; set; }
    }
}

/// <summary>测试触发请求体</summary>
public sealed class TestRobotRequest
{
    public string Content { get; set; } = "你好";
}
