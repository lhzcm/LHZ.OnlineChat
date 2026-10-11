using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Groups;

namespace LHZ.OnlineChat.Domain.Tests.Groups;

public class GroupMemberFactoryTests
{
    [Fact(DisplayName = "CreateOwner_角色为群主且游标为0")]
    public void CreateOwner_SetsOwnerRoleAndZeroCursor()
    {
        var member = GroupMember.CreateOwner(groupId: 1, ownerId: 10001, T.Now);

        Assert.Equal(GroupRole.Owner, member.Role);
        Assert.True(member.IsOwner);
        Assert.Equal(0, member.LastReadMessageId);
        Assert.Null(member.MutedUntil);
        Assert.Equal(T.Now, member.JoinedAt);
    }

    [Fact(DisplayName = "Join_已读游标初始化为入群时的最新消息")]
    public void Join_InitializesReadCursorToLatestMessage()
    {
        // 否则入群前的全部历史都会被当成"离线消息"补发给新成员
        var member = GroupMember.Join(groupId: 1, userId: 10002, latestMessageId: 500, T.Now);

        Assert.Equal(GroupRole.Member, member.Role);
        Assert.Equal(500, member.LastReadMessageId);
    }

    [Fact(DisplayName = "Join_空群时游标为0")]
    public void Join_EmptyGroup_ReadCursorIsZero()
    {
        var member = GroupMember.Join(1, 10002, latestMessageId: 0, T.Now);

        Assert.Equal(0, member.LastReadMessageId);
    }
}

public class GroupMemberPermissionTests
{
    [Theory(DisplayName = "CanManage_群主与管理员具备管理权限")]
    [InlineData(GroupRole.Owner, true)]
    [InlineData(GroupRole.Admin, true)]
    [InlineData(GroupRole.Member, false)]
    public void CanManage_OwnerAndAdmin_HavePermission(GroupRole role, bool expected)
    {
        Assert.Equal(expected, TestMembers.With(role).CanManage);
    }

    [Theory(DisplayName = "EnsureCanManageGroup_有权限时通过")]
    [InlineData(GroupRole.Owner)]
    [InlineData(GroupRole.Admin)]
    public void EnsureCanManageGroup_WithPermission_Passes(GroupRole role)
    {
        TestMembers.With(role).EnsureCanManageGroup("邀请成员");
    }

    [Fact(DisplayName = "EnsureCanManageGroup_普通成员被拒且提示语包含动作名")]
    public void EnsureCanManageGroup_Member_IsRejectedWithActionName()
    {
        var member = TestMembers.With(GroupRole.Member);

        var ex = Assert.Throws<DomainException>(() => member.EnsureCanManageGroup("设置公告"));

        Assert.Equal("只有群主或管理员可以设置公告", ex.Message);
    }

    [Fact(DisplayName = "EnsureIsOwner_仅群主通过")]
    public void EnsureIsOwner_OnlyOwner_Passes()
    {
        TestMembers.With(GroupRole.Owner).EnsureIsOwner("设置管理员");

        foreach (var role in new[] { GroupRole.Admin, GroupRole.Member })
        {
            var ex = Assert.Throws<DomainException>(
                () => TestMembers.With(role).EnsureIsOwner("设置管理员"));
            Assert.Equal("只有群主可以设置管理员", ex.Message);
        }
    }
}

public class GroupMemberRemovalTests
{
    [Fact(DisplayName = "EnsureCanRemove_群主可踢管理员与成员")]
    public void EnsureCanRemove_Owner_CanRemoveAdminAndMember()
    {
        var owner = TestMembers.With(GroupRole.Owner, userId: 10001);

        owner.EnsureCanRemove(TestMembers.With(GroupRole.Admin, userId: 10002));
        owner.EnsureCanRemove(TestMembers.With(GroupRole.Member, userId: 10003));
    }

    [Fact(DisplayName = "EnsureCanRemove_管理员可踢普通成员")]
    public void EnsureCanRemove_Admin_CanRemoveMember()
    {
        var admin = TestMembers.With(GroupRole.Admin, userId: 10002);

        admin.EnsureCanRemove(TestMembers.With(GroupRole.Member, userId: 10003));
    }

    [Fact(DisplayName = "EnsureCanRemove_谁都不能踢群主")]
    public void EnsureCanRemove_Nobody_CanRemoveOwner()
    {
        var owner = TestMembers.With(GroupRole.Owner, userId: 10001);
        var admin = TestMembers.With(GroupRole.Admin, userId: 10002);

        Assert.Equal("不能踢出群主",
            Assert.Throws<DomainException>(() => admin.EnsureCanRemove(owner)).Message);
    }

    [Fact(DisplayName = "EnsureCanRemove_管理员不能踢其他管理员")]
    public void EnsureCanRemove_Admin_CannotRemoveOtherAdmin()
    {
        var admin = TestMembers.With(GroupRole.Admin, userId: 10002);
        var otherAdmin = TestMembers.With(GroupRole.Admin, userId: 10003);

        Assert.Equal("管理员不能踢出其他管理员",
            Assert.Throws<DomainException>(() => admin.EnsureCanRemove(otherAdmin)).Message);
    }

    [Fact(DisplayName = "EnsureCanRemove_普通成员无权踢人")]
    public void EnsureCanRemove_Member_IsNotAllowed()
    {
        var member = TestMembers.With(GroupRole.Member, userId: 10003);

        Assert.Equal("只有群主或管理员可以踢人",
            Assert.Throws<DomainException>(
                () => member.EnsureCanRemove(TestMembers.With(GroupRole.Member, 10004))).Message);
    }

    [Fact(DisplayName = "EnsureCanLeave_群主不能直接退群")]
    public void EnsureCanLeave_Owner_CannotLeaveDirectly()
    {
        Assert.Equal("群主不能直接退出，请先转让群主或解散群组",
            Assert.Throws<DomainException>(
                () => TestMembers.With(GroupRole.Owner).EnsureCanLeave()).Message);
    }

    [Theory(DisplayName = "EnsureCanLeave_非群主可退群")]
    [InlineData(GroupRole.Admin)]
    [InlineData(GroupRole.Member)]
    public void EnsureCanLeave_NonOwner_CanLeave(GroupRole role)
    {
        TestMembers.With(role).EnsureCanLeave();
    }
}

public class GroupMemberMuteTests
{
    [Fact(DisplayName = "IsMuted_未设置时为假")]
    public void IsMuted_WhenNotSet_IsFalse()
    {
        Assert.False(TestMembers.With(GroupRole.Member).IsMuted(T.Now));
    }

    [Fact(DisplayName = "SetMute_未来时间生效")]
    public void SetMute_FutureTime_TakesEffect()
    {
        var member = TestMembers.With(GroupRole.Member);
        var until = T.Plus(TimeSpan.FromHours(1));

        var muted = member.SetMute(until, T.Now);

        Assert.True(muted);
        Assert.Equal(until, member.MutedUntil);
        Assert.True(member.IsMuted(T.Now));
    }

    [Fact(DisplayName = "SetMute_传null表示解除")]
    public void SetMute_Null_Unmutes()
    {
        var member = TestMembers.With(GroupRole.Member);
        member.SetMute(T.Plus(TimeSpan.FromHours(1)), T.Now);

        var muted = member.SetMute(null, T.Now);

        Assert.False(muted);
        Assert.Null(member.MutedUntil);
        Assert.False(member.IsMuted(T.Now));
    }

    [Fact(DisplayName = "SetMute_传过去时间等同于解除")]
    public void SetMute_PastTime_Unmutes()
    {
        var member = TestMembers.With(GroupRole.Member);

        var muted = member.SetMute(T.MinusMinutes(1), T.Now);

        Assert.False(muted);
        Assert.Null(member.MutedUntil);
    }

    [Fact(DisplayName = "IsMuted_到期后自动解除_无需额外清理任务")]
    public void IsMuted_AfterExpiry_AutoUnmutesWithoutCleanupTask()
    {
        var member = TestMembers.With(GroupRole.Member);
        var until = T.PlusMinutes(30);
        member.SetMute(until, T.Now);

        Assert.True(member.IsMuted(T.PlusMinutes(29)));
        Assert.False(member.IsMuted(until));                 // 边界：到期即解除
        Assert.False(member.IsMuted(T.PlusMinutes(31)));
    }

    [Fact(DisplayName = "SetMute_不能禁言群主")]
    public void SetMute_Owner_CannotBeMuted()
    {
        var owner = TestMembers.With(GroupRole.Owner);

        var ex = Assert.Throws<DomainException>(
            () => owner.SetMute(T.Plus(TimeSpan.FromHours(1)), T.Now));

        Assert.Equal("不能禁言群主", ex.Message);
    }

    [Fact(DisplayName = "EnsureNotMuted_未禁言时通过")]
    public void EnsureNotMuted_WhenNotMuted_Passes()
    {
        TestMembers.With(GroupRole.Member).EnsureNotMuted(T.Now, _ => "不该用到");
    }

    [Fact(DisplayName = "EnsureNotMuted_禁言中抛出专用异常并携带截止时间")]
    public void EnsureNotMuted_WhenMuted_ThrowsWithMutedUntil()
    {
        // 单独的异常类型：WS 侧要回一个带截止时间的 muted 协议帧，而非普通失败提示
        var member = TestMembers.With(GroupRole.Member);
        var until = T.Plus(TimeSpan.FromHours(2));
        member.SetMute(until, T.Now);

        var ex = Assert.Throws<MemberMutedException>(
            () => member.EnsureNotMuted(T.Now, d => $"禁言至 {d:MM-dd HH:mm}"));

        Assert.Equal(until, ex.MutedUntil);
        Assert.Equal("禁言至 03-14 12:30", ex.Message);
        Assert.IsAssignableFrom<DomainException>(ex);
    }
}

public class GroupMemberRoleChangeTests
{
    [Fact(DisplayName = "ChangeAdminRole_成员升为管理员再降回")]
    public void ChangeAdminRole_PromoteThenDemoteMember()
    {
        var member = TestMembers.With(GroupRole.Member);

        member.ChangeAdminRole(true);
        Assert.Equal(GroupRole.Admin, member.Role);

        member.ChangeAdminRole(false);
        Assert.Equal(GroupRole.Member, member.Role);
    }

    [Fact(DisplayName = "ChangeAdminRole_不能借此改动群主身份")]
    public void ChangeAdminRole_OwnerRole_IsUnchanged()
    {
        var owner = TestMembers.With(GroupRole.Owner);

        var ex = Assert.Throws<DomainException>(() => owner.ChangeAdminRole(false));

        Assert.Equal("不能修改群主的身份", ex.Message);
        Assert.Equal(GroupRole.Owner, owner.Role);
    }

    [Fact(DisplayName = "PromoteToOwner_与_DemoteFromOwner_配合完成转让")]
    public void PromoteToOwner_AndDemoteFromOwner_TransferOwnership()
    {
        var newOwner = TestMembers.With(GroupRole.Admin, userId: 10002);
        var oldOwner = TestMembers.With(GroupRole.Owner, userId: 10001);

        newOwner.PromoteToOwner();
        oldOwner.DemoteFromOwner();

        Assert.Equal(GroupRole.Owner, newOwner.Role);
        Assert.Equal(GroupRole.Member, oldOwner.Role);
    }
}

public class GroupMemberReadCursorTests
{
    [Fact(DisplayName = "AdvanceReadCursor_只增不减")]
    public void AdvanceReadCursor_OnlyAdvances_NeverGoesBack()
    {
        var member = GroupMember.Join(1, 10002, latestMessageId: 100, T.Now);

        member.AdvanceReadCursor(150);
        Assert.Equal(150, member.LastReadMessageId);

        // 乱序到达的旧回执不能把游标往回拖，否则已读消息会被当作未读重发
        member.AdvanceReadCursor(120);
        Assert.Equal(150, member.LastReadMessageId);
    }

    [Fact(DisplayName = "AdvanceReadCursor_相同值不变")]
    public void AdvanceReadCursor_SameValue_StaysUnchanged()
    {
        var member = GroupMember.Join(1, 10002, 100, T.Now);

        member.AdvanceReadCursor(100);

        Assert.Equal(100, member.LastReadMessageId);
    }
}

internal static class TestMembers
{
    internal static GroupMember With(GroupRole role, int userId = 10001, long groupId = 1, long id = 1)
    {
        var member = role == GroupRole.Owner
            ? GroupMember.CreateOwner(groupId, userId, T.Now)
            : GroupMember.Join(groupId, userId, latestMessageId: 0, T.Now);

        if (role == GroupRole.Admin) member.ChangeAdminRole(true);
        member.AssignPersistedId(id);
        return member;
    }
}
