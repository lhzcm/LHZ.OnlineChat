using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LHZ.OnlineChat.Server;

/// <summary>
/// 管理后台鉴权过滤器：
/// - 校验 JWT 存在且携带 role=admin claim（普通用户 JWT 无此 claim，天然被拒）
/// - SuperOnly=true 时额外校验 arole=0（超级管理员）
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AdminAuthorizeAttribute : Attribute, IAuthorizationFilter
{
    /// <summary>是否仅超级管理员可访问</summary>
    public bool SuperOnly { get; set; }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        // 注意：JWT 短名 "role" 会被 JwtSecurityTokenHandler 默认映射为 ClaimTypes.Role（长 URI）
        var isAdmin = user.Identity?.IsAuthenticated == true
            && (user.FindFirst("role")?.Value == "admin" || user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "admin");
        if (!isAdmin)
        {
            context.Result = new JsonResult(new { success = false, message = "未登录或无权访问管理接口" })
            {
                StatusCode = 401
            };
            return;
        }

        if (SuperOnly && user.FindFirst("arole")?.Value != "0")
        {
            context.Result = new JsonResult(new { success = false, message = "需要超级管理员权限" })
            {
                StatusCode = 403
            };
            return;
        }
    }
}

/// <summary>
/// 管理控制器基类：读取当前管理员 ID
/// </summary>
public abstract class AdminControllerBase : ControllerBase
{
    protected int AdminId
    {
        get
        {
            var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(claim, out var id) ? id : 0;
        }
    }

    protected string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}
