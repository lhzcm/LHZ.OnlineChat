using LHZ.OnlineChat.Application.Groups.Commands;
using LHZ.OnlineChat.Application.Groups.EventHandlers;
using LHZ.OnlineChat.Application.Groups.Queries;
using LHZ.OnlineChat.Application.Robots.Commands;
using LHZ.OnlineChat.Application.Tests.TestDoubles;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Friends;
using LHZ.OnlineChat.Domain.Groups;

namespace LHZ.OnlineChat.Application.Tests.Groups;

public class CreateGroupTests
{
    private readonly ApplicationTestContext _ctx = new();

    private CreateGroupHandler Handler()
        => new(_ctx.Groups, _ctx.GroupMembers, _ctx.Events, _ctx.Clock);

    [Fact]
    public async Task 建群成功_创建者即群主()
    {
        var owner = _ctx.GivenUser();

        var result = await Handler().Handle(
            new CreateGroupCommand { OwnerId = owner.Id, Name = "技术群" }, default);

        Assert.True(result.Success);
        Assert.Equal("技术群", result.Data!.Name);
        Assert.Equal(0, result.Data.MyRole);
        Assert.Equal(1, result.Data.MemberCount);
    }

    [Fact]
    public async Task 建群后创建者被登记为群主成员()
    {
        var owner = _ctx.GivenUser();

        await Handler().Handle(new CreateGroupCommand { OwnerId = owner.Id, Name = "技术群" }, default);

        var member = Assert.Single(_ctx.GroupMembers.All);
        Assert.Equal(owner.Id, member.UserId);
        Assert.Equal(GroupRole.Owner, member.Role);
    }

    [Fact]
    public async Task 建群发出事件()
    {
        var owner = _ctx.GivenUser();

        await Handler().Handle(new CreateGroupCommand { OwnerId = owner.Id, Name = "技术群" }, default);

        var e = _ctx.Events.SingleEvent<GroupCreated>();
        Assert.Equal("技术群", e.Name);
        Assert.Equal(owner.Id, e.OwnerId);
        Assert.NotEqual(0, e.GroupId);
    }

    [Fact]
    public async Task 群名为空时抛出且不留下半成品()
    {
        var owner = _ctx.GivenUser();

        await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new CreateGroupCommand { OwnerId = owner.Id, Name = "   " }, default));

        Assert.Empty(_ctx.Groups.All);
        Assert.Empty(_ctx.GroupMembers.All);
    }
}

public class JoinLeaveGroupTests
{
    private readonly ApplicationTestContext _ctx = new();

    private JoinGroupHandler JoinHandler()
        => new(_ctx.Groups, _ctx.GroupMembers, _ctx.GroupMessages, _ctx.Clock);

    [Fact]
    public async Task 加入群组成功()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var joiner = _ctx.GivenUser("新人", "j@test.local");
        var group = _ctx.GivenOpenGroup(owner.Id);

        var result = await JoinHandler().Handle(
            new JoinGroupCommand { GroupId = group.Id, UserId = joiner.Id }, default);

        Assert.Equal("加入群组成功", result.Message);
        Assert.True(await _ctx.GroupMembers.ExistsAsync(group.Id, joiner.Id));
    }

    [Fact]
    public async Task 默认仅限邀请_不能自行加入()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var outsider = _ctx.GivenUser("路人", "x@test.local");

        // 默认建群即 InviteOnly：群 ID 是连续自增的，若默认可加入，
        // 任何人都能枚举 ID 进群，再顺着历史接口读走全部消息
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => JoinHandler().Handle(
            new JoinGroupCommand { GroupId = group.Id, UserId = outsider.Id }, default));

        Assert.Contains("仅限邀请", ex.Message);
        Assert.False(await _ctx.GroupMembers.ExistsAsync(group.Id, outsider.Id));
    }

    [Fact]
    public async Task 从开放改回仅限邀请后不能再自行加入()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var outsider = _ctx.GivenUser("路人", "x@test.local");
        var group = _ctx.GivenOpenGroup(owner.Id);

        group.SetJoinPolicy(Domain.Groups.GroupJoinPolicy.InviteOnly, owner.Id, _ctx.Now);
        await _ctx.Groups.UpdateAsync(group);

        await Assert.ThrowsAsync<DomainException>(() => JoinHandler().Handle(
            new JoinGroupCommand { GroupId = group.Id, UserId = outsider.Id }, default));
    }

    [Fact]
    public async Task 加入时已读游标设为当前最新消息_避免历史被当离线消息补发()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var joiner = _ctx.GivenUser("新人", "j@test.local");
        var group = _ctx.GivenOpenGroup(owner.Id);
        _ctx.GivenGroupMessage(group.Id, owner.Id, "历史1");
        var latest = _ctx.GivenGroupMessage(group.Id, owner.Id, "历史2");

        await JoinHandler().Handle(
            new JoinGroupCommand { GroupId = group.Id, UserId = joiner.Id }, default);

        var member = await _ctx.GroupMembers.FindAsync(group.Id, joiner.Id);
        Assert.Equal(latest.Id, member!.LastReadMessageId);
    }

    [Fact]
    public async Task 空群加入时游标为0()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var joiner = _ctx.GivenUser("新人", "j@test.local");
        var group = _ctx.GivenOpenGroup(owner.Id);

        await JoinHandler().Handle(
            new JoinGroupCommand { GroupId = group.Id, UserId = joiner.Id }, default);

        Assert.Equal(0, (await _ctx.GroupMembers.FindAsync(group.Id, joiner.Id))!.LastReadMessageId);
    }

    [Fact]
    public async Task 重复加入被拒()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => JoinHandler().Handle(
            new JoinGroupCommand { GroupId = group.Id, UserId = owner.Id }, default));

        Assert.Equal("你已经是该群组成员", ex.Message);
    }

    [Fact]
    public async Task 群不存在时抛出()
    {
        var user = _ctx.GivenUser();

        await Assert.ThrowsAsync<EntityNotFoundException>(() => JoinHandler().Handle(
            new JoinGroupCommand { GroupId = 99999, UserId = user.Id }, default));
    }

    [Fact]
    public async Task 普通成员可退群()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        var result = await new LeaveGroupHandler(_ctx.GroupMembers).Handle(
            new LeaveGroupCommand { GroupId = group.Id, UserId = member.Id }, default);

        Assert.Equal("已退出群组", result.Message);
        Assert.False(await _ctx.GroupMembers.ExistsAsync(group.Id, member.Id));
    }

    [Fact]
    public async Task 群主不能直接退群()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new LeaveGroupHandler(_ctx.GroupMembers).Handle(
                new LeaveGroupCommand { GroupId = group.Id, UserId = owner.Id }, default));

        Assert.Equal("群主不能直接退出，请先转让群主或解散群组", ex.Message);
        Assert.True(await _ctx.GroupMembers.ExistsAsync(group.Id, owner.Id));
    }

    [Fact]
    public async Task 非成员退群时抛出()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => new LeaveGroupHandler(_ctx.GroupMembers).Handle(
                new LeaveGroupCommand { GroupId = group.Id, UserId = 99999 }, default));

        Assert.Equal("你不是该群组成员", ex.Message);
    }
}

public class DismissGroupTests
{
    private readonly ApplicationTestContext _ctx = new();

    private DismissGroupHandler Handler()
        => new(_ctx.Groups, _ctx.GroupMembers, _ctx.Events, _ctx.Clock);

    [Fact]
    public async Task 群主解散成功并清空成员()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        var result = await Handler().Handle(
            new DismissGroupCommand { GroupId = group.Id, UserId = owner.Id }, default);

        Assert.Equal("群组已解散", result.Message);
        Assert.Empty(_ctx.Groups.All);
        Assert.Empty(_ctx.GroupMembers.All);
    }

    [Fact]
    public async Task 非群主不能解散()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new DismissGroupCommand { GroupId = group.Id, UserId = member.Id }, default));

        Assert.Equal("只有群主才能解散群组", ex.Message);
        Assert.Single(_ctx.Groups.All);
    }

    /// <summary>
    /// 改造前的行为不一致：用户自己解散群不通知在线成员，只有管理后台解散才通知。
    /// 现在两条路径发同一个事件，订阅方统一处理。
    /// </summary>
    [Fact]
    public async Task 用户自行解散也会通知在线成员并清理会话设置()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);
        await _ctx.SessionSettings.AddAsync(Domain.Messaging.SessionSetting.Create(
            member.Id, Domain.Messaging.ChatSessionType.Group, group.Id, true, false, _ctx.Now));

        _ctx.Events.Subscribe(new HandleGroupDissolved(_ctx.Notifier, _ctx.SessionSettings));

        await Handler().Handle(
            new DismissGroupCommand { GroupId = group.Id, UserId = owner.Id }, default);

        var pushes = _ctx.Notifier.OfKind(PushKind.GroupDissolved).ToList();
        Assert.Equal(2, pushes.Count);
        Assert.Contains(pushes, p => p.ToUserId == owner.Id);
        Assert.Contains(pushes, p => p.ToUserId == member.Id);
        Assert.Empty(_ctx.SessionSettings.All);
    }

    [Fact]
    public async Task 解散事件携带解散前的成员名单()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        await Handler().Handle(
            new DismissGroupCommand { GroupId = group.Id, UserId = owner.Id }, default);

        var e = _ctx.Events.SingleEvent<GroupDissolved>();
        Assert.Equal(2, e.MemberIds.Count);
    }
}

public class InviteGroupMembersTests
{
    private readonly ApplicationTestContext _ctx = new();

    private InviteGroupMembersHandler Handler()
        => new(_ctx.Groups, _ctx.GroupMembers, _ctx.GroupMessages,
            _ctx.Friendships, _ctx.Events, _ctx.Clock);

    [Fact]
    public async Task 群主邀请好友成功并逐个通知()
    {
        var (owner, friend) = _ctx.GivenFriends();
        var group = _ctx.GivenGroup(owner.Id);
        _ctx.Events.Subscribe(new NotifyOnGroupMembersInvited(_ctx.Notifier));

        var result = await Handler().Handle(new InviteGroupMembersCommand
        {
            GroupId = group.Id, OperatorId = owner.Id, UserIds = new List<int> { friend.Id }
        }, default);

        Assert.Equal("已邀请 1 位好友加入群组", result.Message);
        var push = Assert.Single(_ctx.Notifier.OfKind(PushKind.GroupInvited));
        Assert.Equal(friend.Id, push.ToUserId);
        Assert.Equal(group.Id, push.ContextId);
    }

    [Fact]
    public async Task 管理员也可邀请()
    {
        var (owner, friend) = _ctx.GivenFriends();
        var admin = _ctx.GivenUser("管理员", "adm@test.local");
        var group = _ctx.GivenGroup(owner.Id, admin.Id);
        var adminMember = await _ctx.GroupMembers.FindAsync(group.Id, admin.Id);
        adminMember!.ChangeAdminRole(true);
        await _ctx.GroupMembers.UpdateAsync(adminMember);
        await _ctx.Friendships.AddAsync(Friendship.EstablishDirectly(admin.Id, friend.Id, _ctx.Now));

        var result = await Handler().Handle(new InviteGroupMembersCommand
        {
            GroupId = group.Id, OperatorId = admin.Id, UserIds = new List<int> { friend.Id }
        }, default);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task 普通成员不能邀请()
    {
        var (owner, friend) = _ctx.GivenFriends();
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);
        await _ctx.Friendships.AddAsync(Friendship.EstablishDirectly(member.Id, friend.Id, _ctx.Now));

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new InviteGroupMembersCommand
            {
                GroupId = group.Id, OperatorId = member.Id, UserIds = new List<int> { friend.Id }
            }, default));

        Assert.Equal("只有群主或管理员可以邀请成员", ex.Message);
    }

    [Fact]
    public async Task 只能邀请自己的好友()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var stranger = _ctx.GivenUser("陌生人", "s@test.local");
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new InviteGroupMembersCommand
            {
                GroupId = group.Id, OperatorId = owner.Id, UserIds = new List<int> { stranger.Id }
            }, default));

        Assert.Equal("只能邀请自己的好友加入群组", ex.Message);
    }

    [Fact]
    public async Task 全部已在群中时被拒()
    {
        var (owner, friend) = _ctx.GivenFriends();
        var group = _ctx.GivenGroup(owner.Id, friend.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new InviteGroupMembersCommand
            {
                GroupId = group.Id, OperatorId = owner.Id, UserIds = new List<int> { friend.Id }
            }, default));

        Assert.Equal("所选好友都已在该群中", ex.Message);
    }

    [Fact]
    public async Task 部分已在群中时只邀请其余的()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var inGroup = _ctx.GivenUser("已在群", "a@test.local");
        var newOne = _ctx.GivenUser("待邀请", "b@test.local");
        await _ctx.Friendships.AddAsync(Friendship.EstablishDirectly(owner.Id, inGroup.Id, _ctx.Now));
        await _ctx.Friendships.AddAsync(Friendship.EstablishDirectly(owner.Id, newOne.Id, _ctx.Now));
        var group = _ctx.GivenGroup(owner.Id, inGroup.Id);

        var result = await Handler().Handle(new InviteGroupMembersCommand
        {
            GroupId = group.Id,
            OperatorId = owner.Id,
            UserIds = new List<int> { inGroup.Id, newOne.Id }
        }, default);

        Assert.Equal("已邀请 1 位好友加入群组", result.Message);
        Assert.True(await _ctx.GroupMembers.ExistsAsync(group.Id, newOne.Id));
    }

    [Fact]
    public async Task 名单为空时被拒()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new InviteGroupMembersCommand
            {
                GroupId = group.Id, OperatorId = owner.Id, UserIds = new List<int>()
            }, default));

        Assert.Equal("请选择要邀请的好友", ex.Message);
    }

    [Fact]
    public async Task 名单去重后计数()
    {
        var (owner, friend) = _ctx.GivenFriends();
        var group = _ctx.GivenGroup(owner.Id);

        var result = await Handler().Handle(new InviteGroupMembersCommand
        {
            GroupId = group.Id,
            OperatorId = owner.Id,
            UserIds = new List<int> { friend.Id, friend.Id }
        }, default);

        Assert.Equal("已邀请 1 位好友加入群组", result.Message);
        Assert.Equal(2, _ctx.GroupMembers.All.Count);
    }
}

public class GroupMemberManagementTests
{
    private readonly ApplicationTestContext _ctx = new();

    [Fact]
    public async Task 群主踢出普通成员()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        var result = await new KickGroupMemberHandler(_ctx.GroupMembers, _ctx.Events, _ctx.Clock).Handle(
            new KickGroupMemberCommand
            {
                GroupId = group.Id, OperatorId = owner.Id, TargetUserId = member.Id
            }, default);

        Assert.Equal("已踢出成员", result.Message);
        Assert.False(await _ctx.GroupMembers.ExistsAsync(group.Id, member.Id));
    }

    [Fact]
    public async Task 被踢出群后收到通知_客户端据此退出会话()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        // GroupMemberRemoved 此前从未被 Raise：被踢的人客户端里那个群会一直留着
        _ctx.Events.Subscribe(new NotifyOnGroupMemberRemoved(_ctx.Notifier));

        await new KickGroupMemberHandler(_ctx.GroupMembers, _ctx.Events, _ctx.Clock).Handle(
            new KickGroupMemberCommand
            {
                GroupId = group.Id, OperatorId = owner.Id, TargetUserId = member.Id
            }, default);

        var push = Assert.Single(_ctx.Notifier.OfKind(PushKind.GroupMemberRemoved));
        Assert.Equal(member.Id, push.ToUserId);
        Assert.Equal(group.Id, push.ContextId);
    }

    [Fact]
    public async Task 踢人被拒时不发通知()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var admin = _ctx.GivenUser("管理员", "a@test.local");
        var group = _ctx.GivenGroup(owner.Id, admin.Id);
        var adminMember = await _ctx.GroupMembers.FindAsync(group.Id, admin.Id);
        adminMember!.ChangeAdminRole(true);
        await _ctx.GroupMembers.UpdateAsync(adminMember);

        _ctx.Events.Subscribe(new NotifyOnGroupMemberRemoved(_ctx.Notifier));

        await Assert.ThrowsAsync<DomainException>(
            () => new KickGroupMemberHandler(_ctx.GroupMembers, _ctx.Events, _ctx.Clock).Handle(
                new KickGroupMemberCommand
                {
                    GroupId = group.Id, OperatorId = admin.Id, TargetUserId = owner.Id
                }, default));

        Assert.Empty(_ctx.Notifier.OfKind(PushKind.GroupMemberRemoved));
    }

    [Fact]
    public async Task 不能踢群主()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var admin = _ctx.GivenUser("管理员", "a@test.local");
        var group = _ctx.GivenGroup(owner.Id, admin.Id);
        var adminMember = await _ctx.GroupMembers.FindAsync(group.Id, admin.Id);
        adminMember!.ChangeAdminRole(true);
        await _ctx.GroupMembers.UpdateAsync(adminMember);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new KickGroupMemberHandler(_ctx.GroupMembers, _ctx.Events, _ctx.Clock).Handle(
                new KickGroupMemberCommand
                {
                    GroupId = group.Id, OperatorId = admin.Id, TargetUserId = owner.Id
                }, default));

        Assert.Equal("不能踢出群主", ex.Message);
    }

    [Fact]
    public async Task 管理员不能踢其他管理员()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var admin1 = _ctx.GivenUser("管理1", "a1@test.local");
        var admin2 = _ctx.GivenUser("管理2", "a2@test.local");
        var group = _ctx.GivenGroup(owner.Id, admin1.Id, admin2.Id);

        foreach (var id in new[] { admin1.Id, admin2.Id })
        {
            var m = await _ctx.GroupMembers.FindAsync(group.Id, id);
            m!.ChangeAdminRole(true);
            await _ctx.GroupMembers.UpdateAsync(m);
        }

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new KickGroupMemberHandler(_ctx.GroupMembers, _ctx.Events, _ctx.Clock).Handle(
                new KickGroupMemberCommand
                {
                    GroupId = group.Id, OperatorId = admin1.Id, TargetUserId = admin2.Id
                }, default));

        Assert.Equal("管理员不能踢出其他管理员", ex.Message);
    }

    [Fact]
    public async Task 目标不是群成员时抛出()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => new KickGroupMemberHandler(_ctx.GroupMembers, _ctx.Events, _ctx.Clock).Handle(
                new KickGroupMemberCommand
                {
                    GroupId = group.Id, OperatorId = owner.Id, TargetUserId = 99999
                }, default));

        Assert.Equal("目标用户不是群成员", ex.Message);
    }

    [Fact]
    public async Task 群主设置与取消管理员()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);
        var handler = new SetGroupAdminHandler(_ctx.Groups, _ctx.GroupMembers);

        var promoted = await handler.Handle(new SetGroupAdminCommand
        {
            GroupId = group.Id, OperatorId = owner.Id, TargetUserId = member.Id, IsAdmin = true
        }, default);
        Assert.Equal("已设为管理员", promoted.Message);
        Assert.Equal(GroupRole.Admin, (await _ctx.GroupMembers.FindAsync(group.Id, member.Id))!.Role);

        var demoted = await handler.Handle(new SetGroupAdminCommand
        {
            GroupId = group.Id, OperatorId = owner.Id, TargetUserId = member.Id, IsAdmin = false
        }, default);
        Assert.Equal("已取消管理员", demoted.Message);
        Assert.Equal(GroupRole.Member, (await _ctx.GroupMembers.FindAsync(group.Id, member.Id))!.Role);
    }

    [Fact]
    public async Task 非群主不能设置管理员()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new SetGroupAdminHandler(_ctx.Groups, _ctx.GroupMembers).Handle(
                new SetGroupAdminCommand
                {
                    GroupId = group.Id, OperatorId = member.Id, TargetUserId = owner.Id, IsAdmin = true
                }, default));

        Assert.Equal("只有群主可以设置管理员", ex.Message);
    }

    [Fact]
    public async Task 不能通过设管理员改群主身份()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new SetGroupAdminHandler(_ctx.Groups, _ctx.GroupMembers).Handle(
                new SetGroupAdminCommand
                {
                    GroupId = group.Id, OperatorId = owner.Id, TargetUserId = owner.Id, IsAdmin = false
                }, default));

        Assert.Equal("不能修改群主的身份", ex.Message);
    }
}

public class GroupAnnouncementUseCaseTests
{
    private readonly ApplicationTestContext _ctx = new();

    private SetGroupAnnouncementHandler Handler()
        => new(_ctx.Groups, _ctx.GroupMembers, _ctx.Events, _ctx.Clock);

    [Fact]
    public async Task 群主设置公告成功()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var result = await Handler().Handle(new SetGroupAnnouncementCommand
        {
            GroupId = group.Id, OperatorId = owner.Id, Announcement = "欢迎加入"
        }, default);

        Assert.Equal("公告已更新", result.Message);
        var stored = await _ctx.Groups.FindByIdAsync(group.Id);
        Assert.Equal("欢迎加入", stored!.Announcement!.Text);
        Assert.Equal(owner.Id, stored.Announcement.EditedBy);
    }

    [Fact]
    public async Task 清除公告时三列一并置空()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);
        await Handler().Handle(new SetGroupAnnouncementCommand
        {
            GroupId = group.Id, OperatorId = owner.Id, Announcement = "旧公告"
        }, default);

        var result = await Handler().Handle(new SetGroupAnnouncementCommand
        {
            GroupId = group.Id, OperatorId = owner.Id, Announcement = ""
        }, default);

        Assert.Equal("公告已清除", result.Message);
        var stored = await _ctx.Groups.FindByIdAsync(group.Id);
        Assert.Null(stored!.AnnouncementText);
        Assert.Null(stored.AnnouncementAt);
        Assert.Null(stored.AnnouncementBy);
    }

    [Fact]
    public async Task 普通成员不能设置公告()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SetGroupAnnouncementCommand
            {
                GroupId = group.Id, OperatorId = member.Id, Announcement = "x"
            }, default));

        Assert.Equal("只有群主或管理员可以设置公告", ex.Message);
    }

    [Fact]
    public async Task 非成员不能设置公告()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(() => Handler().Handle(
            new SetGroupAnnouncementCommand
            {
                GroupId = group.Id, OperatorId = 99999, Announcement = "x"
            }, default));

        Assert.Equal("你不是该群组成员", ex.Message);
    }

    [Fact]
    public async Task 公告变更发出事件()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        await Handler().Handle(new SetGroupAnnouncementCommand
        {
            GroupId = group.Id, OperatorId = owner.Id, Announcement = "内容"
        }, default);

        var e = _ctx.Events.SingleEvent<GroupAnnouncementChanged>();
        Assert.True(e.HasAnnouncement);
        Assert.Equal(owner.Id, e.EditedBy);
    }
}

public class GroupQueryTests
{
    private readonly ApplicationTestContext _ctx = new();

    [Fact]
    public async Task 我的群列表含成员数与我的角色()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        var result = await new GetMyGroupsHandler(_ctx.Groups, _ctx.GroupMembers).Handle(
            new GetMyGroupsQuery { UserId = member.Id }, default);

        var info = Assert.Single(result.Data!);
        Assert.Equal(group.Id, info.Id);
        Assert.Equal(2, info.MemberCount);
        Assert.Equal(2, info.MyRole);      // 普通成员
    }

    [Fact]
    public async Task 未加入任何群时返回空列表()
    {
        var user = _ctx.GivenUser();

        var result = await new GetMyGroupsHandler(_ctx.Groups, _ctx.GroupMembers).Handle(
            new GetMyGroupsQuery { UserId = user.Id }, default);

        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task 群列表带公告信息()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);
        group.SetAnnouncement("公告内容", owner.Id, _ctx.Now);
        await _ctx.Groups.UpdateAsync(group);

        var result = await new GetMyGroupsHandler(_ctx.Groups, _ctx.GroupMembers).Handle(
            new GetMyGroupsQuery { UserId = owner.Id }, default);

        var info = Assert.Single(result.Data!);
        Assert.Equal("公告内容", info.Announcement);
        Assert.NotNull(info.AnnouncementAt);
    }

    [Fact]
    public async Task 成员列表按角色排序_群主在前()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        var result = await new GetGroupMembersHandler(_ctx.GroupMembers, _ctx.Users, _ctx.Presence)
            .Handle(new GetGroupMembersQuery { GroupId = group.Id, RequesterId = owner.Id }, default);

        Assert.Equal(2, result.Data!.Count);
        Assert.Equal(0, result.Data[0].Role);
        Assert.Equal(owner.Id, result.Data[0].UserId);
    }

    [Fact]
    public async Task 非成员不能查看群成员名单()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var outsider = _ctx.GivenUser("路人", "x@test.local");
        var group = _ctx.GivenGroup(owner.Id);

        // 名单含昵称/头像/角色/在线状态，属于群内可见信息
        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new GetGroupMembersHandler(_ctx.GroupMembers, _ctx.Users, _ctx.Presence)
                .Handle(new GetGroupMembersQuery { GroupId = group.Id, RequesterId = outsider.Id }, default));

        Assert.Equal("你不是该群成员", ex.Message);
    }

    [Fact]
    public async Task 非成员不能查看群内机器人()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var outsider = _ctx.GivenUser("路人", "x@test.local");
        var (robot, _) = _ctx.GivenRobot(owner.Id);
        var group = _ctx.GivenGroup(owner.Id, robot.UserId);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new GetGroupRobotsHandler(_ctx.GroupMembers, _ctx.Robots)
                .Handle(new GetGroupRobotsQuery { GroupId = group.Id, RequesterId = outsider.Id }, default));

        Assert.Equal("你不是该群成员", ex.Message);
    }

    [Fact]
    public async Task 群成员可以查看群内机器人()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var (robot, _) = _ctx.GivenRobot(owner.Id);
        var group = _ctx.GivenGroup(owner.Id, robot.UserId);

        var result = await new GetGroupRobotsHandler(_ctx.GroupMembers, _ctx.Robots)
            .Handle(new GetGroupRobotsQuery { GroupId = group.Id, RequesterId = owner.Id }, default);

        Assert.True(result.Success);
        Assert.Contains(result.Data!, r => r.UserId == robot.UserId);
    }

    [Fact]
    public async Task 成员列表含在线状态与机器人标记()
    {
        var owner = _ctx.GivenUser();
        var (_, botUser) = _ctx.GivenRobot(owner.Id);
        var group = _ctx.GivenGroup(owner.Id, botUser.Id);
        _ctx.Presence.SetOnline(owner.Id);

        var result = await new GetGroupMembersHandler(_ctx.GroupMembers, _ctx.Users, _ctx.Presence)
            .Handle(new GetGroupMembersQuery { GroupId = group.Id, RequesterId = owner.Id }, default);

        var members = result.Data!;
        Assert.True(members.Single(m => m.UserId == owner.Id).IsOnline);
        Assert.True(members.Single(m => m.UserId == botUser.Id).IsBot);
    }
}

public class SetGroupJoinPolicyTests
{
    private readonly ApplicationTestContext _ctx = new();

    private SetGroupJoinPolicyHandler Handler()
        => new(_ctx.Groups, _ctx.GroupMembers, _ctx.Events, _ctx.Clock);

    [Fact]
    public async Task 群主可开放加入()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenGroup(owner.Id);

        var result = await Handler().Handle(new SetGroupJoinPolicyCommand
        {
            GroupId = group.Id, OperatorId = owner.Id, OpenToJoin = true
        }, default);

        Assert.Equal("已允许任何人加入", result.Message);
        Assert.True((await _ctx.Groups.GetRequiredAsync(group.Id))!.IsOpenToJoin);
    }

    [Fact]
    public async Task 管理员可开放加入()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var admin = _ctx.GivenUser("管理员", "a@test.local");
        var group = _ctx.GivenGroup(owner.Id, admin.Id);

        var adminMember = await _ctx.GroupMembers.GetRequiredAsync(group.Id, admin.Id);
        adminMember.ChangeAdminRole(true);
        await _ctx.GroupMembers.UpdateAsync(adminMember);

        var result = await Handler().Handle(new SetGroupJoinPolicyCommand
        {
            GroupId = group.Id, OperatorId = admin.Id, OpenToJoin = true
        }, default);

        Assert.Equal("已允许任何人加入", result.Message);
    }

    [Fact]
    public async Task 普通成员不能修改入群方式()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new SetGroupJoinPolicyCommand
            {
                GroupId = group.Id, OperatorId = member.Id, OpenToJoin = true
            }, default));

        Assert.Equal("只有群主或管理员可以修改入群方式", ex.Message);
    }

    [Fact]
    public async Task 非成员不能修改入群方式()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var outsider = _ctx.GivenUser("路人", "x@test.local");
        var group = _ctx.GivenGroup(owner.Id);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => Handler().Handle(
            new SetGroupJoinPolicyCommand
            {
                GroupId = group.Id, OperatorId = outsider.Id, OpenToJoin = true
            }, default));
    }

    [Fact]
    public async Task 开放后回收仅限邀请()
    {
        var owner = _ctx.GivenUser();
        var group = _ctx.GivenOpenGroup(owner.Id);

        var result = await Handler().Handle(new SetGroupJoinPolicyCommand
        {
            GroupId = group.Id, OperatorId = owner.Id, OpenToJoin = false
        }, default);

        Assert.Equal("已改为仅限邀请加入", result.Message);
        Assert.False((await _ctx.Groups.GetRequiredAsync(group.Id))!.IsOpenToJoin);
    }
}
