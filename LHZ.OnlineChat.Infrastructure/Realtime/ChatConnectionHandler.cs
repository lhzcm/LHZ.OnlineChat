using LHZ.OnlineChat.Application.Messaging.Commands;
using LHZ.WebSocket.Core;
using LHZ.WebSocket.Interfaces;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LHZ.OnlineChat.Infrastructure.Realtime;

/// <summary>
/// 基础设施层对外的实时通信入口。
/// 表现层只负责「鉴权 + 协议升级」，拿到连接后整条生命周期
/// （事件绑定、连接登记、上线广播、离线补发、断开清理）都交给实现。
/// </summary>
public interface IChatConnectionHandler
{
    /// <summary>接管一条已完成握手的连接</summary>
    Task HandleAsync(IWebSocketClient client, int userId, string sessionId);
}

/// <summary>
/// 连接生命周期管理。
/// 类型本身是 internal（连接表不对外暴露），对外只通过 IChatConnectionHandler 使用。
/// </summary>
internal sealed class ChatConnectionHandler : IChatConnectionHandler
{
    private readonly WsConnectionManager _connections;
    private readonly WsInboundDispatcher _dispatcher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChatConnectionHandler> _logger;

    public ChatConnectionHandler(
        WsConnectionManager connections,
        WsInboundDispatcher dispatcher,
        IServiceScopeFactory scopeFactory,
        ILogger<ChatConnectionHandler> logger)
    {
        _connections = connections;
        _dispatcher = dispatcher;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task HandleAsync(IWebSocketClient client, int userId, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.OnMessageReceived += (IWebSocketClient sender, string message) =>
            _ = _dispatcher.DispatchAsync(sender, userId, message);

        // 收到关闭帧：主动关闭，清理统一交给 OnClientClose
        client.OnCloseReceived += (IWebSocketClient sender, CloseMessage msg) =>
        {
            _logger.LogInformation("WS 收到关闭帧，用户 {UserId}，代码 {Code}", userId, msg.CloseCode);
            sender.Close();
        };

        client.OnClientClose += (IWebSocketClient sender) =>
            _ = OnDisconnectedAsync(userId, sessionId, sender);

        await _connections.AddConnectionAsync(userId, sessionId, client).ConfigureAwait(false);

        // 上线广播与离线群消息补发都不阻塞握手返回
        _ = RunInScopeAsync(mediator =>
            mediator.Send(new BroadcastPresenceCommand { UserId = userId, Online = true }));
        _ = RunInScopeAsync(mediator =>
            mediator.Send(new SendGroupBacklogCommand { UserId = userId }));

        _logger.LogInformation("用户 {UserId} WebSocket 连接就绪", userId);
    }

    /// <summary>断开清理：移除连接；该用户已无任何连接时才广播离线</summary>
    private async Task OnDisconnectedAsync(int userId, string sessionId, IWebSocketClient client)
    {
        try
        {
            // 带身份校验：旧连接的关闭事件不会误删新连接
            if (!await _connections.RemoveConnectionAsync(userId, sessionId, client).ConfigureAwait(false))
                return;

            if (_connections.IsOnline(userId)) return;

            await RunInScopeAsync(mediator =>
                mediator.Send(new BroadcastPresenceCommand { UserId = userId, Online = false }))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WS 下线清理失败，用户 {UserId}", userId);
        }
    }

    /// <summary>
    /// 在独立 DI 作用域里执行用例。
    /// WS 连接是长生命周期的，握手时的请求作用域早已结束，不能复用。
    /// </summary>
    private async Task RunInScopeAsync(Func<ISender, Task> action)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<ISender>()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WS 连接生命周期用例执行失败");
        }
    }
}
