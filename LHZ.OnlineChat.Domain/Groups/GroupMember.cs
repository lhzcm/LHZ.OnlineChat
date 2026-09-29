using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Groups;

/// <summary>
/// 群内角色（替代原来的魔法数 0/1/2；数值与既有库表一致，不改存量数据）。
/// 注意数值顺序有语义：值越小权限越高，排序与 "Role > Admin 即普通成员" 的判断都依赖它。
/// </summary>
public enum GroupRole
{
    Owner = 0,
    Admin = 1,
    Member = 2
}

/// <summary>
/// 群成员聚合根：角色、已读游标、禁言状态。
/// 群管理相关的权限规则全部收敛在这里 —— 原先 "operatorMember.Role > 1" 这类判断
/// 在 GroupService / BotService / AdminService 里重复了 7 处。
/// </summary>
public sealed class GroupMember : AggregateRoot<long>
{
    private GroupMember() { }

    public long GroupId { get; private set; }

    public int UserId { get; private set; }

    public GroupRole Role { get; private set; }

    /// <summary>已读游标：该成员最后已读的群消息 ID（0=从未同步过，跳过离线补发）</summary>
    public long LastReadMessageId { get; private set; }

    /// <summary>禁言截止时间（null=未禁言，到期自动解除）</summary>
    public DateTime? MutedUntil { get; private set; }

    public DateTime JoinedAt { get; private set; }

    // ==================== 工厂 ====================

    /// <summary>建群时把创建者登记为群主</summary>
    public static GroupMember CreateOwner(long groupId, int ownerId, DateTime now)
        => new()
        {
            GroupId = groupId,
            UserId = ownerId,
            Role = GroupRole.Owner,
            LastReadMessageId = 0,
            JoinedAt = now
        };

    /// <summary>
    /// 加入群组。已读游标初始化为当前群内最新消息 ID，
    /// 避免把入群前的历史消息当成离线消息补发（普通加入/邀请/拉机器人共用同一规则）。
    /// </summary>
    public static GroupMember Join(long groupId, int userId, long latestMessageId, DateTime now)
        => new()
        {
            GroupId = groupId,
            UserId = userId,
            Role = GroupRole.Member,
            LastReadMessageId = latestMessageId,
            JoinedAt = now
        };

    // ==================== 权限守卫 ====================

    /// <summary>是否群主</summary>
    public bool IsOwner => Role == GroupRole.Owner;

    /// <summary>是否具备管理权限（群主或管理员）</summary>
    public bool CanManage => Role is GroupRole.Owner or GroupRole.Admin;

    /// <summary>需要管理权限（群主/管理员）才能执行的操作</summary>
    public void EnsureCanManageGroup(string action)
    {
        DomainException.Ensure(CanManage, $"只有群主或管理员可以{action}");
    }

    /// <summary>仅群主可执行的操作</summary>
    public void EnsureIsOwner(string action)
    {
        DomainException.Ensure(IsOwner, $"只有群主可以{action}");
    }

    /// <summary>
    /// 踢人规则：不能踢群主；管理员不能踢其他管理员。
    /// </summary>
    public void EnsureCanRemove(GroupMember target)
    {
        EnsureCanManageGroup("踢人");
        DomainException.Ensure(!target.IsOwner, "不能踢出群主");
        DomainException.Ensure(!(Role == GroupRole.Admin && target.Role == GroupRole.Admin),
            "管理员不能踢出其他管理员");
    }

    /// <summary>群主不能直接退群，必须先转让或解散</summary>
    public void EnsureCanLeave()
    {
        DomainException.Ensure(!IsOwner, "群主不能直接退出，请先转让群主或解散群组");
    }

    // ==================== 禁言 ====================

    /// <summary>当前是否处于禁言中</summary>
    public bool IsMuted(DateTime now) => MutedUntil.HasValue && MutedUntil.Value > now;

    /// <summary>
    /// 设置禁言截止时间；传 null 或已过去的时间表示解除禁言。
    /// 返回是否处于禁言状态（供调用方生成提示语）。
    /// </summary>
    public bool SetMute(DateTime? mutedUntil, DateTime now)
    {
        DomainException.Ensure(!IsOwner, "不能禁言群主");
        MutedUntil = mutedUntil.HasValue && mutedUntil.Value > now ? mutedUntil : null;
        return MutedUntil.HasValue;
    }

    /// <summary>发言前的禁言校验（被禁言则抛出，携带截止时间）</summary>
    public void EnsureNotMuted(DateTime now, Func<DateTime, string> formatUntil)
    {
        if (!IsMuted(now)) return;
        throw new MemberMutedException(MutedUntil!.Value, formatUntil(MutedUntil.Value));
    }

    // ==================== 角色变更 ====================

    /// <summary>设为/取消管理员（群主身份不可通过此路径变更）</summary>
    public void ChangeAdminRole(bool isAdmin)
    {
        DomainException.Ensure(!IsOwner, "不能修改群主的身份");
        Role = isAdmin ? GroupRole.Admin : GroupRole.Member;
    }

    /// <summary>接任群主</summary>
    public void PromoteToOwner() => Role = GroupRole.Owner;

    /// <summary>卸任群主，降为普通成员</summary>
    public void DemoteFromOwner() => Role = GroupRole.Member;

    // ==================== 已读游标 ====================

    /// <summary>推进已读游标（只增不减）</summary>
    public void AdvanceReadCursor(long messageId)
    {
        if (messageId > LastReadMessageId) LastReadMessageId = messageId;
    }
}

/// <summary>
/// 成员被禁言导致发言被拒。
/// 单独成一类：WS 侧要回一个带截止时间的 muted 协议消息，而非普通的失败提示。
/// </summary>
public sealed class MemberMutedException : DomainException
{
    public MemberMutedException(DateTime mutedUntil, string message) : base(message)
    {
        MutedUntil = mutedUntil;
    }

    public DateTime MutedUntil { get; }
}
