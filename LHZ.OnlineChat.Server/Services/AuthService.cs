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
    private readonly WsConnectionManager _wsConnectionManager;

    public AuthService(IFreeSql fsql, RedisService redis, AppSettings appSettings, EmailService emailService,
        IWebHostEnvironment env, WsConnectionManager wsConnectionManager)
    {
        _fsql = fsql;
        _redis = redis;
        _appSettings = appSettings;
        _emailService = emailService;
        _env = env;
        _wsConnectionManager = wsConnectionManager;
    }

    /// <summary>
    /// 发送邮箱验证码（6 位数字，5 分钟有效，60 秒冷却）
    /// purpose: register（注册，默认）/ forgot（忘记密码，要求邮箱已注册）
    /// </summary>
    public async Task<ApiResponse<SendCodeResponse>> SendCodeAsync(SendCodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !EmailRegex.IsMatch(request.Email))
            return ApiResponse<SendCodeResponse>.Fail("邮箱格式不正确");

        var email = request.Email.Trim().ToLowerInvariant();

        // 忘记密码：邮箱必须已注册（避免向任意邮箱发码）
        if (request.Purpose == "forgot")
        {
            var exists = await _fsql.Select<User>().Where(u => u.Email == email).AnyAsync();
            if (!exists)
                return ApiResponse<SendCodeResponse>.Fail("该邮箱未注册");
        }

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
    /// 用户登录（账号 ID 或邮箱 + 密码），ip 由 Controller 从请求来源解析
    /// </summary>
    public async Task<ApiResponse<LoginResponse>> LoginAsync(LoginRequest request, string ip)
    {
        var account = request.Account?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(request.Password))
            return ApiResponse<LoginResponse>.Fail("请输入账号和密码");

        // 纯数字按账号 ID 查询，否则按邮箱查询（邮箱统一小写）
        User? user;
        if (int.TryParse(account, out var accountId))
        {
            user = await _fsql.Select<User>().Where(u => u.Id == accountId).FirstAsync();
        }
        else
        {
            var email = account.ToLowerInvariant();
            user = await _fsql.Select<User>().Where(u => u.Email == email).FirstAsync();
        }

        if (user == null)
            return ApiResponse<LoginResponse>.Fail("账号或密码错误");

        // 机器人账号禁止登录
        if (user.IsBot)
            return ApiResponse<LoginResponse>.Fail("机器人账号不能登录");

        // 封禁账号禁止登录（管理后台封禁）
        if (user.IsBanned)
            return ApiResponse<LoginResponse>.Fail(user.BanReason ?? "账号已被封禁，请联系管理员");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return ApiResponse<LoginResponse>.Fail("账号或密码错误");

        // 创建登录会话（多端登录：每台设备一个独立会话，可单独管理/踢下线）
        var sessionId = Guid.NewGuid().ToString("N");
        var deviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? "未知设备" : request.DeviceName.Trim();
        await CreateSessionAsync(user.Id, sessionId, deviceName, ip);

        // 生成 Token（携带 sid 会话标识，用于踢下线与 WS 会话关联）
        var token = GenerateJwtToken(user, sessionId);
        var refreshToken = GenerateRefreshToken();
        await StoreRefreshTokenAsync(user.Id, sessionId, refreshToken);

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
    /// 刷新 Token（保持会话不变，轮换 RefreshToken）
    /// </summary>
    public async Task<ApiResponse<LoginResponse>> RefreshTokenAsync(RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return ApiResponse<LoginResponse>.Fail("RefreshToken 不能为空");

        // O(1) 反查：根据 token 哈希找到 userId:sessionId
        var lookupKey = GetRefreshLookupKey(request.RefreshToken);
        var lookupValue = await _redis.GetStringAsync(lookupKey);
        if (string.IsNullOrEmpty(lookupValue) || !TryParseSessionLookup(lookupValue, out var userId, out var sessionId))
            return ApiResponse<LoginResponse>.Fail("RefreshToken 无效或已过期");

        // 二次校验：确保该 token 仍是该会话当前有效的刷新令牌
        var storedToken = await _redis.GetStringAsync($"token:refresh:{sessionId}");
        if (storedToken != request.RefreshToken)
            return ApiResponse<LoginResponse>.Fail("RefreshToken 无效或已过期");

        var user = await _fsql.Select<User>().Where(u => u.Id == userId).FirstAsync();
        if (user == null)
            return ApiResponse<LoginResponse>.Fail("用户不存在");

        // 生成新 Token，轮换 RefreshToken（会话 ID 不变，多端互不影响）
        var token = GenerateJwtToken(user, sessionId);
        var refreshToken = GenerateRefreshToken();

        await _redis.DeleteKeyAsync(lookupKey);
        await StoreRefreshTokenAsync(user.Id, sessionId, refreshToken);
        await TouchSessionAsync(sessionId);

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

    // ==================== 多端会话管理 ====================

    /// <summary>
    /// 创建登录会话：记录设备元数据 + 登记到用户会话集合（Redis，7 天）
    /// </summary>
    private async Task CreateSessionAsync(int userId, string sessionId, string deviceName, string ip)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _redis.SetJsonAsync(GetSessionMetaKey(sessionId), new
        {
            DeviceName = deviceName,
            Ip = ip,
            CreatedAt = now,
            LastActiveAt = now
        }, TimeSpan.FromDays(7));

        await _redis.SetAddAsync(GetUserSessionsKey(userId), sessionId);
    }

    /// <summary>
    /// 更新会话最后活跃时间（刷新 Token 时调用）
    /// </summary>
    private async Task TouchSessionAsync(string sessionId)
    {
        var meta = await _redis.GetJsonAsync<SessionMeta>(GetSessionMetaKey(sessionId));
        if (meta == null) return;
        meta.LastActiveAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _redis.SetJsonAsync(GetSessionMetaKey(sessionId), meta, TimeSpan.FromDays(7));
    }

    /// <summary>
    /// 获取当前用户的所有登录会话
    /// </summary>
    public async Task<ApiResponse<List<SessionInfoDto>>> GetSessionsAsync(int userId, string currentSessionId)
    {
        var sessionIds = await _redis.SetMembersAsync(GetUserSessionsKey(userId));
        var result = new List<SessionInfoDto>();

        foreach (var sid in sessionIds)
        {
            var meta = await _redis.GetJsonAsync<SessionMeta>(GetSessionMetaKey(sid));
            if (meta == null) continue; // 元数据过期，忽略
            result.Add(new SessionInfoDto
            {
                SessionId = sid,
                DeviceName = meta.DeviceName,
                Ip = meta.Ip,
                CreatedAt = meta.CreatedAt,
                LastActiveAt = meta.LastActiveAt,
                IsCurrent = sid == currentSessionId
            });
        }

        // 最后活跃时间倒序
        result = result.OrderByDescending(s => s.LastActiveAt).ToList();
        return ApiResponse<List<SessionInfoDto>>.Ok(result);
    }

    /// <summary>
    /// 踢下线指定会话（当前用户自己的其他设备）：删除令牌 + 关闭该会话的 WebSocket 连接
    /// </summary>
    public async Task<ApiResponse> KickSessionAsync(int userId, string sessionId)
    {
        var isMember = await _redis.SetContainsAsync(GetUserSessionsKey(userId), sessionId);
        if (!isMember)
            return ApiResponse.Fail("会话不存在");

        await RemoveSessionAsync(userId, sessionId);
        return ApiResponse.Ok("该设备已下线");
    }

    /// <summary>
    /// 退出当前用户的其他所有会话（保留当前设备）
    /// </summary>
    public async Task<ApiResponse> LogoutOtherSessionsAsync(int userId, string currentSessionId)
    {
        var sessionIds = await _redis.SetMembersAsync(GetUserSessionsKey(userId));
        var kicked = 0;
        foreach (var sid in sessionIds)
        {
            if (sid == currentSessionId) continue;
            await RemoveSessionAsync(userId, sid);
            kicked++;
        }
        return ApiResponse.Ok(kicked > 0 ? $"已退出 {kicked} 台设备" : "没有其他在线设备");
    }

    /// <summary>
    /// 使该账号所有会话失效（修改密码 / 忘记密码重置后调用），并关闭所有 WS 连接
    /// </summary>
    public async Task LogoutAllSessionsAsync(int userId)
    {
        var sessionIds = await _redis.SetMembersAsync(GetUserSessionsKey(userId));
        foreach (var sid in sessionIds)
        {
            await RemoveSessionAsync(userId, sid);
        }
    }

    /// <summary>
    /// 删除单个会话：刷新令牌、反查索引、元数据、会话集合，并通知 WS 断开（踢下线）
    /// </summary>
    private async Task RemoveSessionAsync(int userId, string sessionId)
    {
        // 刷新令牌与反查索引
        var refreshToken = await _redis.GetStringAsync($"token:refresh:{sessionId}");
        if (!string.IsNullOrEmpty(refreshToken))
            await _redis.DeleteKeyAsync(GetRefreshLookupKey(refreshToken));
        await _redis.DeleteKeyAsync($"token:refresh:{sessionId}");

        // 元数据与会话集合
        await _redis.DeleteKeyAsync(GetSessionMetaKey(sessionId));
        await _redis.SetRemoveAsync(GetUserSessionsKey(userId), sessionId);

        // 关闭该会话的 WebSocket 连接（通知 + 断开）
        _wsConnectionManager.CloseSession(sessionId);
    }

    /// <summary>
    /// 存储 RefreshToken（会话维度，7 天过期）+ token 哈希反查索引（userId:sessionId）
    /// </summary>
    private async Task StoreRefreshTokenAsync(int userId, string sessionId, string refreshToken)
    {
        await _redis.SetStringAsync($"token:refresh:{sessionId}", refreshToken, TimeSpan.FromDays(7));
        await _redis.SetStringAsync(GetRefreshLookupKey(refreshToken), $"{userId}:{sessionId}", TimeSpan.FromDays(7));
    }

    private static bool TryParseSessionLookup(string value, out int userId, out string sessionId)
    {
        userId = 0;
        sessionId = string.Empty;
        var idx = value.IndexOf(':');
        if (idx <= 0 || idx == value.Length - 1) return false;
        if (!int.TryParse(value[..idx], out userId)) return false;
        sessionId = value[(idx + 1)..];
        return sessionId.Length > 0;
    }

    private static string GetSessionMetaKey(string sessionId) => $"sess:meta:{sessionId}";
    private static string GetUserSessionsKey(int userId) => $"sess:{userId}";

    /// <summary>
    /// 会话元数据（Redis JSON）
    /// </summary>
    private class SessionMeta
    {
        public string DeviceName { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public long CreatedAt { get; set; }
        public long LastActiveAt { get; set; }
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
    /// 生成 JWT Token（携带 sid 会话标识：踢下线时按会话精确失效，WS 连接与会话关联）
    /// </summary>
    private string GenerateJwtToken(User user, string sessionId)
    {
        var secretBytes = Encoding.UTF8.GetBytes(_appSettings.Jwt.Secret);
        var key = new SymmetricSecurityKey(secretBytes);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Nickname),
            new Claim("nickname", user.Nickname),
            new Claim("sid", sessionId)
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
    /// 修改密码（验证原密码，改后所有登录会话失效并强制下线）
    /// </summary>
    public async Task<ApiResponse> ChangePasswordAsync(int userId, ChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return ApiResponse.Fail("新密码长度不能少于 6 个字符");

        var user = await _fsql.Select<User>().Where(u => u.Id == userId).FirstAsync();
        if (user == null)
            return ApiResponse.Fail("用户不存在");

        if (string.IsNullOrEmpty(request.OldPassword) || !BCrypt.Net.BCrypt.Verify(request.OldPassword, user.PasswordHash))
            return ApiResponse.Fail("原密码错误");

        await _fsql.Update<User>()
            .Set(u => u.PasswordHash, BCrypt.Net.BCrypt.HashPassword(request.NewPassword))
            .Where(u => u.Id == userId)
            .ExecuteAffrowsAsync();

        // 所有登录会话失效（JWT 本身到期自然失效，会话令牌立即删除）
        await LogoutAllSessionsAsync(userId);

        return ApiResponse.Ok("密码修改成功，其他设备已下线，请重新登录");
    }

    /// <summary>
    /// 忘记密码（邮箱验证码重置密码，成功后该账号所有登录会话失效）
    /// </summary>
    public async Task<ApiResponse> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
            return ApiResponse.Fail("邮箱格式不正确");
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return ApiResponse.Fail("新密码长度不能少于 6 个字符");

        var user = await _fsql.Select<User>().Where(u => u.Email == email).FirstAsync();
        if (user == null)
            return ApiResponse.Fail("该邮箱未注册");

        // 验证码校验
        var codeKey = GetEmailCodeKey(email);
        var storedCode = await _redis.GetStringAsync(codeKey);
        if (string.IsNullOrEmpty(storedCode) || storedCode != request.Code)
            return ApiResponse.Fail("验证码错误或已过期");

        // 重置密码
        await _fsql.Update<User>()
            .Set(u => u.PasswordHash, BCrypt.Net.BCrypt.HashPassword(request.NewPassword))
            .Where(u => u.Id == user.Id)
            .ExecuteAffrowsAsync();

        // 验证码一次性使用
        await _redis.DeleteKeyAsync(codeKey);

        // 所有登录会话失效（防止旧设备继续使用）
        await LogoutAllSessionsAsync(user.Id);

        return ApiResponse.Ok("密码重置成功，请使用新密码登录");
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
