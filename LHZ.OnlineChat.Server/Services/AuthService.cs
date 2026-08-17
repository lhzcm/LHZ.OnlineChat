using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LHZ.OnlineChat.Server.Config;
using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;

namespace LHZ.OnlineChat.Server.Services;

/// <summary>
/// 用户认证服务
/// 账号 = User.Id（int 自增，起始 10000）
/// </summary>
public class AuthService
{
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly string[] AllowedAvatarExts = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

    private readonly IFreeSql _fsql;
    private readonly RedisService _redis;
    private readonly AppSettings _appSettings;
    private readonly EmailService _emailService;
    private readonly IWebHostEnvironment _env;

    public AuthService(IFreeSql fsql, RedisService redis, AppSettings appSettings, EmailService emailService, IWebHostEnvironment env)
    {
        _fsql = fsql;
        _redis = redis;
        _appSettings = appSettings;
        _emailService = emailService;
        _env = env;
    }

    /// <summary>
    /// 发送邮箱验证码（6 位数字，5 分钟有效，60 秒冷却）
    /// </summary>
    public async Task<ApiResponse<SendCodeResponse>> SendCodeAsync(SendCodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !EmailRegex.IsMatch(request.Email))
            return ApiResponse<SendCodeResponse>.Fail("邮箱格式不正确");

        var email = request.Email.Trim().ToLowerInvariant();

        // 冷却：已有未过期的验证码则拒绝重复发送
        var key = GetEmailCodeKey(email);
        if (await _redis.KeyExistsAsync(key))
            return ApiResponse<SendCodeResponse>.Fail("验证码已发送，请稍后再试");

        var code = Random.Shared.Next(100000, 1000000).ToString();
        await _redis.SetStringAsync(key, code, TimeSpan.FromMinutes(5));

        // 发送邮件；未配置 SMTP 时返回 DevCode 便于本地调试
        var sent = await _emailService.SendCodeAsync(email, code);

        return ApiResponse<SendCodeResponse>.Ok(new SendCodeResponse
        {
            DevCode = sent ? null : code,
            CooldownSeconds = 60
        }, "验证码已发送");
    }

    /// <summary>
    /// 用户注册（昵称 + 邮箱验证码 + 密码），成功自动分配账号 ID
    /// </summary>
    public async Task<ApiResponse<RegisterResponse>> RegisterAsync(RegisterRequest request)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        // ===== 参数校验 =====
        if (string.IsNullOrWhiteSpace(request.Nickname))
            return ApiResponse<RegisterResponse>.Fail("昵称不能为空");
        if (request.Nickname.Length > 50)
            return ApiResponse<RegisterResponse>.Fail("昵称长度不能超过 50 个字符");
        if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
            return ApiResponse<RegisterResponse>.Fail("邮箱格式不正确");
        if (request.Password.Length < 6)
            return ApiResponse<RegisterResponse>.Fail("密码长度不能少于 6 个字符");

        // ===== 验证码校验 =====
        var codeKey = GetEmailCodeKey(email);
        var storedCode = await _redis.GetStringAsync(codeKey);
        if (string.IsNullOrEmpty(storedCode) || storedCode != request.Code)
            return ApiResponse<RegisterResponse>.Fail("验证码错误或已过期");

        // ===== 邮箱唯一性 =====
        var exists = await _fsql.Select<User>().Where(u => u.Email == email).AnyAsync();
        if (exists)
            return ApiResponse<RegisterResponse>.Fail("该邮箱已注册");

        // ===== 创建用户（Id 自增分配，起始 10000）=====
        var user = new User
        {
            Email = email,
            Nickname = request.Nickname.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        // ExecuteIdentityAsync 返回自增主键并回填到实体
        user.Id = (int)await _fsql.Insert(user).ExecuteIdentityAsync();

        // 验证码一次性使用
        await _redis.DeleteKeyAsync(codeKey);

        return ApiResponse<RegisterResponse>.Ok(new RegisterResponse
        {
            AccountId = user.Id
        }, $"注册成功，你的账号是 {user.Id}，请牢记");
    }

    /// <summary>
    /// 用户登录（账号 ID + 密码）
    /// </summary>
    public async Task<ApiResponse<LoginResponse>> LoginAsync(LoginRequest request)
    {
        if (request.Account <= 0 || string.IsNullOrWhiteSpace(request.Password))
            return ApiResponse<LoginResponse>.Fail("请输入账号和密码");

        var user = await _fsql.Select<User>().Where(u => u.Id == request.Account).FirstAsync();
        if (user == null)
            return ApiResponse<LoginResponse>.Fail("账号或密码错误");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return ApiResponse<LoginResponse>.Fail("账号或密码错误");

        // 生成 Token
        var token = GenerateJwtToken(user);
        var refreshToken = GenerateRefreshToken();

        // 存储 RefreshToken 到 Redis（7天过期），并建立 token→userId 反查索引
        await StoreRefreshTokenAsync(user.Id, refreshToken);

        return ApiResponse<LoginResponse>.Ok(new LoginResponse
        {
            Token = token,
            RefreshToken = refreshToken,
            User = new UserInfo
            {
                Id = user.Id,
                Nickname = user.Nickname,
                Avatar = user.Avatar,
                Email = user.Email
            }
        });
    }

    /// <summary>
    /// 刷新 Token
    /// </summary>
    public async Task<ApiResponse<LoginResponse>> RefreshTokenAsync(RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return ApiResponse<LoginResponse>.Fail("RefreshToken 不能为空");

        // O(1) 反查：根据 token 哈希找到 userId
        var lookupKey = GetRefreshLookupKey(request.RefreshToken);
        var userIdStr = await _redis.GetStringAsync(lookupKey);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
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
                Nickname = user.Nickname,
                Avatar = user.Avatar,
                Email = user.Email
            }
        });
    }

    /// <summary>
    /// 存储 RefreshToken（用户维度 + token 哈希反查索引）
    /// </summary>
    private async Task StoreRefreshTokenAsync(int userId, string refreshToken)
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
    /// 邮箱验证码 Key
    /// </summary>
    private static string GetEmailCodeKey(string email) => $"email:code:{email}";

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
            new Claim(ClaimTypes.Name, user.Nickname),
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

    /// <summary>
    /// 修改昵称
    /// </summary>
    public async Task<ApiResponse> UpdateProfileAsync(int userId, UpdateProfileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Nickname))
            return ApiResponse.Fail("昵称不能为空");
        if (request.Nickname.Trim().Length > 50)
            return ApiResponse.Fail("昵称长度不能超过 50 个字符");

        await _fsql.Update<User>()
            .Set(u => u.Nickname, request.Nickname.Trim())
            .Where(u => u.Id == userId)
            .ExecuteAffrowsAsync();

        return ApiResponse.Ok("昵称修改成功");
    }

    /// <summary>
    /// 上传头像（保存到 uploads 目录，返回 /uploads/xxx 相对地址）
    /// </summary>
    public async Task<ApiResponse<AvatarResponse>> UploadAvatarAsync(int userId, IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return ApiResponse<AvatarResponse>.Fail("请选择图片文件");
        if (file.Length > 2 * 1024 * 1024)
            return ApiResponse<AvatarResponse>.Fail("图片大小不能超过 2MB");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedAvatarExts.Contains(ext))
            return ApiResponse<AvatarResponse>.Fail("仅支持 jpg / png / gif / webp 格式图片");

        var uploadsDir = Path.Combine(_env.ContentRootPath, "uploads");
        Directory.CreateDirectory(uploadsDir);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var savePath = Path.Combine(uploadsDir, fileName);
        await using (var stream = File.Create(savePath))
        {
            await file.CopyToAsync(stream);
        }

        var avatarUrl = $"/uploads/{fileName}";
        await _fsql.Update<User>()
            .Set(u => u.Avatar, avatarUrl)
            .Where(u => u.Id == userId)
            .ExecuteAffrowsAsync();

        return ApiResponse<AvatarResponse>.Ok(new AvatarResponse { Avatar = avatarUrl }, "头像修改成功");
    }

    /// <summary>
    /// 换绑邮箱（需新邮箱验证码，且新邮箱未被其他账号绑定）
    /// </summary>
    public async Task<ApiResponse> UpdateEmailAsync(int userId, UpdateEmailRequest request)
    {
        var newEmail = request.NewEmail?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newEmail) || !EmailRegex.IsMatch(newEmail))
            return ApiResponse.Fail("邮箱格式不正确");

        // 验证码校验（发到新邮箱）
        var codeKey = GetEmailCodeKey(newEmail);
        var storedCode = await _redis.GetStringAsync(codeKey);
        if (string.IsNullOrEmpty(storedCode) || storedCode != request.Code)
            return ApiResponse.Fail("验证码错误或已过期");

        // 唯一性：新邮箱不能被其他账号绑定
        var exists = await _fsql.Select<User>()
            .Where(u => u.Email == newEmail && u.Id != userId)
            .AnyAsync();
        if (exists)
            return ApiResponse.Fail("该邮箱已被其他账号绑定");

        await _fsql.Update<User>()
            .Set(u => u.Email, newEmail)
            .Where(u => u.Id == userId)
            .ExecuteAffrowsAsync();

        // 验证码一次性使用
        await _redis.DeleteKeyAsync(codeKey);

        return ApiResponse.Ok("邮箱修改成功");
    }
}
