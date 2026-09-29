using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;

namespace LHZ.OnlineChat.Domain.Admins;

/// <summary>管理员角色（数值与既有库表一致）</summary>
public enum AdminRole
{
    /// <summary>超级管理员：可管理管理员、查审计日志</summary>
    Super = 0,

    /// <summary>运营管理员</summary>
    Operator = 1
}

/// <summary>管理员账号状态</summary>
public enum AdminStatus
{
    Disabled = 0,
    Enabled = 1
}

/// <summary>
/// 管理员聚合根。与用户体系完全隔离：User 是被管理对象，Admin 是管理者。
/// </summary>
public sealed class Admin : AggregateRoot<int>
{
    public const int MinUsernameLength = 2;
    public const int MaxUsernameLength = 50;

    private Admin() { }

    public string Username { get; private set; } = string.Empty;

    public PasswordHash PasswordHash { get; private set; } = default!;

    public AdminRole Role { get; private set; } = AdminRole.Operator;

    public AdminStatus Status { get; private set; } = AdminStatus.Enabled;

    public DateTime CreatedAt { get; private set; }

    public DateTime? LastLoginAt { get; private set; }

    public bool IsSuper => Role == AdminRole.Super;

    public static Admin Create(string username, PasswordHash passwordHash, AdminRole role, DateTime now)
        => new()
        {
            Username = NormalizeUsername(username),
            PasswordHash = passwordHash,
            Role = role,
            Status = AdminStatus.Enabled,
            CreatedAt = now
        };

    /// <summary>登录前校验账号状态（口令是否正确由应用层用 IPasswordHasher 判断）</summary>
    public void EnsureCanLogin()
    {
        DomainException.Ensure(Status == AdminStatus.Enabled, "该管理员账号已停用");
    }

    public void RecordLogin(DateTime now) => LastLoginAt = now;

    public void SetPassword(PasswordHash newHash) => PasswordHash = newHash;

    /// <summary>
    /// 变更角色/状态。不能停用或降级自己 —— 否则超管可能把自己锁在门外。
    /// </summary>
    public void ChangeRoleAndStatus(AdminRole? role, AdminStatus? status, int operatorId)
    {
        var demotingSelf = Id == operatorId
            && (status == AdminStatus.Disabled || (role.HasValue && role.Value != AdminRole.Super));
        DomainException.Ensure(!demotingSelf, "不能停用或降级自己");

        if (role.HasValue) Role = role.Value;
        if (status.HasValue) Status = status.Value;
    }

    /// <summary>删除前校验：不能删自己，也不能删超管</summary>
    public void EnsureDeletableBy(int operatorId)
    {
        DomainException.Ensure(Id != operatorId, "不能删除自己");
        DomainException.Ensure(!IsSuper, "不能删除超级管理员");
    }

    /// <summary>管理员账号名规则</summary>
    public static string NormalizeUsername(string? raw)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        DomainException.Ensure(
            trimmed.Length >= MinUsernameLength && trimmed.Length <= MaxUsernameLength,
            $"账号长度需为 {MinUsernameLength}-{MaxUsernameLength} 个字符");
        return trimmed;
    }
}

/// <summary>
/// 管理操作审计日志聚合根。
/// </summary>
public sealed class AdminAuditLog : AggregateRoot<long>
{
    private AdminAuditLog() { }

    public int AdminId { get; private set; }

    public string AdminName { get; private set; } = string.Empty;

    /// <summary>操作类型，如 user.ban / group.dissolve / robot.delete</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>目标类型：user / group / message / robot / admin</summary>
    public string TargetType { get; private set; } = string.Empty;

    public string? TargetId { get; private set; }

    public string? Detail { get; private set; }

    public string? Ip { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static AdminAuditLog Record(
        int adminId,
        string adminName,
        string action,
        string targetType,
        string? targetId,
        string? detail,
        string? ip,
        DateTime now)
        => new()
        {
            AdminId = adminId,
            AdminName = adminName,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Detail = detail,
            Ip = ip,
            CreatedAt = now
        };
}

/// <summary>审计动作与目标类型常量（避免字符串散落）</summary>
public static class AuditActions
{
    public const string TargetUser = "user";
    public const string TargetGroup = "group";
    public const string TargetMessage = "message";
    public const string TargetRobot = "robot";
    public const string TargetAdmin = "admin";

    public const string UserBan = "user.ban";
    public const string UserUnban = "user.unban";
    public const string UserKick = "user.kick";
    public const string UserResetPassword = "user.reset_password";

    public const string AdminCreate = "admin.create";
    public const string AdminUpdate = "admin.update";
    public const string AdminDelete = "admin.delete";

    public const string GroupDissolve = "group.dissolve";
    public const string GroupRemoveMember = "group.remove_member";
    public const string GroupMute = "group.mute";
    public const string GroupTransfer = "group.transfer";

    public const string MessageDelete = "message.delete";

    public const string RobotSetEnabled = "robot.set_enabled";
    public const string RobotDelete = "robot.delete";
}
