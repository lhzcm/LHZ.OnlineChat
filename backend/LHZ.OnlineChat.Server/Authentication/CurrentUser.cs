using System.Globalization;
using System.Security.Claims;
using LHZ.OnlineChat.Application.Common;

namespace LHZ.OnlineChat.Server.Authentication;

/// <summary>
/// 从 HttpContext 读取调用者身份。
/// 这是 ICurrentUser 唯一的实现 —— 业务代码不再各自写 GetCurrentUserId()
/// （改造前 7 个控制器里各抄了一遍同样的 FindFirst(ClaimTypes.NameIdentifier) 解析）。
/// </summary>
internal sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public int UserId => ParseId(Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value);

    public string SessionId => Principal?.FindFirst("sid")?.Value ?? string.Empty;

    /// <summary>管理员 JWT 同样把管理员 ID 放在 NameIdentifier；非管理员请求返回 0</summary>
    public int AdminId => IsAdmin ? ParseId(Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value) : 0;

    public string? ClientIp => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// 是否管理员身份。
    /// JwtSecurityTokenHandler 会把 JWT 里的短名 "role" 映射成 ClaimTypes.Role（长 URI），
    /// 两处都要看 —— 改造前踩过这个坑（导致管理接口一律 401）。
    /// </summary>
    private bool IsAdmin
        => Principal?.FindFirst("role")?.Value == AdminRoleClaimValue
           || Principal?.FindFirst(ClaimTypes.Role)?.Value == AdminRoleClaimValue;

    internal const string AdminRoleClaimValue = "admin";

    private static int ParseId(string? raw)
        => int.TryParse(raw, CultureInfo.InvariantCulture, out var id) ? id : 0;
}
