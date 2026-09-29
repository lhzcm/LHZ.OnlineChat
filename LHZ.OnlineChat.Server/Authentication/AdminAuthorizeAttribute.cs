using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LHZ.OnlineChat.Server.Authentication;

/// <summary>
/// 管理后台鉴权过滤器：
/// - 要求 JWT 已认证且携带 role=admin（普通用户令牌没有此 claim，天然被拒）
/// - SuperOnly=true 时额外要求 arole=0（超级管理员）
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdminAuthorizeAttribute : Attribute, IAuthorizationFilter
{
    /// <summary>超管专属角色值</summary>
    private const string SuperRoleValue = "0";

    /// <summary>是否仅超级管理员可访问</summary>
    public bool SuperOnly { get; set; }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var user = context.HttpContext.User;

        // JWT 短名 "role" 会被 JwtSecurityTokenHandler 映射为 ClaimTypes.Role（长 URI），两处都查
        var isAdmin = user.Identity?.IsAuthenticated == true
                      && (user.FindFirst("role")?.Value == HttpCurrentUser.AdminRoleClaimValue
                          || user.FindFirst(ClaimTypes.Role)?.Value == HttpCurrentUser.AdminRoleClaimValue);

        if (!isAdmin)
        {
            context.Result = new JsonResult(new { success = false, message = "未登录或无权访问管理接口" })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
            return;
        }

        if (SuperOnly && user.FindFirst("arole")?.Value != SuperRoleValue)
        {
            context.Result = new JsonResult(new { success = false, message = "需要超级管理员权限" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
