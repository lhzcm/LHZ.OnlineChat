namespace LHZ.OnlineChat.Application.Abstractions;

/// <summary>
/// 管理操作审计。
/// 写日志失败不影响主流程（与改造前一致：LogAsync 内部吞掉异常）。
/// 管理员名与来源 IP 由实现自行补齐，用例只关心「谁、做了什么、对谁」。
/// </summary>
public interface IAuditLogger
{
    Task RecordAsync(
        int adminId,
        string action,
        string targetType,
        string? targetId,
        string? detail,
        CancellationToken ct = default);
}
