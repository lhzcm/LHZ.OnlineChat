using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Friends;

namespace LHZ.OnlineChat.Domain.Tests.Friends;

public class FriendshipRequestTests
{
    [Fact]
    public void Request_初始为待确认()
    {
        var friendship = Friendship.Request(requesterId: 10001, targetId: 10002, T.Now);

        Assert.Equal(10001, friendship.UserId);
        Assert.Equal(10002, friendship.FriendId);
        Assert.Equal(FriendshipStatus.Pending, friendship.Status);
        Assert.False(friendship.IsAccepted);
        Assert.Equal(T.Now, friendship.CreatedAt);
    }

    [Fact]
    public void Request_不能添加自己()
    {
        var ex = Assert.Throws<DomainException>(() => Friendship.Request(10001, 10001, T.Now));

        Assert.Equal("不能添加自己为好友", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Request_目标ID非法时抛出(int targetId)
    {
        var ex = Assert.Throws<DomainException>(() => Friendship.Request(10001, targetId, T.Now));

        Assert.Equal("请输入正确的账号 ID", ex.Message);
    }

    [Fact]
    public void EstablishDirectly_直接建立已接受关系()
    {
        // 创建机器人时与创建者自动成为好友，不走申请流程
        var friendship = Friendship.EstablishDirectly(10001, 10005, T.Now);

        Assert.Equal(FriendshipStatus.Accepted, friendship.Status);
        Assert.True(friendship.IsAccepted);
    }

    [Theory]
    [InlineData(FriendshipStatus.Pending, "已发送好友申请，等待对方确认")]
    [InlineData(FriendshipStatus.Accepted, "你们已经是好友了")]
    [InlineData(FriendshipStatus.Blocked, "对方已将你屏蔽")]
    public void DescribeRejectionOfNewRequest_提示语随状态而定(
        FriendshipStatus status, string expected)
    {
        var friendship = TestFriendships.With(status);

        Assert.Equal(expected, friendship.DescribeRejectionOfNewRequest());
    }
}

public class FriendshipAcceptTests
{
    [Fact]
    public void Accept_被申请方可接受并发出事件()
    {
        var friendship = TestFriendships.Pending(requesterId: 10001, targetId: 10002);

        friendship.Accept(operatorId: 10002, T.Now);

        Assert.Equal(FriendshipStatus.Accepted, friendship.Status);
        var accepted = Assert.IsType<FriendRequestAccepted>(
            Assert.Single(friendship.DequeueDomainEvents()));
        Assert.Equal(10001, accepted.RequesterId);
        Assert.Equal(10002, accepted.AccepterId);
    }

    [Fact]
    public void Accept_申请方自己不能接受()
    {
        var friendship = TestFriendships.Pending(requesterId: 10001, targetId: 10002);

        var ex = Assert.Throws<DomainException>(() => friendship.Accept(10001, T.Now));

        Assert.Equal("无权操作此申请", ex.Message);
    }

    [Fact]
    public void Accept_无关第三方不能接受()
    {
        var friendship = TestFriendships.Pending(10001, 10002);

        Assert.Throws<DomainException>(() => friendship.Accept(10003, T.Now));
    }

    [Fact]
    public void Accept_已处理的申请不能重复接受()
    {
        var friendship = TestFriendships.Pending(10001, 10002);
        friendship.Accept(10002, T.Now);

        var ex = Assert.Throws<DomainException>(() => friendship.Accept(10002, T.Now));

        Assert.Equal("该申请已处理", ex.Message);
    }

    [Fact]
    public void Accept_失败时不产生事件()
    {
        var friendship = TestFriendships.Pending(10001, 10002);

        Assert.Throws<DomainException>(() => friendship.Accept(10001, T.Now));

        Assert.Empty(friendship.DequeueDomainEvents());
    }

    [Fact]
    public void EnsureCanReject_仅被申请方可拒绝()
    {
        var friendship = TestFriendships.Pending(10001, 10002);

        friendship.EnsureCanReject(10002);
        Assert.Equal("无权操作此申请",
            Assert.Throws<DomainException>(() => friendship.EnsureCanReject(10001)).Message);
    }
}

public class FriendshipPeerTests
{
    [Theory]
    [InlineData(10001, true)]
    [InlineData(10002, true)]
    [InlineData(10003, false)]
    public void Involves_判断是否牵涉某人(int userId, bool expected)
    {
        var friendship = TestFriendships.Pending(10001, 10002);

        Assert.Equal(expected, friendship.Involves(userId));
    }

    [Theory]
    [InlineData(10001, 10002)]
    [InlineData(10002, 10001)]
    public void PeerOf_双向都能取到对方(int me, int expectedPeer)
    {
        var friendship = TestFriendships.Pending(10001, 10002);

        Assert.Equal(expectedPeer, friendship.PeerOf(me));
    }

    [Fact]
    public void PeerOf_与该关系无关时抛出()
    {
        var friendship = TestFriendships.Pending(10001, 10002);

        var ex = Assert.Throws<DomainException>(() => friendship.PeerOf(10003));

        Assert.Equal("该好友关系与当前用户无关", ex.Message);
    }
}

public class FriendSettingTests
{
    [Fact]
    public void CreateFor_以设置者视角创建()
    {
        var setting = FriendSetting.CreateFor(userId: 10001, friendId: 10002, T.Now);

        Assert.Equal(10001, setting.UserId);
        Assert.Equal(10002, setting.FriendId);
        Assert.Null(setting.Remark);
        Assert.Null(setting.Category);
    }

    [Fact]
    public void SetRemark_去空白并推进更新时间()
    {
        var setting = FriendSetting.CreateFor(10001, 10002, T.Now);
        var later = T.PlusMinutes(1);

        setting.SetRemark("  老王  ", later);

        Assert.Equal("老王", setting.Remark);
        Assert.Equal(later, setting.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void SetRemark_空白视为清除(string? input)
    {
        var setting = FriendSetting.CreateFor(10001, 10002, T.Now);
        setting.SetRemark("老王", T.Now);

        setting.SetRemark(input, T.Now);

        Assert.Null(setting.Remark);
    }

    [Fact]
    public void SetRemark_超长时抛出()
    {
        var setting = FriendSetting.CreateFor(10001, 10002, T.Now);

        var ex = Assert.Throws<DomainException>(() => setting.SetRemark(
            new string('x', FriendSetting.MaxRemarkLength + 1), T.Now));

        Assert.Equal($"备注长度不能超过 {FriendSetting.MaxRemarkLength} 个字符", ex.Message);
    }

    [Fact]
    public void SetRemark_恰好达到上限时通过()
    {
        var setting = FriendSetting.CreateFor(10001, 10002, T.Now);

        setting.SetRemark(new string('x', FriendSetting.MaxRemarkLength), T.Now);

        Assert.Equal(FriendSetting.MaxRemarkLength, setting.Remark!.Length);
    }

    [Fact]
    public void SetCategory_去空白并可清除()
    {
        var setting = FriendSetting.CreateFor(10001, 10002, T.Now);

        setting.SetCategory("  同事 ", T.Now);
        Assert.Equal("同事", setting.Category);

        setting.SetCategory("  ", T.Now);
        Assert.Null(setting.Category);
    }

    [Fact]
    public void SetCategory_超长时抛出()
    {
        var setting = FriendSetting.CreateFor(10001, 10002, T.Now);

        var ex = Assert.Throws<DomainException>(() => setting.SetCategory(
            new string('x', FriendSetting.MaxCategoryLength + 1), T.Now));

        Assert.Equal($"分类名称不能超过 {FriendSetting.MaxCategoryLength} 个字符", ex.Message);
    }

    [Fact]
    public void 备注与分类互不影响()
    {
        var setting = FriendSetting.CreateFor(10001, 10002, T.Now);

        setting.SetRemark("老王", T.Now);
        setting.SetCategory("同事", T.Now);
        setting.SetRemark(null, T.Now);

        Assert.Null(setting.Remark);
        Assert.Equal("同事", setting.Category);
    }
}

internal static class TestFriendships
{
    internal static Friendship Pending(int requesterId, int targetId, long id = 1)
    {
        var friendship = Friendship.Request(requesterId, targetId, T.Now);
        friendship.AssignPersistedId(id);
        return friendship;
    }

    /// <summary>构造处于任意状态的关系（Blocked 无公开路径，走已有行为拼出来）</summary>
    internal static Friendship With(FriendshipStatus status)
    {
        switch (status)
        {
            case FriendshipStatus.Pending:
                return Pending(10001, 10002);

            case FriendshipStatus.Accepted:
                var accepted = Friendship.EstablishDirectly(10001, 10002, T.Now);
                accepted.AssignPersistedId(1);
                return accepted;

            case FriendshipStatus.Blocked:
                // Blocked 是历史数据里可能存在的状态，用反射构造以覆盖提示语分支
                var blocked = Pending(10001, 10002);
                typeof(Friendship)
                    .GetProperty(nameof(Friendship.Status))!
                    .SetValue(blocked, FriendshipStatus.Blocked);
                return blocked;

            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }
}
