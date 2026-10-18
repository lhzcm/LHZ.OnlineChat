using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Groups;

/// <summary>
/// 群公告值对象：内容 + 最后编辑时间 + 编辑者，三者要么同时有值要么同时为空。
/// 原先是 Group_ 上三个各自可空的散列字段，清除公告时要记得同时清三处、少清一处就留下脏数据。
/// </summary>
public sealed class GroupAnnouncement : ValueObject
{
    public const int MaxLength = 2000;

    private GroupAnnouncement(string text, DateTime editedAt, int editedBy)
    {
        Text = text;
        EditedAt = editedAt;
        EditedBy = editedBy;
    }

    public string Text { get; }

    public DateTime EditedAt { get; }

    public int EditedBy { get; }

    /// <summary>创建公告；内容为空白则返回 null（表示清除公告）</summary>
    public static GroupAnnouncement? Create(string? text, int editedBy, DateTime now)
    {
        var normalized = (text ?? string.Empty).Trim();
        DomainException.Ensure(normalized.Length <= MaxLength, $"公告内容过长（最多 {MaxLength} 字）");
        return normalized.Length == 0 ? null : new GroupAnnouncement(normalized, now, editedBy);
    }

    /// <summary>从已持久化的三列还原（兼容历史数据：时间/编辑者可能缺失）</summary>
    public static GroupAnnouncement? Restore(string? text, DateTime? editedAt, int? editedBy)
        => string.IsNullOrWhiteSpace(text)
            ? null
            : new GroupAnnouncement(text, editedAt ?? default, editedBy ?? 0);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Text;
        yield return EditedAt;
        yield return EditedBy;
    }
}

/// <summary>
/// 入群方式。
///
/// 默认 InviteOnly 而不是 Open：群 ID 是自增主键、连续且可枚举，
/// 若默认可自行加入，任何人都能 join 任意群 —— 而消息历史与搜索的权限判断
/// 只校验「是不是成员」，于是加入就等于拿到该群全部历史消息和成员名单。
/// 需要公开招募的群，由群主/管理员显式改成 Open。
/// </summary>
public enum GroupJoinPolicy
{
    /// <summary>仅限邀请（默认）：只能由群主/管理员拉入，不能自行加入</summary>
    InviteOnly = 0,

    /// <summary>开放加入：知道群 ID 即可自行加入</summary>
    Open = 1
}

/// <summary>
/// 群组聚合根。
/// 成员是独立聚合（GroupMember）—— 大集合不塞进聚合根，按「小聚合 + 按标识引用」建模，
/// 否则每次发言都得把整群成员载入内存。跨两者的不变量由 GroupMember 的守卫方法表达。
/// </summary>
public sealed class Group : AggregateRoot<long>
{
    public const int MaxNameLength = 100;

    /// <summary>邀请链接 token 的长度下限（32 字节 base64url 编码后是 43 字符）</summary>
    public const int InviteTokenMinLength = 20;

    public const int InviteTokenMaxLength = 64;

    /// <summary>邀请链接有效期（天）。过期即失效，群主可随时重置或关闭</summary>
    public const int InviteLinkTtlDays = 7;

    private Group() { }

    public string Name { get; private set; } = string.Empty;

    public string? Avatar { get; private set; }

    public int OwnerId { get; private set; }

    /// <summary>入群方式；存量数据经 CodeFirst 补列后取默认值 0，即收紧为仅限邀请</summary>
    public GroupJoinPolicy JoinPolicy { get; private set; } = GroupJoinPolicy.InviteOnly;

    /// <summary>是否允许自行加入</summary>
    public bool IsOpenToJoin => JoinPolicy == GroupJoinPolicy.Open;

    /// <summary>
    /// 群邀请链接的 token（能力凭证）。null = 未开启。
    /// 只存 token 不存完整 URL：站点域名由客户端拼，服务端不知道用户从哪个域名访问。
    /// token 是"持有即可申请/加入"的凭证，因此不能写进日志。
    /// </summary>
    public string? InviteToken { get; private set; }

    /// <summary>邀请链接过期时间（UTC）；与 InviteToken 同进同退</summary>
    public DateTime? InviteTokenExpiresAt { get; private set; }

    /// <summary>
    /// 凭邀请链接入群是否需要管理员审批（默认需要）。
    /// 只作用于链接入口；「开放加入」（JoinPolicy.Open，知道群号即可进）的语义不受影响。
    /// </summary>
    public bool JoinApprovalRequired { get; private set; } = true;

    /// <summary>邀请链接当前是否可用</summary>
    public bool HasActiveInviteLink(DateTime now)
        => !string.IsNullOrEmpty(InviteToken) && InviteTokenExpiresAt is { } expires && expires > now;

    // ===== 公告的三个持久化列（映射到既有表结构 Announcement / AnnouncementAt / AnnouncementBy）=====
    // 对外只暴露聚合后的值对象，三列永远同进同退。

    public string? AnnouncementText { get; private set; }

    public DateTime? AnnouncementAt { get; private set; }

    public int? AnnouncementBy { get; private set; }

    /// <summary>公告值对象视图（只读计算属性不会被 ORM 映射成列）</summary>
    public GroupAnnouncement? Announcement
        => GroupAnnouncement.Restore(AnnouncementText, AnnouncementAt, AnnouncementBy);

    public DateTime CreatedAt { get; private set; }

    public static Group Create(string name, string? avatar, int ownerId, DateTime now)
    {
        var normalized = (name ?? string.Empty).Trim();
        DomainException.Ensure(normalized.Length > 0, "群组名称不能为空");
        DomainException.Ensure(normalized.Length <= MaxNameLength, $"群组名称不能超过 {MaxNameLength} 个字符");
        return new Group
        {
            Name = normalized,
            Avatar = avatar,
            OwnerId = ownerId,
            JoinPolicy = GroupJoinPolicy.InviteOnly,
            CreatedAt = now
        };
    }

    /// <summary>
    /// 自行加入前的准入校验。调用方需已确认「该用户还不是成员」。
    /// </summary>
    public void EnsureJoinable()
    {
        DomainException.Ensure(
            IsOpenToJoin,
            "该群仅限邀请加入，请联系群主或管理员邀请你");
    }

    /// <summary>设置入群方式（调用前须由 GroupMember.EnsureCanManageGroup 校验权限）</summary>
    public void SetJoinPolicy(GroupJoinPolicy policy, int operatedBy, DateTime now)
    {
        if (JoinPolicy == policy) return;

        JoinPolicy = policy;
        Raise(new GroupJoinPolicyChanged(Id, policy, operatedBy, now));
    }

    /// <summary>
    /// 开启或重置邀请链接（调用前须由 GroupMember.EnsureCanManageGroup 校验权限）。
    /// 重置会立刻覆盖旧 token —— 旧链接立即失效，已提交的待审批申请不受影响。
    /// </summary>
    public void EnableInviteLink(string token, DateTime now)
    {
        var normalized = (token ?? string.Empty).Trim();
        DomainException.Ensure(
            normalized.Length is >= InviteTokenMinLength and <= InviteTokenMaxLength,
            "邀请链接 token 长度不合法");
        DomainException.Ensure(
            normalized.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'),
            "邀请链接 token 只能包含字母、数字、- 与 _");

        InviteToken = normalized;
        InviteTokenExpiresAt = now.AddDays(InviteLinkTtlDays);
    }

    /// <summary>
    /// 关闭邀请链接（调用前须由 GroupMember.EnsureCanManageGroup 校验权限）。
    /// 已提交的申请保留：链接失效不应该连带丢掉管理员还没处理的待审批记录。
    /// </summary>
    public void DisableInviteLink()
    {
        InviteToken = null;
        InviteTokenExpiresAt = null;
    }

    /// <summary>
    /// 凭链接进入前的准入校验。
    /// 「失效」与「不存在」共用同一句提示：调用方不该让外人区分出群是否存在。
    /// </summary>
    public void EnsureInviteLinkUsable(DateTime now)
        => DomainException.Ensure(HasActiveInviteLink(now), "邀请链接已失效，请让群主重新生成");

    /// <summary>设置凭链接入群是否需要审批（调用前须由 GroupMember.EnsureCanManageGroup 校验权限）</summary>
    public void SetJoinApprovalRequired(bool required) => JoinApprovalRequired = required;

    /// <summary>设置/清除公告（调用前须由 GroupMember.EnsureCanManageGroup 校验权限）</summary>
    public void SetAnnouncement(string? text, int editedBy, DateTime now)
    {
        var announcement = GroupAnnouncement.Create(text, editedBy, now);
        AnnouncementText = announcement?.Text;
        AnnouncementAt = announcement is null ? null : announcement.EditedAt;
        AnnouncementBy = announcement is null ? null : announcement.EditedBy;
        Raise(new GroupAnnouncementChanged(Id, editedBy, announcement is not null, now));
    }

    /// <summary>转让群主（原群主由调用方降级为普通成员）</summary>
    public void TransferOwnership(int newOwnerId, DateTime now)
    {
        DomainException.Ensure(newOwnerId != OwnerId, "新群主不能是当前群主");
        var previousOwnerId = OwnerId;
        OwnerId = newOwnerId;
        Raise(new GroupOwnershipTransferred(Id, previousOwnerId, newOwnerId, now));
    }

    /// <summary>该用户是否为群主</summary>
    public bool IsOwnedBy(int userId) => OwnerId == userId;

    /// <summary>仅群主可解散</summary>
    public void EnsureCanBeDismissedBy(int userId)
    {
        DomainException.Ensure(IsOwnedBy(userId), "只有群主才能解散群组");
    }

    /// <summary>标记解散（记录随后由仓储删除；memberIds 供订阅方通知在线成员）</summary>
    public void Dissolve(IReadOnlyList<int> memberIds, DateTime now)
        => Raise(new GroupDissolved(Id, Name, memberIds, now));
}
