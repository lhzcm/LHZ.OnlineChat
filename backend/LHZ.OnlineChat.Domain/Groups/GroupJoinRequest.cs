using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Groups;

/// <summary>入群申请状态</summary>
public enum JoinRequestStatus
{
    /// <summary>待审批</summary>
    Pending = 0,

    /// <summary>已通过</summary>
    Approved = 1,

    /// <summary>已拒绝</summary>
    Rejected = 2
}

/// <summary>
/// 入群申请（凭邀请链接提交）。
///
/// 独立聚合而不是挂在 Group 上：申请是短期的、数量与成员数同阶的记录，
/// 塞进群聚合会让"改群名/改公告"这类操作把整批申请一起载入；
/// 跨两者的不变量（同一人同一群最多一条待审批）由数据库部分唯一索引兜底。
/// 事件由用例在落库后直接派发（与 InviteGroupMembersHandler 同一风格），聚合本身保持纯状态。
/// </summary>
public sealed class GroupJoinRequest : AggregateRoot<long>
{
    /// <summary>申请附言上限（够说明来意，又不至于变成第二个聊天窗口）</summary>
    public const int MaxMessageLength = 100;

    private GroupJoinRequest() { }

    public long GroupId { get; private set; }

    public int UserId { get; private set; }

    public JoinRequestStatus Status { get; private set; } = JoinRequestStatus.Pending;

    /// <summary>申请附言（可选）</summary>
    public string? Message { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? HandledAt { get; private set; }

    /// <summary>处理人（群主/管理员）</summary>
    public int? HandledBy { get; private set; }

    /// <summary>提交申请。附言超长直接截断（不因为一句备注把整条申请丢掉）</summary>
    public static GroupJoinRequest Submit(long groupId, int userId, string? message, DateTime now)
    {
        var normalized = (message ?? string.Empty).Trim();
        if (normalized.Length > MaxMessageLength) normalized = normalized[..MaxMessageLength];

        return new GroupJoinRequest
        {
            GroupId = groupId,
            UserId = userId,
            Status = JoinRequestStatus.Pending,
            Message = normalized.Length == 0 ? null : normalized,
            CreatedAt = now
        };
    }

    /// <summary>只有待审批的申请能被处理（重复点"通过"不该再写一次成员）</summary>
    public void EnsurePending()
        => DomainException.Ensure(Status == JoinRequestStatus.Pending, "该申请已处理过了");

    /// <summary>通过（调用方负责把用户写进群成员）</summary>
    public void Approve(int handledBy, DateTime now)
    {
        EnsurePending();
        Status = JoinRequestStatus.Approved;
        HandledBy = handledBy;
        HandledAt = now;
    }

    /// <summary>拒绝</summary>
    public void Reject(int handledBy, DateTime now)
    {
        EnsurePending();
        Status = JoinRequestStatus.Rejected;
        HandledBy = handledBy;
        HandledAt = now;
    }
}
