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
/// 群组聚合根。
/// 成员是独立聚合（GroupMember）—— 大集合不塞进聚合根，按「小聚合 + 按标识引用」建模，
/// 否则每次发言都得把整群成员载入内存。跨两者的不变量由 GroupMember 的守卫方法表达。
/// </summary>
public sealed class Group : AggregateRoot<long>
{
    public const int MaxNameLength = 100;

    private Group() { }

    public string Name { get; private set; } = string.Empty;

    public string? Avatar { get; private set; }

    public int OwnerId { get; private set; }

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
            CreatedAt = now
        };
    }

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
