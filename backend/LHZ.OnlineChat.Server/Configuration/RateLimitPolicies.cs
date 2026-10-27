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

    /// <summary>
    /// 凭邀请链接申请/加入群组：按来源 IP 分区。
    /// 限流中间件在鉴权之前执行（拿不到用户身份），所以这里只能用 IP ——
    /// 但接口本身要求登录，脚本刷单的起点已经受限。
    /// </summary>
    internal const string GroupJoin = "group-join";

    /// <summary>机器人主动推送（第三方调用，按令牌分区而非按 IP）</summary>
    internal const string RobotReply = "robot-reply";

    /// <summary>匿名的注册 / 忘记密码（要先发码，所以阈值与 SendCode 同量级；按 IP 分区）</summary>
    internal const string AuthAnonymous = "auth-anonymous";

    /// <summary>刷新令牌（匿名可达，但真人会在同一出口下频繁触发，阈值比 AuthAnonymous 宽；按 IP 分区）</summary>
    internal const string TokenRefresh = "token-refresh";

    /// <summary>头像 / 聊天图片 / 语音上传：每次最多落盘 10MB，按登录用户分区</summary>
    internal const string Upload = "upload";

    /// <summary>消息搜索：走 LIKE + trgm 扫描，是登录后最贵的查询，按登录用户分区</summary>
    internal const string Search = "search";

    /// <summary>
    /// 调用方标识：优先真实客户端 IP。
    /// X-Forwarded-For 已在管道最前面解析（见 Program.cs 的 UseForwardedHeaders），
    /// 否则容器里所有请求的 RemoteIpAddress 都是 nginx 的地址，会被算进同一个分区。
    /// </summary>
    internal static string ClientKey(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// 登录用户标识，用于「按用户」分区的策略。
    /// 必须在 UseAuthentication 之后才能读到身份（见 Program.cs 的中间件顺序）；
    /// 读不到（未登录 / 令牌无效）时退回 IP，这类请求随后会被授权拦成 401。
    /// 身份只来自已验签的 JWT，不读请求体或请求头里的自称。
    /// </summary>
    internal static string UserKey(HttpContext context)
    {
        var id = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrEmpty(id) ? ClientKey(context) : $"user:{id}";
    }
}
