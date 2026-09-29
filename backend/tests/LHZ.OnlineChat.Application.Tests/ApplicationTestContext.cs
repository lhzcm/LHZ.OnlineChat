using LHZ.OnlineChat.Application.Tests.TestDoubles;
using LHZ.OnlineChat.Domain.Users;

namespace LHZ.OnlineChat.Application.Tests;

/// <summary>
/// 用例测试的共享上下文：把全部内存仓储与端口替身装在一起。
/// 每个测试新建一份，彼此隔离；需要预置数据时用 Given* 系列方法。
/// </summary>
internal sealed class ApplicationTestContext
{
    internal FakeClock Clock { get; } = new();

    internal InMemoryUserRepository Users { get; } = new();

    internal InMemoryFriendshipRepository Friendships { get; } = new();

    internal InMemoryFriendSettingRepository FriendSettings { get; } = new();

    internal InMemoryBlacklistRepository Blacklist { get; } = new();

    internal InMemoryGroupRepository Groups { get; } = new();

    internal InMemoryGroupMemberRepository GroupMembers { get; } = new();

    internal InMemoryPrivateMessageRepository PrivateMessages { get; } = new();

    internal InMemoryGroupMessageRepository GroupMessages { get; } = new();

    internal InMemorySessionSettingRepository SessionSettings { get; } = new();

    internal InMemoryRobotRepository Robots { get; } = new();

    internal InMemoryAdminRepository Admins { get; } = new();

    internal InMemoryAdminAuditLogRepository AuditLogs { get; } = new();

    internal FakePasswordHasher Hasher { get; } = new();

    internal FakeTokenIssuer Tokens { get; } = new();

    internal FakeSessionStore Sessions { get; } = new();

    internal FakeSessionTerminator Terminator { get; }

    internal FakeVerificationCodeStore Codes { get; } = new();

    internal FakeEmailSender Email { get; } = new();

    internal FakePresenceStore Presence { get; } = new();

    internal FakeRecentMessageCache Cache { get; } = new();

    internal FakeFileStorage Files { get; } = new();

    internal FakeWebhookDispatcher Webhooks { get; } = new();

    internal FakeWebhookSigner Signer { get; } = new();

    internal FakeRobotTokenCipher Cipher { get; } = new();

    internal FakeMuteMessageFormatter MuteFormatter { get; } = new();

    internal FakeAuditLogger Audit { get; } = new();

    internal FakeCurrentUser CurrentUser { get; } = new();

    internal FakeConnectionRegistry Connections { get; } = new();

    internal RecordingRealtimeNotifier Notifier { get; } = new();

    internal RecordingEventDispatcher Events { get; } = new();

    internal FakeRobotConversationService RobotConversations { get; } = new();

    internal ApplicationTestContext() => Terminator = new FakeSessionTerminator(Sessions);

    internal DateTime Now => Clock.UtcNow;

    // ==================== 预置数据 ====================

    /// <summary>预置一个普通用户（口令统一为 pass123456）</summary>
    internal User GivenUser(string nickname = "张三", string email = "a@test.local")
    {
        var user = User.Register(
            nickname, Domain.Users.Email.Parse(email), FakePasswordHasher.Of("pass123456"), Now);
        Users.AddAsync(user).GetAwaiter().GetResult();
        user.DequeueDomainEvents();
        return user;
    }

    /// <summary>预置一个机器人账号 + 对应配置，并与创建者建立好友关系</summary>
    internal (Domain.Robots.Robot Robot, User BotUser) GivenRobot(
        int ownerId,
        string name = "助理",
        string webhook = "https://example.com/hook",
        string? secret = null,
        bool enabled = true)
    {
        var botUser = User.CreateBot(name, null, Now);
        Users.AddAsync(botUser).GetAwaiter().GetResult();
        botUser.DequeueDomainEvents();

        var robot = Domain.Robots.Robot.Create(
            ownerId, botUser.Id, name, null,
            Domain.Robots.WebhookUrl.Parse(webhook), secret, null, Now);
        Robots.AddAsync(robot).GetAwaiter().GetResult();
        robot.AssignToken(Cipher.Encode(robot.Id));
        if (!enabled) robot.SetEnabled(false);

        Friendships
            .AddAsync(Domain.Friends.Friendship.EstablishDirectly(ownerId, botUser.Id, Now))
            .GetAwaiter().GetResult();

        return (robot, botUser);
    }

    /// <summary>预置两个已成为好友的用户</summary>
    internal (User A, User B) GivenFriends()
    {
        var a = GivenUser("张三", "a@test.local");
        var b = GivenUser("李四", "b@test.local");
        Friendships
            .AddAsync(Domain.Friends.Friendship.EstablishDirectly(a.Id, b.Id, Now))
            .GetAwaiter().GetResult();
        return (a, b);
    }

    /// <summary>预置一个群（创建者为群主），可追加普通成员</summary>
    internal Domain.Groups.Group GivenGroup(int ownerId, params int[] memberIds)
    {
        var group = Domain.Groups.Group.Create("测试群", null, ownerId, Now);
        Groups.AddAsync(group).GetAwaiter().GetResult();
        group.DequeueDomainEvents();

        GroupMembers
            .AddAsync(Domain.Groups.GroupMember.CreateOwner(group.Id, ownerId, Now))
            .GetAwaiter().GetResult();

        foreach (var memberId in memberIds)
        {
            GroupMembers
                .AddAsync(Domain.Groups.GroupMember.Join(group.Id, memberId, 0, Now))
                .GetAwaiter().GetResult();
        }

        return group;
    }

    /// <summary>预置一个管理员</summary>
    internal Domain.Admins.Admin GivenAdmin(
        string username = "admin",
        Domain.Admins.AdminRole role = Domain.Admins.AdminRole.Super)
    {
        var admin = Domain.Admins.Admin.Create(
            username, FakePasswordHasher.Of("admin123456"), role, Now);
        Admins.AddAsync(admin).GetAwaiter().GetResult();
        return admin;
    }

    /// <summary>预置一条私聊消息</summary>
    internal Domain.Messaging.PrivateMessage GivenPrivateMessage(
        int senderId, int receiverId, string content = "内容", DateTime? sentAt = null)
    {
        var message = Domain.Messaging.PrivateMessage.Send(
            senderId, receiverId, content, Domain.Messaging.MessageKind.Text,
            null, null, sentAt ?? Now);
        PrivateMessages.AddAsync(message).GetAwaiter().GetResult();
        return message;
    }

    /// <summary>预置一条群消息</summary>
    internal Domain.Messaging.GroupMessage GivenGroupMessage(
        long groupId, int senderId, string content = "内容", DateTime? sentAt = null)
    {
        var message = Domain.Messaging.GroupMessage.Send(
            groupId, senderId, content, Domain.Messaging.MessageKind.Text,
            null, Domain.Messaging.MentionList.Empty, null, sentAt ?? Now);
        GroupMessages.AddAsync(message).GetAwaiter().GetResult();
        return message;
    }
}
