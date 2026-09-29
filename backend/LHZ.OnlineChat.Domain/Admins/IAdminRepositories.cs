using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Admins;

/// <summary>管理员仓储</summary>
public interface IAdminRepository
{
    Task<Admin?> FindByIdAsync(int adminId, CancellationToken ct = default);

    Task<Admin> GetRequiredAsync(int adminId, CancellationToken ct = default);

    Task<Admin?> FindByUsernameAsync(string username, CancellationToken ct = default);

    Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default);

    /// <summary>是否已存在任何管理员（启动时决定要不要创建初始超管）</summary>
    Task<bool> AnyAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Admin>> ListAllAsync(CancellationToken ct = default);

    /// <summary>插入并回填自增主键</summary>
    Task AddAsync(Admin admin, CancellationToken ct = default);

    Task UpdateAsync(Admin admin, CancellationToken ct = default);

    Task DeleteAsync(int adminId, CancellationToken ct = default);
}

/// <summary>审计日志仓储</summary>
public interface IAdminAuditLogRepository
{
    Task<(IReadOnlyList<AdminAuditLog> Items, int Total)> PageAsync(
        PageRequest page, string? action, CancellationToken ct = default);

    Task AddAsync(AdminAuditLog log, CancellationToken ct = default);
}
