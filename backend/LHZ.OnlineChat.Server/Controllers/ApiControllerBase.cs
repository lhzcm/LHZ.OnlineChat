using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace LHZ.OnlineChat.Server.Controllers;

/// <summary>
/// 控制器基类。
///
/// 改造前每个控制器都重复三样东西：注入具体 Service、自己解析 JWT 取 userId、
/// 手写 `result.Success ? Ok(result) : BadRequest(result)`。
/// 现在这三样各只有一份，控制器方法真正变成「一行转发」。
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _mediator;
    private ICurrentUser? _currentUser;

    /// <summary>用例派发器</summary>
    protected ISender Mediator
        => _mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    /// <summary>当前调用者</summary>
    protected ICurrentUser CurrentUser
        => _currentUser ??= HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    /// <summary>当前登录用户账号 ID</summary>
    protected int UserId => CurrentUser.UserId;

    /// <summary>当前登录会话（设备）ID</summary>
    protected string SessionId => CurrentUser.SessionId;

    /// <summary>当前管理员 ID</summary>
    protected int AdminId => CurrentUser.AdminId;

    /// <summary>
    /// 派发用例并按既有约定返回：成功 200，业务失败 400（响应体形状与改造前完全一致）。
    /// </summary>
    protected async Task<IActionResult> Send<TResponse>(
        IRequest<TResponse> request, CancellationToken ct = default)
        where TResponse : IApiResponse
    {
        var response = await Mediator.Send(request, ct);
        return response.Success ? Ok(response) : BadRequest(response);
    }
}
