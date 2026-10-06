namespace LHZ.OnlineChat.Server.Configuration;

/// <summary>
/// 限流策略名与分区规则。
///
/// 为什么需要它：登录已经有账号/IP 双维度限流（ILoginThrottle），但匿名发码接口
/// 和第三方机器人推送接口一个都没有 —— 前者可以对任意邮箱反复触发发信（换邮箱
/// 即可绕过「同邮箱 60 秒冷却」），后者是无鉴权成本的公网入口。
///
/// 这里刻意不注册全局兜底限流器：/ws 握手走的是同一条管道，全局限流会在
/// 客户端批量重连时误伤 WebSocket 升级请求。按接口点名更精确，也更容易解释。
/// </summary>
internal static class RateLimitPolicies
{
    /// <summary>匿名发码接口（注册 / 忘记密码 / 换绑邮箱共用同一入口）</summary>
    internal const string SendCode = "send-code";

    /// <summary>机器人主动推送（第三方调用，按令牌分区而非按 IP）</summary>
    internal const string RobotReply = "robot-reply";

    /// <summary>
    /// 调用方标识：优先真实客户端 IP。
    /// X-Forwarded-For 已在管道最前面解析（见 Program.cs 的 UseForwardedHeaders），
    /// 否则容器里所有请求的 RemoteIpAddress 都是 nginx 的地址，会被算进同一个分区。
    /// </summary>
    internal static string ClientKey(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
