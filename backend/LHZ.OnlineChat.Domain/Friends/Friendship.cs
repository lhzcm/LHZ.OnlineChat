using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Friends;

/// <summary>好友关系状态（替代原来的魔法数 0/1/2）</summary>
public enum FriendshipStatus
{
    /// <summary>待确认</summary>
    Pending = 0,

    /// <summary>已接受</summary>
    Accepted = 1,

    /// <summary>已屏蔽</summary>
    Blocked = 2
}

/// <summary>
/// 好友关系聚合根。
/// 一条记录表达双向关系（UserId=申请方，FriendId=被申请方），查询时双向匹配。
/// </summary>
public sealed class Friendship : AggregateRoot<long>
{
    private Friendship() { }

    /// <summary>申请方</summary>
    public int UserId { get; private set; }

    /// <summary>被申请方</summary>
    public int FriendId { get; private set; }

    public FriendshipStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>发起好友申请</summary>
    public static Friendship Request(int requesterId, int targetId, DateTime now)
    {
        DomainException.Ensure(targetId > 0, "请输入正确的账号 ID");
        DomainException.Ensure(requesterId != targetId, "不能添加自己为好友");
        return new Friendship
        {
            UserId = requesterId,
            FriendId = targetId,
            Status = FriendshipStatus.Pending,
            CreatedAt = now
        };
    }

    /// <summary>直接建立已接受的关系（创建机器人时与创建者自动成为好友）</summary>
    public static Friendship EstablishDirectly(int userId, int friendId, DateTime now)
        => new()
        {
            UserId = userId,
            FriendId = friendId,
            Status = FriendshipStatus.Accepted,
            CreatedAt = now
        };

    /// <summary>已存在关系时，该如何驳回新的申请 —— 提示语随状态而定</summary>
    public string DescribeRejectionOfNewRequest() => Status switch
    {
        FriendshipStatus.Pending => "已发送好友申请，等待对方确认",
        FriendshipStatus.Accepted => "你们已经是好友了",
        FriendshipStatus.Blocked => "对方已将你屏蔽",
        _ => "操作失败"
    };

    /// <summary>接受申请：仅被申请方可操作，且必须仍处于待确认</summary>
    public void Accept(int operatorId, DateTime now)
    {
        DomainException.Ensure(FriendId == operatorId, "无权操作此申请");
        DomainException.Ensure(Status == FriendshipStatus.Pending, "该申请已处理");
        Status = FriendshipStatus.Accepted;
        Raise(new FriendRequestAccepted(UserId, FriendId, now));
    }

    /// <summary>拒绝申请：仅被申请方可操作（记录随后由仓储删除）</summary>
    public void EnsureCanReject(int operatorId)
    {
        DomainException.Ensure(FriendId == operatorId, "无权操作此申请");
    }

    /// <summary>该关系是否牵涉指定用户</summary>
    public bool Involves(int userId) => UserId == userId || FriendId == userId;

    /// <summary>取对方的账号 ID</summary>
    public int PeerOf(int userId)
    {
        DomainException.Ensure(Involves(userId), "该好友关系与当前用户无关");
        return UserId == userId ? FriendId : UserId;
    }

    public bool IsAccepted => Status == FriendshipStatus.Accepted;
}

/// <summary>
/// 好友设置聚合根（备注 + 分类标签），以设置者视角存储。
/// UserId=设置者，FriendId=被设置的好友；双方互不可见对方的设置。
/// </summary>
public sealed class FriendSetting : AggregateRoot<long>
{
    public const int MaxRemarkLength = 50;
    public const int MaxCategoryLength = 30;

    private FriendSetting() { }

    public int UserId { get; private set; }

    public int FriendId { get; private set; }

    /// <summary>备注名；null 表示显示对方昵称</summary>
    public string? Remark { get; private set; }

    /// <summary>分类标签；null 表示未分组</summary>
    public string? Category { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static FriendSetting CreateFor(int userId, int friendId, DateTime now)
        => new() { UserId = userId, FriendId = friendId, UpdatedAt = now };

    /// <summary>设置备注；空白视为清除</summary>
    public void SetRemark(string? remark, DateTime now)
    {
        var normalized = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim();
        DomainException.Ensure(normalized is null || normalized.Length <= MaxRemarkLength,
            $"备注长度不能超过 {MaxRemarkLength} 个字符");
        Remark = normalized;
        UpdatedAt = now;
    }

    /// <summary>设置分类；空白视为清除（未分组）</summary>
    public void SetCategory(string? category, DateTime now)
    {
        var normalized = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        DomainException.Ensure(normalized is null || normalized.Length <= MaxCategoryLength,
            $"分类名称不能超过 {MaxCategoryLength} 个字符");
        Category = normalized;
        UpdatedAt = now;
    }
}
