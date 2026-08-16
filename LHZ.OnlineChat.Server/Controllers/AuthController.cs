using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LHZ.OnlineChat.Server.Controllers;

/// <summary>
/// 用户认证控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// 发送邮箱验证码（6 位数字）
    /// </summary>
    [HttpPost("send-code")]
    public async Task<IActionResult> SendCode([FromBody] SendCodeRequest request)
    {
        var result = await _authService.SendCodeAsync(request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 用户注册
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 刷新 Token
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var result = await _authService.RefreshTokenAsync(request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// 获取当前用户信息（需要认证）
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized(ApiResponse.Fail("无效的 Token"));

        var fsql = HttpContext.RequestServices.GetRequiredService<IFreeSql>();
        var user = await fsql.Select<LHZ.OnlineChat.Server.Models.Entities.User>()
            .Where(u => u.Id == userId)
            .FirstAsync();

        if (user == null)
            return NotFound(ApiResponse.Fail("用户不存在"));

        return Ok(ApiResponse<UserInfo>.Ok(new UserInfo
        {
            Id = user.Id,
            Nickname = user.Nickname,
            Avatar = user.Avatar
        }));
    }
}
