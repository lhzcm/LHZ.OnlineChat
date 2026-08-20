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
/// 管理后台服务：管理员认证、用户/群/消息管理、仪表盘统计、审计日志。
/// 管理身份与用户体系完全隔离（Admin 表独立）；管理 JWT 携带 role=admin claim。
/// </summary>
public class AdminService
{
    private readonly IFreeSql _fsql;
    private readonly RedisService _redis;
    private readonly AppSettings _appSettings;
    private readonly AuthService _authService;
    private readonly WsConnectionManager _wsConnectionManager;

    public AdminService(IFreeSql fsql, RedisService redis, AppSettings appSettings,
        AuthService authService, WsConnectionManager wsConnectionManager)
    {
        _fsql = fsql;
        _redis = redis;
        _appSettings = appSettings;
        _authService = authService;
        _wsConnectionManager = wsConnectionManager;
    }

    // ==================== 管理员认证 ====================

    public async Task<ApiResponse<AdminLoginResponse>> LoginAsync(AdminLoginRequest request, string ip)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(request.Password))
            return ApiResponse<AdminLoginResponse>.Fail("请输入账号和密码");

        var admin = await _fsql.Select<Admin>().Where(a => a.Username == username).FirstAsync();
        if (admin == null || !BCrypt.Net.BCrypt.Verify(request.Password, admin.PasswordHash))
            return ApiResponse<AdminLoginResponse>.Fail("账号或密码错误");
        if (admin.Status != 1)
            return ApiResponse<AdminLoginResponse>.Fail("该管理员账号已停用");

        await _fsql.Update<Admin>()
            .Set(a => a.LastLoginAt, DateTime.UtcNow)
            .Where(a => a.Id == admin.Id)
            .ExecuteAffrowsAsync();

        var token = GenerateAdminJwt(admin);
        return ApiResponse<AdminLoginResponse>.Ok(new AdminLoginResponse
        {
            Token = token,
            Admin = ToInfo(admin)
        }, "登录成功");
    }

    public async Task<ApiResponse<AdminInfo>> GetMeAsync(int adminId)
    {
        var admin = await _fsql.Select<Admin>().Where(a => a.Id == adminId).FirstAsync();
        if (admin == null)
            return ApiResponse<AdminInfo>.Fail("管理员不存在");
        return ApiResponse<AdminInfo>.Ok(ToInfo(admin));
    }

    public async Task<ApiResponse> ChangePasswordAsync(int adminId, AdminChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return ApiResponse.Fail("新密码长度不能少于 6 个字符");

        var admin = await _fsql.Select<Admin>().Where(a => a.Id == adminId).FirstAsync();
        if (admin == null)
            return ApiResponse.Fail("管理员不存在");
        if (string.IsNullOrEmpty(request.OldPassword) || !BCrypt.Net.BCrypt.Verify(request.OldPassword, admin.PasswordHash))
            return ApiResponse.Fail("原密码错误");

        await _fsql.Update<Admin>()
            .Set(a => a.PasswordHash, BCrypt.Net.BCrypt.HashPassword(request.NewPassword))
            .Where(a => a.Id == adminId)
            .ExecuteAffrowsAsync();

        return ApiResponse.Ok("密码修改成功");
    }

    // ==================== 管理员管理（超管） ====================

    public async Task<ApiResponse<List<AdminInfo>>> ListAdminsAsync()
    {
        var list = await _fsql.Select<Admin>()
            .OrderBy(a => a.Id)
            .ToListAsync();
        return ApiResponse<List<AdminInfo>>.Ok(list.Select(ToInfo).ToList());
    }

    public async Task<ApiResponse> CreateAdminAsync(int operatorId, AdminCreateRequest request)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username) || username.Length < 2 || username.Length > 50)
            return ApiResponse.Fail("账号长度需为 2-50 个字符");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            return ApiResponse.Fail("密码长度不能少于 6 个字符");
        if (request.Role != 0 && request.Role != 1)
            return ApiResponse.Fail("无效的角色");

        var exists = await _fsql.Select<Admin>().Where(a => a.Username == username).AnyAsync();
        if (exists)
            return ApiResponse.Fail("该管理员账号已存在");

        var admin = new Admin
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = request.Role,
            Status = 1,
            CreatedAt = DateTime.UtcNow
        };
        admin.Id = (int)await _fsql.Insert(admin).ExecuteIdentityAsync();

        await LogAsync(operatorId, "admin.create", "admin", admin.Id.ToString(),
            $"创建管理员 {username}（角色 {(request.Role == 0 ? "超管" : "运营")}）");

        return ApiResponse.Ok("管理员已创建");
    }

    public async Task<ApiResponse> UpdateAdminAsync(int operatorId, int targetId, AdminUpdateRequest request)
    {
        var target = await _fsql.Select<Admin>().Where(a => a.Id == targetId).FirstAsync();
        if (target == null)
            return ApiResponse.Fail("管理员不存在");
        if (targetId == operatorId && (request.Status == 0 || request.Role.HasValue && request.Role.Value != 0))
            return ApiResponse.Fail("不能停用或降级自己");

        if (request.Role.HasValue)
        {
            if (request.Role.Value != 0 && request.Role.Value != 1)
                return ApiResponse.Fail("无效的角色");
            await _fsql.Update<Admin>().Set(a => a.Role, request.Role.Value).Where(a => a.Id == targetId).ExecuteAffrowsAsync();
        }
        if (request.Status.HasValue)
        {
            if (request.Status.Value != 0 && request.Status.Value != 1)
                return ApiResponse.Fail("无效的状态");
            await _fsql.Update<Admin>().Set(a => a.Status, request.Status.Value).Where(a => a.Id == targetId).ExecuteAffrowsAsync();
        }

        await LogAsync(operatorId, "admin.update", "admin", targetId.ToString(),
            $"更新管理员 {target.Username}（role={request.Role} status={request.Status}）");
        return ApiResponse.Ok("已更新");
    }

    public async Task<ApiResponse> DeleteAdminAsync(int operatorId, int targetId)
    {
        if (targetId == operatorId)
            return ApiResponse.Fail("不能删除自己");
        var target = await _fsql.Select<Admin>().Where(a => a.Id == targetId).FirstAsync();
        if (target == null)
            return ApiResponse.Fail("管理员不存在");
        if (target.Role == 0)
            return ApiResponse.Fail("不能删除超级管理员");

        await _fsql.Delete<Admin>().Where(a => a.Id == targetId).ExecuteAffrowsAsync();
        await LogAsync(operatorId, "admin.delete", "admin", targetId.ToString(), $"删除管理员 {target.Username}");
        return ApiResponse.Ok("已删除");
    }

    // ==================== 用户管理 ====================

    public async Task<ApiResponse<PagedResult<AdminUserDto>>> ListUsersAsync(
        string? keyword, int page = 1, int pageSize = 20, bool? isBot = null, bool? banned = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _fsql.Select<User>();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            if (int.TryParse(kw, out var id))
            {
                query = query.Where(u => u.Id == id || u.Nickname.Contains(kw) || (u.Email != null && u.Email.Contains(kw)));
            }
            else
            {
                query = query.Where(u => u.Nickname.Contains(kw) || (u.Email != null && u.Email.Contains(kw)));
            }
        }
        if (isBot.HasValue) query = query.Where(u => u.IsBot == isBot.Value);
        if (banned.HasValue) query = query.Where(u => u.IsBanned == banned.Value);

        var total = (int)await query.CountAsync();
        var users = await query
            .OrderByDescending(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = await BuildUserDtosAsync(users);
        return ApiResponse<PagedResult<AdminUserDto>>.Ok(new PagedResult<AdminUserDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ApiResponse<AdminUserDetailDto>> GetUserDetailAsync(int userId)
    {
        var user = await _fsql.Select<User>().Where(u => u.Id == userId).FirstAsync();
        if (user == null)
            return ApiResponse<AdminUserDetailDto>.Fail("用户不存在");

        var dto = (await BuildUserDtosAsync(new List<User> { user }))[0];

        // 登录设备列表（Redis 会话元数据）
        var sessions = await _authService.GetSessionsAsync(userId, string.Empty);
        var detail = new AdminUserDetailDto
        {
            User = dto,
            Sessions = sessions.Data ?? new List<SessionInfoDto>()
        };
        return ApiResponse<AdminUserDetailDto>.Ok(detail);
    }

    /// <summary>
    /// 封禁/解封用户：封禁后立即踢掉该用户所有设备（API 401 + WS 断开 + 登录被拒）
    /// </summary>
    public async Task<ApiResponse> BanUserAsync(int adminId, int userId, AdminBanRequest request)
    {
        var user = await _fsql.Select<User>().Where(u => u.Id == userId).FirstAsync();
        if (user == null)
            return ApiResponse.Fail("用户不存在");
        if (user.IsBot)
            return ApiResponse.Fail("机器人账号不支持封禁，请直接删除机器人");

        if (request.Banned)
        {
            await _fsql.Update<User>()
                .Set(u => u.IsBanned, true)
                .Set(u => u.BanReason, request.Reason)
                .Set(u => u.BannedAt, DateTime.UtcNow)
                .Where(u => u.Id == userId)
                .ExecuteAffrowsAsync();
            // 立即踢掉所有设备：Redis 会话全删 + WS 断开 → 正在进行的 API 全部 401
            await _authService.LogoutAllSessionsAsync(userId);
            await LogAsync(adminId, "user.ban", "user", userId.ToString(), $"封禁用户 {user.Nickname}（原因：{request.Reason ?? "未填写"}）");
            return ApiResponse.Ok("已封禁，该用户所有设备已下线");
        }
        else
        {
            await _fsql.Update<User>()
                .Set(u => u.IsBanned, false)
                .Set(u => u.BanReason, null)
                .Set(u => u.BannedAt, null)
                .Where(u => u.Id == userId)
                .ExecuteAffrowsAsync();
            await LogAsync(adminId, "user.unban", "user", userId.ToString(), $"解封用户 {user.Nickname}");
            return ApiResponse.Ok("已解封");
        }
    }

    /// <summary>强制下线：踢掉该用户所有设备（不动账号）</summary>
    public async Task<ApiResponse> KickUserAsync(int adminId, int userId)
    {
        var user = await _fsql.Select<User>().Where(u => u.Id == userId).FirstAsync();
        if (user == null)
            return ApiResponse.Fail("用户不存在");

        await _authService.LogoutAllSessionsAsync(userId);
        await LogAsync(adminId, "user.kick", "user", userId.ToString(), $"强制下线用户 {user.Nickname}");
        return ApiResponse.Ok("该用户所有设备已下线");
    }

    /// <summary>重置密码（免验证码）</summary>
    public async Task<ApiResponse> ResetUserPasswordAsync(int adminId, int userId, AdminResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return ApiResponse.Fail("新密码长度不能少于 6 个字符");

        var user = await _fsql.Select<User>().Where(u => u.Id == userId).FirstAsync();
        if (user == null)
            return ApiResponse.Fail("用户不存在");
        if (user.IsBot)
            return ApiResponse.Fail("机器人账号无密码");

        await _fsql.Update<User>()
            .Set(u => u.PasswordHash, BCrypt.Net.BCrypt.HashPassword(request.NewPassword))
            .Where(u => u.Id == userId)
            .ExecuteAffrowsAsync();
        // 重置后旧会话失效，要求重新登录
        await _authService.LogoutAllSessionsAsync(userId);

        await LogAsync(adminId, "user.reset_password", "user", userId.ToString(), $"重置用户 {user.Nickname} 的密码");
        return ApiResponse.Ok("密码已重置，该用户需重新登录");
    }

    // ==================== 仪表盘 ====================

    public async Task<ApiResponse<DashboardOverviewDto>> GetDashboardAsync()
    {
        var today = DateTime.UtcNow.Date;

        var dto = new DashboardOverviewDto
        {
            OnlineUsers = _wsConnectionManager.GetOnlineUserIds().Count(),
            WsConnections = _wsConnectionManager.ConnectionCount,
            TotalUsers = (int)await _fsql.Select<User>().CountAsync(),
            BannedUsers = (int)await _fsql.Select<User>().Where(u => u.IsBanned).CountAsync(),
            TotalGroups = (int)await _fsql.Select<Group_>().CountAsync(),
            TotalRobots = (int)await _fsql.Select<RobotProfile>().CountAsync(),
            TotalMessages = await _fsql.Select<PrivateMessage>().CountAsync() + await _fsql.Select<GroupMessage>().CountAsync(),
            TodayMessages = await _fsql.Select<PrivateMessage>().Where(m => m.SentAt >= today).CountAsync()
                + await _fsql.Select<GroupMessage>().Where(m => m.SentAt >= today).CountAsync(),
            TodayRegistrations = (int)await _fsql.Select<User>().Where(u => u.CreatedAt >= today).CountAsync()
        };

        // 近 7 日注册趋势
        for (var i = 6; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var next = day.AddDays(1);
            var cnt = (int)await _fsql.Select<User>().Where(u => u.CreatedAt >= day && u.CreatedAt < next).CountAsync();
            dto.RegisterTrend.Add(new TrendPointDto { Date = day.ToString("MM-dd"), Count = cnt });
        }
        // 近 7 日消息趋势
        for (var i = 6; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var next = day.AddDays(1);
            var cnt = await _fsql.Select<PrivateMessage>().Where(m => m.SentAt >= day && m.SentAt < next).CountAsync()
                + await _fsql.Select<GroupMessage>().Where(m => m.SentAt >= day && m.SentAt < next).CountAsync();
            dto.MessageTrend.Add(new TrendPointDto { Date = day.ToString("MM-dd"), Count = cnt });
        }

        return ApiResponse<DashboardOverviewDto>.Ok(dto);
    }

    // ==================== 审计日志 ====================

    public async Task<ApiResponse<PagedResult<AdminLogDto>>> ListLogsAsync(int page = 1, int pageSize = 20, string? action = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _fsql.Select<AdminLog>();
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(l => l.Action == action.Trim());

        var total = (int)await query.CountAsync();
        var logs = await query
            .OrderByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = logs.Select(l => new AdminLogDto
        {
            Id = l.Id,
            AdminName = l.AdminName,
            Action = l.Action,
            TargetType = l.TargetType,
            TargetId = l.TargetId,
            Detail = l.Detail,
            Ip = l.Ip,
            CreatedAt = l.CreatedAt
        }).ToList();

        return ApiResponse<PagedResult<AdminLogDto>>.Ok(new PagedResult<AdminLogDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    // ==================== 内部 ====================

    private async Task<List<AdminUserDto>> BuildUserDtosAsync(List<User> users)
    {
        if (users.Count == 0) return new List<AdminUserDto>();
        var userIds = users.Select(u => u.Id).ToList();

        var friendCounts = await _fsql.Select<Friend>()
            .Where(f => userIds.Contains(f.UserId) && f.Status == 1)
            .GroupBy(f => f.UserId)
            .ToListAsync(g => new { UserId = g.Key, Cnt = g.Count() });
        var friendDict = friendCounts.ToDictionary(x => x.UserId, x => x.Cnt);

        var groupCounts = await _fsql.Select<GroupMember>()
            .Where(m => userIds.Contains(m.UserId))
            .GroupBy(m => m.UserId)
            .ToListAsync(g => new { UserId = g.Key, Cnt = g.Count() });
        var groupDict = groupCounts.ToDictionary(x => x.UserId, x => x.Cnt);

        var pmCounts = await _fsql.Select<PrivateMessage>()
            .Where(m => userIds.Contains(m.SenderId))
            .GroupBy(m => m.SenderId)
            .ToListAsync(g => new { UserId = g.Key, Cnt = g.Count() });
        var gmCounts = await _fsql.Select<GroupMessage>()
            .Where(m => userIds.Contains(m.SenderId))
            .GroupBy(m => m.SenderId)
            .ToListAsync(g => new { UserId = g.Key, Cnt = g.Count() });
        var msgDict = new Dictionary<int, long>();
        foreach (var x in pmCounts) msgDict[x.UserId] = msgDict.GetValueOrDefault(x.UserId) + x.Cnt;
        foreach (var x in gmCounts) msgDict[x.UserId] = msgDict.GetValueOrDefault(x.UserId) + x.Cnt;

        return users.Select(u => new AdminUserDto
        {
            Id = u.Id,
            Nickname = u.Nickname,
            Email = u.Email,
            Avatar = u.Avatar,
            IsBot = u.IsBot,
            IsBanned = u.IsBanned,
            BanReason = u.BanReason,
            BannedAt = u.BannedAt,
            CreatedAt = u.CreatedAt,
            IsOnline = _wsConnectionManager.IsOnline(u.Id),
            FriendCount = friendDict.GetValueOrDefault(u.Id),
            GroupCount = groupDict.GetValueOrDefault(u.Id),
            MessageCount = msgDict.GetValueOrDefault(u.Id)
        }).ToList();
    }

    /// <summary>记录审计日志（操作本身失败不影响主流程）</summary>
    public async Task LogAsync(int adminId, string action, string targetType, string? targetId, string? detail, string? ip = null)
    {
        try
        {
            var adminName = "?";
            var admin = await _fsql.Select<Admin>().Where(a => a.Id == adminId).FirstAsync();
            if (admin != null) adminName = admin.Username;

            await _fsql.Insert(new AdminLog
            {
                AdminId = adminId,
                AdminName = adminName,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                Detail = detail,
                Ip = ip,
                CreatedAt = DateTime.UtcNow
            }).ExecuteAffrowsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ADMIN] 审计日志写入失败: {ex.Message}");
        }
    }

    /// <summary>生成管理员 JWT（role=admin + arole=角色 + aid=管理员ID；无 sid，不走用户会话校验）</summary>
    private string GenerateAdminJwt(Admin admin)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_appSettings.Jwt.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new Claim(ClaimTypes.Name, admin.Username),
            new Claim("role", "admin"),
            new Claim("arole", admin.Role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _appSettings.Jwt.Issuer,
            audience: _appSettings.Jwt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_appSettings.Jwt.ExpireMinutes),
            signingCredentials: credentials
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static AdminInfo ToInfo(Admin a) => new()
    {
        Id = a.Id,
        Username = a.Username,
        Role = a.Role,
        Status = a.Status,
        LastLoginAt = a.LastLoginAt
    };
}
