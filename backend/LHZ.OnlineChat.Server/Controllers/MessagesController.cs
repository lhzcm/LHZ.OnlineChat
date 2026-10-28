using LHZ.OnlineChat.Application.Files.Commands;
using LHZ.OnlineChat.Application.Files.Queries;
using LHZ.OnlineChat.Application.Messaging.Commands;
using LHZ.OnlineChat.Application.Messaging.Queries;
using LHZ.OnlineChat.Application.Users.Commands;
using LHZ.OnlineChat.Server.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LHZ.OnlineChat.Server.Controllers;

/// <summary>消息历史、搜索、会话与已读状态</summary>
[Route("api/[controller]")]
[Authorize]
public sealed class MessagesController : ApiControllerBase
{
    /// <summary>私聊历史（分页）</summary>
    [HttpGet("private/{friendId:int}")]
    public Task<IActionResult> GetPrivateHistory(
        int friendId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Send(new GetPrivateHistoryQuery
        {
            UserId = UserId,
            FriendId = friendId,
            Page = page,
            PageSize = pageSize
        }, ct);

    /// <summary>群聊历史（分页）</summary>
    [HttpGet("group/{groupId:long}")]
    public Task<IActionResult> GetGroupHistory(
        long groupId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Send(new GetGroupHistoryQuery
        {
            GroupId = groupId,
            UserId = UserId,
            Page = page,
            PageSize = pageSize
        }, ct);

    /// <summary>标记群消息已读（推进已读游标）</summary>
    [HttpPut("group/{groupId:long}/read")]
    public Task<IActionResult> MarkGroupAsRead(long groupId, CancellationToken ct)
        => Send(new MarkGroupReadCommand { GroupId = groupId, UserId = UserId }, ct);

    /// <summary>
    /// 搜索消息（scopeType/scopeId 可选，限定会话内搜索）。
    /// keyword 声明为可空：否则 [ApiController] 会把非空引用类型参数视为必填，
    /// 空关键词被模型校验拦在用例之外，返回 ValidationProblemDetails 而不是统一的 ApiResponse 形状。
    /// </summary>
    [HttpGet("search")]
    [EnableRateLimiting(RateLimitPolicies.Search)]
    public Task<IActionResult> SearchMessages(
        [FromQuery] string? keyword,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        [FromQuery] string? scopeType = null,
        [FromQuery] long? scopeId = null,
        CancellationToken ct = default)
        => Send(new SearchMessagesQuery
        {
            UserId = UserId,
            Keyword = keyword ?? string.Empty,
            Page = page,
            PageSize = pageSize,
            ScopeType = scopeType,
            ScopeId = scopeId
        }, ct);

    /// <summary>会话列表（私聊 + 群聊聚合）</summary>
    [HttpGet("sessions")]
    public Task<IActionResult> GetSessions(CancellationToken ct)
        => Send(new GetChatSessionsQuery { UserId = UserId }, ct);

    /// <summary>更新会话设置（置顶 / 免打扰）</summary>
    [HttpPut("session-setting")]
    public Task<IActionResult> UpdateSessionSetting(
        [FromBody] UpdateSessionSettingCommand command, CancellationToken ct)
    {
        command.UserId = UserId;
        return Send(command, ct);
    }

    /// <summary>标记单条消息已读</summary>
    [HttpPut("{messageId:long}/read")]
    public Task<IActionResult> MarkAsRead(long messageId, CancellationToken ct)
        => Send(new MarkMessageReadCommand { MessageId = messageId, UserId = UserId }, ct);

    /// <summary>批量标记某人发来的消息已读</summary>
    [HttpPut("read-all/{senderId:int}")]
    public Task<IActionResult> MarkAllAsRead(int senderId, CancellationToken ct)
        => Send(new MarkConversationReadCommand { SenderId = senderId, UserId = UserId }, ct);

    /// <summary>未读消息数</summary>
    [HttpGet("unread-count")]
    public Task<IActionResult> GetUnreadCount(CancellationToken ct)
        => Send(new GetUnreadCountQuery { UserId = UserId }, ct);

    /// <summary>离线消息（上线时拉取）</summary>
    [HttpGet("offline")]
    public Task<IActionResult> GetOfflineMessages(CancellationToken ct)
        => Send(new GetOfflineMessagesQuery { UserId = UserId }, ct);
}

/// <summary>聊天图片上传</summary>
[Route("api/[controller]")]
[Authorize]
public sealed class UploadsController : ApiControllerBase
{
    /// <summary>上传聊天图片</summary>
    [HttpPost("image")]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(IFormFile? file, CancellationToken ct)
    {
        await using var upload = file.ToFileUpload();
        return await Send(new UploadChatImageCommand { File = upload.Value }, ct);
    }

    /// <summary>上传语音消息（webm/ogg/mp3/m4a/mp4/wav，≤10MB；时长由客户端限制 60 秒）</summary>
    [HttpPost("voice")]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> UploadVoice(IFormFile? file, CancellationToken ct)
    {
        await using var upload = file.ToFileUpload();
        return await Send(new UploadChatVoiceCommand { File = upload.Value }, ct);
    }

    /// <summary>上传聊天文件（文档/压缩包/代码等，≤50MB；落盘名由服务端生成）</summary>
    [HttpPost("file")]
    [EnableRateLimiting(RateLimitPolicies.FileUpload)]
    [RequestSizeLimit(52 * 1024 * 1024)]
    public async Task<IActionResult> UploadFile(IFormFile? file, CancellationToken ct)
    {
        await using var upload = file.ToFileUpload();
        return await Send(new UploadChatFileCommand
        {
            File = upload.Value,
            UploaderId = UserId
        }, ct);
    }

    /// <summary>按 URL 批量取文件元数据（原始文件名 + 大小；前端按屏取）</summary>
    [HttpPost("files/meta")]
    public Task<IActionResult> GetFileMeta([FromBody] FileMetaRequest body, CancellationToken ct)
        => Send(new GetFileMetaQuery { Urls = body.Urls }, ct);
}

// ==================== 请求体 ====================

public sealed class FileMetaRequest
{
    /// <summary>要查询的文件地址（≤50 个，非本站文件目录的会被忽略）</summary>
    public List<string> Urls { get; set; } = new();
}
