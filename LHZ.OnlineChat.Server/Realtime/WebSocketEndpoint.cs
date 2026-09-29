using System.Globalization;
using System.Security.Claims;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Infrastructure.Realtime;
using LHZ.WebSocket.AspNetCore;

namespace LHZ.OnlineChat.Server.Realtime;

/// <summary>
/// WebSocket 端点：只做「握手前的鉴权」与「升级协议」，
/// 连接生命周期交给基础设施层的 ChatConnectionHandler。
///
/// 改造前这段逻辑约 95 行内联在 Program.cs 里，混着连接表操作与业务调用。
/// </summary>
internal static class WebSocketEndpoint
{
    /// <summary>无 sid 的旧版 Token 用一次性随机键，不参与会话有效性校验</summary>
    private const string LegacySessionPrefix = "legacy-";

    internal static void MapChatWebSocket(this WebApplication app)
    {
        app.UseWebSocket(async context =>
        {
            var logger = app.Services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("LHZ.OnlineChat.Server.Realtime.WebSocketEndpoint");

            var httpContext = app.Services.GetRequiredService<IHttpContextAccessor>().HttpContext;

            // ===== 1) 身份校验 =====
            if (httpContext?.User.Identity?.IsAuthenticated != true)
            {
                logger.LogWarning("WS 连接被拒：未认证");
                context.Dispose();
                return;
            }

            var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, CultureInfo.InvariantCulture, out var userId) || userId <= 0)
            {
                logger.LogWarning("WS 连接被拒：无法解析用户 ID");
                context.Dispose();
                return;
            }

            // ===== 2) 会话有效性（被踢下线的会话重连会被拒） =====
            var sessionId = httpContext.User.FindFirst("sid")?.Value
                            ?? $"{LegacySessionPrefix}{Guid.NewGuid():N}";

            if (!sessionId.StartsWith(LegacySessionPrefix, StringComparison.Ordinal))
            {
                var sessions = app.Services.GetRequiredService<ISessionStore>();
                if (!await sessions.IsSessionValidAsync(sessionId))
                {
                    logger.LogWarning("WS 连接被拒：会话已失效（可能被踢下线），用户 {UserId}", userId);
                    context.Dispose();
                    return;
                }
            }

            // ===== 3) 升级协议 =====
            var client = await context.HttpUpgradeAsync();
            if (client is null)
            {
                logger.LogWarning("WS 握手失败，用户 {UserId}", userId);
                return;
            }

            // ===== 4) 交给基础设施层接管 =====
            await app.Services
                .GetRequiredService<IChatConnectionHandler>()
                .HandleAsync(client, userId, sessionId);
        });
    }
}
