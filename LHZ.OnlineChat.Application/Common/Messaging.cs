using MediatR;

namespace LHZ.OnlineChat.Application.Common;

/// <summary>
/// 写操作用例（Command）。返回 ApiResponse 系列，与既有 HTTP 契约保持一致。
/// 操作者身份作为普通属性显式携带（而非从 HttpContext 隐式读取），
/// 这样 HTTP 路径与 WebSocket 路径可以复用同一个用例，单测也不需要伪造请求上下文。
/// </summary>
public interface ICommand<out TResponse> : IRequest<TResponse>
{
}

/// <summary>读操作用例（Query）。与 Command 分开只为语义清晰</summary>
public interface IQuery<out TResponse> : IRequest<TResponse>
{
}

/// <summary>
/// 当前请求的调用者信息（由表现层从 JWT / 连接上下文提供）。
/// Controller 用它填充命令的操作者字段，业务代码不直接接触 HttpContext。
/// </summary>
public interface ICurrentUser
{
    /// <summary>当前用户账号 ID；未认证为 0</summary>
    int UserId { get; }

    /// <summary>当前登录会话 ID（JWT sid claim）；无则为空串</summary>
    string SessionId { get; }

    /// <summary>当前管理员 ID；非管理员请求为 0</summary>
    int AdminId { get; }

    /// <summary>请求来源 IP</summary>
    string? ClientIp { get; }
}
