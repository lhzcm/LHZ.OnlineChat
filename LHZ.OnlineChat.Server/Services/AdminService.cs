using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LHZ.FastJson;
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
    private readonly BotService _botService;

    public AdminService(IFreeSql fsql, RedisService redis, AppSettings appSettings,
        AuthService authService, WsConnectionManager wsConnectionManager, BotService botService)
    {
        _fsql = fsql;
        _redis = redis;
        _appSettings = appSettings;
        _authService = authService;
        _wsConnectionManager = wsConnectionManager;
        _botService = botService;
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

    // ==================== 群管理（P1） ====================

    public async Task<ApiResponse<PagedResult<AdminGroupDto>>> ListGroupsAsync(
        string? keyword, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _fsql.Select<Group_>();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(g => g.Name.Contains(kw));
        }

        var total = (int)await query.CountAsync();
        var groups = await query
            .OrderByDescending(g => g.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var ownerIds = groups.Select(g => g.OwnerId).Distinct().ToList();
        var owners = ownerIds.Count > 0
            ? await _fsql.Select<User>().Where(u => ownerIds.Contains(u.Id)).ToListAsync()
            : new List<User>();
        var ownerDict = owners.ToDictionary(u => u.Id);

        var memberCounts = await _fsql.Select<GroupMember>()
            .Where(m => groups.Select(g => g.Id).Contains(m.GroupId))
            .GroupBy(m => m.GroupId)
            .ToListAsync(g => new { GroupId = g.Key, Cnt = g.Count() });
        var memberDict = memberCounts.ToDictionary(x => x.GroupId, x => x.Cnt);

        var msgCounts = await _fsql.Select<GroupMessage>()
            .Where(m => groups.Select(g => g.Id).Contains(m.GroupId))
            .GroupBy(m => m.GroupId)
            .ToListAsync(g => new { GroupId = g.Key, Cnt = g.Count() });
        var msgDict = msgCounts.ToDictionary(x => x.GroupId, x => x.Cnt);

        var items = groups.Select(g => new AdminGroupDto
        {
            Id = g.Id,
            Name = g.Name,
            Avatar = g.Avatar,
            OwnerId = g.OwnerId,
            OwnerName = ownerDict.GetValueOrDefault(g.OwnerId)?.Nickname ?? $"用户{g.OwnerId}",
            MemberCount = memberDict.GetValueOrDefault(g.Id),
            MessageCount = msgDict.GetValueOrDefault(g.Id),
            Announcement = g.Announcement,
            CreatedAt = g.CreatedAt
        }).ToList();

        return ApiResponse<PagedResult<AdminGroupDto>>.Ok(new PagedResult<AdminGroupDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ApiResponse<AdminGroupDetailDto>> GetGroupDetailAsync(long groupId)
    {
        var group = await _fsql.Select<Group_>().Where(g => g.Id == groupId).FirstAsync();
        if (group == null)
            return ApiResponse<AdminGroupDetailDto>.Fail("群不存在");

        var owner = await _fsql.Select<User>().Where(u => u.Id == group.OwnerId).FirstAsync();
        var members = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId)
            .OrderBy(m => m.Role)
            .ToListAsync();
        var userIds = members.Select(m => m.UserId).ToList();
        var users = userIds.Count > 0
            ? await _fsql.Select<User>().Where(u => userIds.Contains(u.Id)).ToListAsync()
            : new List<User>();
        var userDict = users.ToDictionary(u => u.Id);
        var msgCount = await _fsql.Select<GroupMessage>().Where(m => m.GroupId == groupId).CountAsync();

        var dto = new AdminGroupDetailDto
        {
            Group = new AdminGroupDto
            {
                Id = group.Id,
                Name = group.Name,
                Avatar = group.Avatar,
                OwnerId = group.OwnerId,
                OwnerName = owner?.Nickname ?? $"用户{group.OwnerId}",
                MemberCount = members.Count,
                MessageCount = msgCount,
                Announcement = group.Announcement,
                CreatedAt = group.CreatedAt
            },
            Members = members.Select(m => new AdminGroupMemberDto
            {
                UserId = m.UserId,
                Nickname = userDict.GetValueOrDefault(m.UserId)?.Nickname ?? $"用户{m.UserId}",
                Avatar = userDict.GetValueOrDefault(m.UserId)?.Avatar,
                Role = m.Role,
                IsOnline = _wsConnectionManager.IsOnline(m.UserId),
                IsBot = userDict.GetValueOrDefault(m.UserId)?.IsBot ?? false,
                MutedUntil = m.MutedUntil
            }).ToList()
        };
        return ApiResponse<AdminGroupDetailDto>.Ok(dto);
    }

    /// <summary>解散群（强制）：删除群/成员/消息，通知在线成员</summary>
    public async Task<ApiResponse> DissolveGroupAsync(int adminId, long groupId)
    {
        var group = await _fsql.Select<Group_>().Where(g => g.Id == groupId).FirstAsync();
        if (group == null)
            return ApiResponse.Fail("群不存在");

        var members = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId)
            .ToListAsync();

        await _fsql.Delete<GroupMessage>().Where(m => m.GroupId == groupId).ExecuteAffrowsAsync();
        await _fsql.Delete<GroupMember>().Where(m => m.GroupId == groupId).ExecuteAffrowsAsync();
        await _fsql.Delete<Group_>().Where(g => g.Id == groupId).ExecuteAffrowsAsync();
        await _fsql.Delete<SessionSetting>().Where(s => s.SessionType == "group" && s.SessionId == groupId).ExecuteAffrowsAsync();

        // 通知在线成员：群已解散
        var notify = new Models.DTOs.WsMessage
        {
            Type = Models.DTOs.WsMessageType.GroupDissolved,
            From = groupId.ToString(),
            To = groupId.ToString(),
            Content = $"群「{group.Name}」已被解散",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageId = string.Empty,
            MessageType = 0,
            SenderName = string.Empty,
            SenderAvatar = null
        };
        var json = FastJson.JsonConvert.Serialize(notify);
        foreach (var m in members)
        {
            foreach (var client in _wsConnectionManager.GetConnections(m.UserId))
            {
                client.SendMessage(json);
            }
        }

        await LogAsync(adminId, "group.dissolve", "group", groupId.ToString(), $"解散群「{group.Name}」（{members.Count} 人）");
        return ApiResponse.Ok($"群已解散（{members.Count} 名成员）");
    }

    /// <summary>移除群成员（强制）</summary>
    public async Task<ApiResponse> RemoveGroupMemberAsync(int adminId, long groupId, int userId)
    {
        var group = await _fsql.Select<Group_>().Where(g => g.Id == groupId).FirstAsync();
        if (group == null)
            return ApiResponse.Fail("群不存在");
        if (group.OwnerId == userId)
            return ApiResponse.Fail("不能移除群主，请先转让群主");

        var member = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == userId)
            .FirstAsync();
        if (member == null)
            return ApiResponse.Fail("该用户不在群中");

        await _fsql.Delete<GroupMember>().Where(m => m.Id == member.Id).ExecuteAffrowsAsync();

        var user = await _fsql.Select<User>().Where(u => u.Id == userId).FirstAsync();
        await LogAsync(adminId, "group.remove_member", "group", groupId.ToString(),
            $"从群「{group.Name}」移除成员 {user?.Nickname ?? $"用户{userId}"}");
        return ApiResponse.Ok("已移除该成员");
    }

    /// <summary>禁言/解除禁言群成员</summary>
    public async Task<ApiResponse> MuteGroupMemberAsync(int adminId, long groupId, int userId, AdminMuteRequest request)
    {
        var group = await _fsql.Select<Group_>().Where(g => g.Id == groupId).FirstAsync();
        if (group == null)
            return ApiResponse.Fail("群不存在");
        if (group.OwnerId == userId)
            return ApiResponse.Fail("不能禁言群主");

        var member = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == userId)
            .FirstAsync();
        if (member == null)
            return ApiResponse.Fail("该用户不在群中");

        var mutedUntil = request.MutedUntil.HasValue && request.MutedUntil.Value > DateTime.UtcNow
            ? request.MutedUntil.Value
            : (DateTime?)null;
        await _fsql.Update<GroupMember>()
            .Set(m => m.MutedUntil, mutedUntil)
            .Where(m => m.Id == member.Id)
            .ExecuteAffrowsAsync();

        var user = await _fsql.Select<User>().Where(u => u.Id == userId).FirstAsync();
        var detail = mutedUntil.HasValue
            ? $"禁言 {user?.Nickname ?? $"用户{userId}"} 至 {mutedUntil.Value:yyyy-MM-dd HH:mm}（UTC）"
            : $"解除 {user?.Nickname ?? $"用户{userId}"} 的禁言";
        await LogAsync(adminId, "group.mute", "group", groupId.ToString(), $"群「{group.Name}」{detail}");
        return ApiResponse.Ok(mutedUntil.HasValue ? "已禁言" : "已解除禁言");
    }

    /// <summary>转让群主（原群主降为成员）</summary>
    public async Task<ApiResponse> TransferGroupOwnerAsync(int adminId, long groupId, AdminTransferOwnerRequest request)
    {
        var group = await _fsql.Select<Group_>().Where(g => g.Id == groupId).FirstAsync();
        if (group == null)
            return ApiResponse.Fail("群不存在");

        var newOwner = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == request.NewOwnerId)
            .FirstAsync();
        if (newOwner == null)
            return ApiResponse.Fail("新群主必须是群成员");

        await _fsql.Update<Group_>().Set(g => g.OwnerId, request.NewOwnerId).Where(g => g.Id == groupId).ExecuteAffrowsAsync();
        await _fsql.Update<GroupMember>()
            .Set(m => m.Role, 0)
            .Where(m => m.Id == newOwner.Id)
            .ExecuteAffrowsAsync();
        await _fsql.Update<GroupMember>()
            .Set(m => m.Role, 2)
            .Where(m => m.GroupId == groupId && m.UserId == group.OwnerId)
            .ExecuteAffrowsAsync();

        await LogAsync(adminId, "group.transfer", "group", groupId.ToString(),
            $"群「{group.Name}」群主转让为 #{request.NewOwnerId}");
        return ApiResponse.Ok("群主已转让");
    }

    // ==================== 消息管理（P1） ====================

    public async Task<ApiResponse<PagedResult<AdminMessageDto>>> SearchMessagesAdminAsync(
        string? keyword, int? userId, long? groupId, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var result = new List<AdminMessageDto>();
        var total = 0;

        // 群聊（groupId 指定时只查该群）
        var gmQuery = _fsql.Select<GroupMessage>();
        if (groupId.HasValue) gmQuery = gmQuery.Where(m => m.GroupId == groupId.Value);
        if (userId.HasValue) gmQuery = gmQuery.Where(m => m.SenderId == userId.Value);
        if (!string.IsNullOrWhiteSpace(keyword)) gmQuery = gmQuery.Where(m => m.Content.Contains(keyword.Trim()));

        // 私聊（userId 指定时只查该用户相关；否则查全部）
        var pmQuery = _fsql.Select<PrivateMessage>();
        if (userId.HasValue) pmQuery = pmQuery.Where(m => m.SenderId == userId.Value || m.ReceiverId == userId.Value);
        if (!string.IsNullOrWhiteSpace(keyword)) pmQuery = pmQuery.Where(m => m.Content.Contains(keyword.Trim()));

        var groupTotal = (int)await gmQuery.CountAsync();
        var pmTotal = (int)await pmQuery.CountAsync();
        total = groupTotal + pmTotal;

        // 合并取最近（两边各取 take 条，合并排序后统一分页裁剪）
        var take = page * pageSize;
        var gmList = groupTotal > 0
            ? await gmQuery.OrderByDescending(m => m.SentAt).Take(take).ToListAsync()
            : new List<GroupMessage>();
        var pmList = pmTotal > 0
            ? await pmQuery.OrderByDescending(m => m.SentAt).Take(take).ToListAsync()
            : new List<PrivateMessage>();

        var senderIds = gmList.Select(m => m.SenderId)
            .Concat(pmList.Select(m => m.SenderId))
            .Concat(pmList.Select(m => m.ReceiverId))
            .Distinct().ToList();
        var users = senderIds.Count > 0
            ? await _fsql.Select<User>().Where(u => senderIds.Contains(u.Id)).ToListAsync()
            : new List<User>();
        var userDict = users.ToDictionary(u => u.Id);

        var merged = new List<(DateTime SentAt, AdminMessageDto Dto)>();
        foreach (var m in gmList)
        {
            merged.Add((m.SentAt, new AdminMessageDto
            {
                Id = m.Id,
                MessageId = m.ClientMessageId ?? m.Id.ToString(),
                Type = "group",
                SenderId = m.SenderId,
                SenderName = userDict.GetValueOrDefault(m.SenderId)?.Nickname ?? $"用户{m.SenderId}",
                SenderAvatar = userDict.GetValueOrDefault(m.SenderId)?.Avatar,
                Content = m.Content,
                MessageType = m.MessageType,
                SessionId = m.GroupId,
                IsDeleted = m.IsDeleted,
                SentAt = m.SentAt
            }));
        }
        foreach (var m in pmList)
        {
            var peer = m.SenderId == (userId ?? -1) ? m.ReceiverId : m.SenderId;
            merged.Add((m.SentAt, new AdminMessageDto
            {
                Id = m.Id,
                MessageId = m.ClientMessageId ?? m.Id.ToString(),
                Type = "private",
                SenderId = m.SenderId,
                SenderName = userDict.GetValueOrDefault(m.SenderId)?.Nickname ?? $"用户{m.SenderId}",
                SenderAvatar = userDict.GetValueOrDefault(m.SenderId)?.Avatar,
                Content = m.Content,
                MessageType = m.MessageType,
                SessionId = peer,
                IsDeleted = m.IsDeleted,
                SentAt = m.SentAt
            }));
        }

        var sorted = merged.OrderByDescending(x => x.SentAt).ToList();
        var items = sorted.Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Dto).ToList();

        return ApiResponse<PagedResult<AdminMessageDto>>.Ok(new PagedResult<AdminMessageDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>强制删除消息（标记 IsDeleted + 广播撤回通知，历史/搜索即隐藏）</summary>
    public async Task<ApiResponse> DeleteMessageAdminAsync(int adminId, string type, long messageId)
    {
        if (type == "private")
        {
            var msg = await _fsql.Select<PrivateMessage>().Where(m => m.Id == messageId).FirstAsync();
            if (msg == null)
                return ApiResponse.Fail("消息不存在");
            await _fsql.Update<PrivateMessage>()
                .Set(m => m.IsDeleted, true)
                .Where(m => m.Id == messageId)
                .ExecuteAffrowsAsync();

            // 通知双方刷新（复用撤回协议）
            BroadcastRecallAsync(messageId.ToString(), msg.SenderId, msg.ReceiverId, null);
            await LogAsync(adminId, "message.delete", "message", messageId.ToString(),
                $"删除私聊消息 #{messageId}（发送者 #{msg.SenderId}）");
        }
        else if (type == "group")
        {
            var msg = await _fsql.Select<GroupMessage>().Where(m => m.Id == messageId).FirstAsync();
            if (msg == null)
                return ApiResponse.Fail("消息不存在");
            await _fsql.Update<GroupMessage>()
                .Set(m => m.IsDeleted, true)
                .Where(m => m.Id == messageId)
                .ExecuteAffrowsAsync();

            BroadcastRecallAsync(messageId.ToString(), msg.SenderId, null, msg.GroupId);
            await LogAsync(adminId, "message.delete", "message", messageId.ToString(),
                $"删除群消息 #{messageId}（群 {msg.GroupId}）");
        }
        else
        {
            return ApiResponse.Fail("无效的消息类型");
        }
        return ApiResponse.Ok("消息已删除");
    }

    /// <summary>广播撤回通知（私聊双方 / 群内全部在线成员）</summary>
    private void BroadcastRecallAsync(string targetId, int senderId, int? peerId, long? groupId)
    {
        var notify = new Models.DTOs.WsMessage
        {
            Type = Models.DTOs.WsMessageType.MessageRecalled,
            From = senderId.ToString(),
            To = groupId?.ToString() ?? peerId?.ToString() ?? string.Empty,
            Content = targetId,
            MessageId = targetId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageType = 0,
            SenderName = string.Empty,
            SenderAvatar = null
        };
        var json = FastJson.JsonConvert.Serialize(notify);

        if (groupId.HasValue)
        {
            var members = _fsql.Select<GroupMember>().Where(m => m.GroupId == groupId.Value).ToList();
            foreach (var m in members)
            {
                foreach (var client in _wsConnectionManager.GetConnections(m.UserId))
                {
                    client.SendMessage(json);
                }
            }
        }
        else
        {
            foreach (var uid in new[] { senderId, peerId ?? 0 }.Where(i => i > 0))
            {
                foreach (var client in _wsConnectionManager.GetConnections(uid))
                {
                    client.SendMessage(json);
                }
            }
        }
    }

    // ==================== 机器人管理（P1） ====================

    public async Task<ApiResponse<PagedResult<AdminRobotDto>>> ListRobotsAsync(
        string? keyword, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _fsql.Select<RobotProfile>();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            if (int.TryParse(kw, out var id))
                query = query.Where(p => p.Id == id || p.UserId == id || p.Name.Contains(kw));
            else
                query = query.Where(p => p.Name.Contains(kw));
        }

        var total = (int)await query.CountAsync();
        var robots = await query
            .OrderByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var ownerIds = robots.Select(r => r.OwnerId).Distinct().ToList();
        var owners = ownerIds.Count > 0
            ? await _fsql.Select<User>().Where(u => ownerIds.Contains(u.Id)).ToListAsync()
            : new List<User>();
        var ownerDict = owners.ToDictionary(u => u.Id);

        var items = robots.Select(r => new AdminRobotDto
        {
            Id = r.Id,
            UserId = r.UserId,
            Name = r.Name,
            OwnerId = r.OwnerId,
            OwnerName = ownerDict.GetValueOrDefault(r.OwnerId)?.Nickname ?? $"用户{r.OwnerId}",
            WebhookUrl = r.WebhookUrl,
            Enabled = r.Enabled,
            PushCount = r.PushCount,
            CallbackFailCount = r.CallbackFailCount,
            CreatedAt = r.CreatedAt
        }).ToList();

        return ApiResponse<PagedResult<AdminRobotDto>>.Ok(new PagedResult<AdminRobotDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ApiResponse> SetRobotEnabledAsync(int adminId, long robotId, bool enabled)
    {
        var profile = await _fsql.Select<RobotProfile>().Where(p => p.Id == robotId).FirstAsync();
        if (profile == null)
            return ApiResponse.Fail("机器人不存在");

        await _fsql.Update<RobotProfile>()
            .Set(p => p.Enabled, enabled)
            .Where(p => p.Id == robotId)
            .ExecuteAffrowsAsync();

        await LogAsync(adminId, "robot.set_enabled", "robot", robotId.ToString(),
            $"{(enabled ? "启用" : "停用")}机器人「{profile.Name}」");
        return ApiResponse.Ok(enabled ? "机器人已启用" : "机器人已停用");
    }

    public async Task<ApiResponse> DeleteRobotAsync(int adminId, long robotId)
    {
        var result = await _botService.DeleteRobotByAdminAsync(robotId);
        if (result.Success)
        {
            await LogAsync(adminId, "robot.delete", "robot", robotId.ToString(), "删除机器人");
        }
        return result;
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
