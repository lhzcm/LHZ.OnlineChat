using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LHZ.OnlineChat.Server.Config;
using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Models.Entities;
using Microsoft.IdentityModel.Tokens;

namespace LHZ.OnlineChat.Server.Services;

/// <summary>
/// 用户认证服务
/// </summary>
public class AuthService
{
    private readonly IFreeSql _fsql;
    private readonly RedisService _redis;
    private readonly AppSettings _appSettings;

    public AuthService(IFreeSql fsql, RedisService redis, AppSettings appSettings)
    {
        _fsql = fsql;
        _redis = redis;
        _appSettings = appSettings;
    }

    /// <summary>
    /// 用户注册
    /// </summary>
    public async Task<ApiResponse> RegisterAsync(RegisterRequest request)
    {
        // 校验参数
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return ApiResponse.Fail("用户名和密码不能为空");

        if (request.Username.Length < 3 || request.Username.Length > 50)
            return ApiResponse.Fail("用户名长度需在 3-50 个字符之间");

        if (request.Password.Length < 6)
            return ApiResponse.Fail("密码长度不能少于 6 个字符");

        // 检查用户名唯一性
        var exists = await _fsql.Select<User>().Where(u => u.Username == request.Username).AnyAsync();
        if (exists)
            return ApiResponse.Fail("用户名已存在");

        // 创建用户
        var user = new User
        {
            Username = request.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Nickname = string.IsNullOrWhiteSpace(request.Nickname) ? request.Username : request.Nickname,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _fsql.Insert(user).ExecuteAffrowsAsync();
        return ApiResponse.Ok("注册成功");
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    public async Task<ApiResponse<LoginResponse>> LoginAsync(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return ApiResponse<LoginResponse>.Fail("用户名和密码不能为空");

        var user = await _fsql.Select<User>().Where(u => u.Username == request.Username).FirstAsync();
        if (user == null)
            return ApiResponse<LoginResponse>.Fail("用户名或密码错误");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return ApiResponse<LoginResponse>.Fail("用户名或密码错误");

        // 生成 Token
        var token = GenerateJwtToken(user);
        var refreshToken = GenerateRefreshToken();

        // 存储 RefreshToken 到 Redis（7天过期），并建立 token→userId 反查索引
        await StoreRefreshTokenAsync(user.Id, refreshToken);

        var userInfo = new UserInfo
        {
            Id = user.Id,
            Username = user.Username,
            Nickname = user.Nickname,
            Avatar = user.Avatar
        };

        return ApiResponse<LoginResponse>.Ok(new LoginResponse
        {
            Token = token,
            RefreshToken = refreshToken,
            User = userInfo
        });
    }

    /// <summary>
    /// 刷新 Token
    /// </summary>
    public async Task<ApiResponse<LoginResponse>> RefreshTokenAsync(RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return ApiResponse<LoginResponse>.Fail("RefreshToken 不能为空");

        // O(1) 反查：根据 token 哈希找到 userId（替代原来的 KEYS 全量扫描）
        var lookupKey = GetRefreshLookupKey(request.RefreshToken);
        var userIdStr = await _redis.GetStringAsync(lookupKey);
        if (string.IsNullOrEmpty(userIdStr) || !long.TryParse(userIdStr, out var userId))
            return ApiResponse<LoginResponse>.Fail("RefreshToken 无效或已过期");

        // 二次校验：确保该 token 仍是该用户当前有效的刷新令牌
        var storedToken = await _redis.GetStringAsync($"token:refresh:{userId}");
        if (storedToken != request.RefreshToken)
            return ApiResponse<LoginResponse>.Fail("RefreshToken 无效或已过期");

        var user = await _fsql.Select<User>().Where(u => u.Id == userId).FirstAsync();
        if (user == null)
            return ApiResponse<LoginResponse>.Fail("用户不存在");

        // 生成新 Token，轮换 RefreshToken（删除旧反查索引）
        var token = GenerateJwtToken(user);
        var refreshToken = GenerateRefreshToken();

        await _redis.DeleteKeyAsync(lookupKey);
        await StoreRefreshTokenAsync(user.Id, refreshToken);

        return ApiResponse<LoginResponse>.Ok(new LoginResponse
        {
            Token = token,
            RefreshToken = refreshToken,
            User = new UserInfo
            {
                Id = user.Id,
                Username = user.Username,
                Nickname = user.Nickname,
                Avatar = user.Avatar
            }
        });
    }

    /// <summary>
    /// 存储 RefreshToken（用户维度 + token 哈希反查索引）
    /// </summary>
    private async Task StoreRefreshTokenAsync(long userId, string refreshToken)
    {
        await _redis.SetStringAsync($"token:refresh:{userId}", refreshToken, TimeSpan.FromDays(7));
        await _redis.SetStringAsync(GetRefreshLookupKey(refreshToken), userId.ToString(), TimeSpan.FromDays(7));
    }

    /// <summary>
    /// RefreshToken 反查索引 Key（SHA256 哈希）
    /// </summary>
    private static string GetRefreshLookupKey(string refreshToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken))).ToLowerInvariant();
        return $"token:refresh:lookup:{hash}";
    }

    /// <summary>
    /// 生成 JWT Token
    /// </summary>
    private string GenerateJwtToken(User user)
    {
        var secretBytes = Encoding.UTF8.GetBytes(_appSettings.Jwt.Secret);
        var key = new SymmetricSecurityKey(secretBytes);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim("nickname", user.Nickname)
        };

        var tokenDescriptor = new JwtSecurityToken(
            issuer: _appSettings.Jwt.Issuer,
            audience: _appSettings.Jwt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_appSettings.Jwt.ExpireMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
    }

    /// <summary>
    /// 生成 Refresh Token
    /// </summary>
    private static string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }
}
