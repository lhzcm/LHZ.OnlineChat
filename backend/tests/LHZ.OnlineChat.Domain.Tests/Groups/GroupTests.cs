using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Groups;

namespace LHZ.OnlineChat.Domain.Tests.Groups;

public class GroupCreationTests
{
    [Fact]
    public void Create_去空白并记录群主()
    {
        var group = Group.Create("  技术交流群 ", "/uploads/g.png", ownerId: 10001, T.Now);

        Assert.Equal("技术交流群", group.Name);
        Assert.Equal("/uploads/g.png", group.Avatar);
        Assert.Equal(10001, group.OwnerId);
        Assert.Null(group.Announcement);
        Assert.Equal(T.Now, group.CreatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_群名为空时抛出(string? name)
    {
        var ex = Assert.Throws<DomainException>(() => Group.Create(name!, null, 10001, T.Now));

        Assert.Equal("群组名称不能为空", ex.Message);
    }

    [Fact]
    public void Create_群名超长时抛出()
    {
        var ex = Assert.Throws<DomainException>(
            () => Group.Create(new string('x', Group.MaxNameLength + 1), null, 10001, T.Now));

        Assert.Equal($"群组名称不能超过 {Group.MaxNameLength} 个字符", ex.Message);
    }

    [Theory]
    [InlineData(10001, true)]
    [InlineData(10002, false)]
    public void IsOwnedBy_判定群主(int userId, bool expected)
    {
        var group = TestGroups.Create(ownerId: 10001);

        Assert.Equal(expected, group.IsOwnedBy(userId));
    }

    [Fact]
    public void EnsureCanBeDismissedBy_非群主被拒()
    {
        var group = TestGroups.Create(ownerId: 10001);

        group.EnsureCanBeDismissedBy(10001);
        Assert.Equal("只有群主才能解散群组",
            Assert.Throws<DomainException>(() => group.EnsureCanBeDismissedBy(10002)).Message);
    }

    [Fact]
    public void Dissolve_发出携带解散前成员名单的事件()
    {
        // 成员名单必须在删除前抓取，否则事件订阅方无从通知谁
        var group = TestGroups.Create();
        var memberIds = new[] { 10001, 10002, 10003 };

        group.Dissolve(memberIds, T.Now);

        var dissolved = Assert.IsType<GroupDissolved>(Assert.Single(group.DequeueDomainEvents()));
        Assert.Equal(group.Id, dissolved.GroupId);
        Assert.Equal(memberIds, dissolved.MemberIds);
        Assert.Equal(group.Name, dissolved.Name);
    }
}

public class GroupAnnouncementTests
{
    [Fact]
    public void SetAnnouncement_三列同时写入()
    {
        var group = TestGroups.Create();

        group.SetAnnouncement("欢迎加入", editedBy: 10001, T.Now);

        Assert.Equal("欢迎加入", group.AnnouncementText);
        Assert.Equal(T.Now, group.AnnouncementAt);
        Assert.Equal(10001, group.AnnouncementBy);
    }

    [Fact]
    public void SetAnnouncement_聚合成值对象对外暴露()
    {
        var group = TestGroups.Create();

        group.SetAnnouncement("欢迎加入", 10001, T.Now);

        var announcement = group.Announcement;
        Assert.NotNull(announcement);
        Assert.Equal("欢迎加入", announcement!.Text);
        Assert.Equal(T.Now, announcement.EditedAt);
        Assert.Equal(10001, announcement.EditedBy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void SetAnnouncement_空白时三列一并置空(string? text)
    {
        // 改造前是三个各自可空的散列字段，清除公告要记得同时清三处，漏一处就留下脏数据
        var group = TestGroups.Create();
        group.SetAnnouncement("旧公告", 10001, T.Now);

        group.SetAnnouncement(text, 10002, T.PlusMinutes(5));

        Assert.Null(group.AnnouncementText);
        Assert.Null(group.AnnouncementAt);
        Assert.Null(group.AnnouncementBy);
        Assert.Null(group.Announcement);
    }

    [Fact]
    public void SetAnnouncement_超长时抛出()
    {
        var group = TestGroups.Create();

        var ex = Assert.Throws<DomainException>(() => group.SetAnnouncement(
            new string('公', GroupAnnouncement.MaxLength + 1), 10001, T.Now));

        Assert.Equal($"公告内容过长（最多 {GroupAnnouncement.MaxLength} 字）", ex.Message);
    }

    [Fact]
    public void SetAnnouncement_发出事件并标明是否仍有公告()
    {
        var group = TestGroups.Create();

        group.SetAnnouncement("内容", 10001, T.Now);
        var set = Assert.IsType<GroupAnnouncementChanged>(Assert.Single(group.DequeueDomainEvents()));
        Assert.True(set.HasAnnouncement);

        group.SetAnnouncement("", 10001, T.Now);
        var cleared = Assert.IsType<GroupAnnouncementChanged>(Assert.Single(group.DequeueDomainEvents()));
        Assert.False(cleared.HasAnnouncement);
    }

    [Fact]
    public void Restore_从历史数据还原_缺时间与编辑者也不崩()
    {
        // 早期数据可能只有正文而没有时间/编辑者
        var announcement = GroupAnnouncement.Restore("旧公告", null, null);

        Assert.NotNull(announcement);
        Assert.Equal("旧公告", announcement!.Text);
        Assert.Equal(default, announcement.EditedAt);
        Assert.Equal(0, announcement.EditedBy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Restore_正文为空时返回null(string? text)
    {
        Assert.Null(GroupAnnouncement.Restore(text, T.Now, 10001));
    }

    [Fact]
    public void 值相等语义()
    {
        var a = GroupAnnouncement.Create("同样的内容", 10001, T.Now);
        var b = GroupAnnouncement.Create("同样的内容", 10001, T.Now);

        Assert.Equal(a, b);
    }
}

public class GroupOwnershipTests
{
    [Fact]
    public void TransferOwnership_更新群主并发出事件()
    {
        var group = TestGroups.Create(ownerId: 10001);

        group.TransferOwnership(newOwnerId: 10002, T.Now);

        Assert.Equal(10002, group.OwnerId);
        var transferred = Assert.IsType<GroupOwnershipTransferred>(
            Assert.Single(group.DequeueDomainEvents()));
        Assert.Equal(10001, transferred.PreviousOwnerId);
        Assert.Equal(10002, transferred.NewOwnerId);
    }

    [Fact]
    public void TransferOwnership_转给当前群主时抛出()
    {
        var group = TestGroups.Create(ownerId: 10001);

        var ex = Assert.Throws<DomainException>(() => group.TransferOwnership(10001, T.Now));

        Assert.Equal("新群主不能是当前群主", ex.Message);
    }

    [Fact]
    public void TransferOwnership_失败时群主不变且无事件()
    {
        var group = TestGroups.Create(ownerId: 10001);

        Assert.Throws<DomainException>(() => group.TransferOwnership(10001, T.Now));

        Assert.Equal(10001, group.OwnerId);
        Assert.Empty(group.DequeueDomainEvents());
    }
}

internal static class TestGroups
{
    internal static Group Create(long id = 1, int ownerId = 10001, string name = "测试群")
    {
        var group = Group.Create(name, null, ownerId, T.Now);
        group.AssignPersistedId(id);
        group.DequeueDomainEvents();
        return group;
    }
}
