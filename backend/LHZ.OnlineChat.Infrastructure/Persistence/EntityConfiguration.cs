using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Blacklists;
using LHZ.OnlineChat.Domain.Friends;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Robots;
using LHZ.OnlineChat.Domain.Users;
using LHZ.OnlineChat.Infrastructure.Persistence.TypeHandlers;

namespace LHZ.OnlineChat.Infrastructure.Persistence;

/// <summary>
/// 领域实体 → 数据表的映射配置（FreeSql FluentApi）。
///
/// 改造前映射靠实体上的 [Table]/[Column] 特性，代价是领域模型必须引用 FreeSql；
/// 改用 FluentApi 后领域层零依赖，映射细节全部收敛在这一个文件里。
///
/// 表名与列名严格对齐既有库结构（线上有数据，不做结构性变更）：
///   User → "User_"、Friendship → "Friend"、FriendSetting → "FriendTag"、
///   Group → "Group_"、BlacklistEntry → "Blacklist"、Robot → "RobotProfile"、
///   AdminAuditLog → "AdminLog"
/// 几处属性名与列名不同的也在这里对齐（Kind → MessageType 等）。
/// </summary>
internal static class EntityConfiguration
{
    /// <summary>需要 CodeFirst 同步结构的全部实体</summary>
    internal static readonly Type[] AllEntityTypes =
    {
        typeof(User),
        typeof(Friendship),
        typeof(FriendSetting),
        typeof(Group),
        typeof(GroupMember),
        typeof(PrivateMessage),
        typeof(GroupMessage),
        typeof(SessionSetting),
        typeof(BlacklistEntry),
        typeof(Robot),
        typeof(Admin),
        typeof(AdminAuditLog)
    };

    internal static void Apply(IFreeSql fsql)
    {
        ValueObjectTypeHandlers.RegisterAll();

        ConfigureUsers(fsql);
        ConfigureFriends(fsql);
        ConfigureGroups(fsql);
        ConfigureMessaging(fsql);
        ConfigureRobots(fsql);
        ConfigureAdmins(fsql);
    }

    private static void ConfigureUsers(IFreeSql fsql)
    {
        fsql.CodeFirst.ConfigEntity<User>(e =>
        {
            e.Name("User_");
            e.Property(x => x.Id).IsPrimary(true).IsIdentity(true);
            e.Property(x => x.Email).IsNullable(true).StringLength(200);
            e.Property(x => x.PasswordHash).IsNullable(true).StringLength(200);
            e.Property(x => x.Nickname).IsNullable(false).StringLength(50);
            e.Property(x => x.Avatar).StringLength(500);
            e.Property(x => x.IsBot).IsNullable(false);
            e.Property(x => x.IsBanned).IsNullable(false);
            e.Property(x => x.BanReason).IsNullable(true).StringLength(500);
            e.Property(x => x.BannedAt).IsNullable(true);
            e.Property(x => x.CreatedAt).IsNullable(false);
            e.Property(x => x.UpdatedAt).IsNullable(false);
        });

        fsql.CodeFirst.ConfigEntity<BlacklistEntry>(e =>
        {
            e.Name("Blacklist");
            e.Property(x => x.Id).IsPrimary(true).IsIdentity(true);
            e.Property(x => x.UserId).IsNullable(false);
            e.Property(x => x.BlockedUserId).IsNullable(false);
            e.Property(x => x.CreatedAt).IsNullable(false);
        });
    }

    private static void ConfigureFriends(IFreeSql fsql)
    {
        fsql.CodeFirst.ConfigEntity<Friendship>(e =>
        {
            e.Name("Friend");
            e.Property(x => x.Id).IsPrimary(true).IsIdentity(true);
            e.Property(x => x.UserId).IsNullable(false);
            e.Property(x => x.FriendId).IsNullable(false);
            // 枚举按 int 存，数值与改造前的 0/1/2 一致
            e.Property(x => x.Status).MapType(typeof(int)).IsNullable(false);
            e.Property(x => x.CreatedAt).IsNullable(false);
        });

        fsql.CodeFirst.ConfigEntity<FriendSetting>(e =>
        {
            e.Name("FriendTag");
            e.Property(x => x.Id).IsPrimary(true).IsIdentity(true);
            e.Property(x => x.UserId).IsNullable(false);
            e.Property(x => x.FriendId).IsNullable(false);
            e.Property(x => x.Remark).StringLength(50);
            e.Property(x => x.Category).StringLength(30);
            e.Property(x => x.UpdatedAt).IsNullable(false);
        });
    }

    private static void ConfigureGroups(IFreeSql fsql)
    {
        fsql.CodeFirst.ConfigEntity<Group>(e =>
        {
            e.Name("Group_");
            e.Property(x => x.Id).IsPrimary(true).IsIdentity(true);
            e.Property(x => x.Name).IsNullable(false).StringLength(100);
            e.Property(x => x.Avatar).StringLength(500);
            e.Property(x => x.OwnerId).IsNullable(false);
            // 入群方式按 int 落库；存量行补列后取默认值 0（仅限邀请），即收紧而非放开
            e.Property(x => x.JoinPolicy).MapType(typeof(int)).IsNullable(false);
            // 公告三列：属性名 AnnouncementText 对应既有列名 Announcement
            e.Property(x => x.AnnouncementText).Name("Announcement").StringLength(2000);
            e.Property(x => x.AnnouncementAt).IsNullable(true);
            e.Property(x => x.AnnouncementBy).IsNullable(true);
            e.Property(x => x.CreatedAt).IsNullable(false);
        });

        fsql.CodeFirst.ConfigEntity<GroupMember>(e =>
        {
            e.Name("GroupMember");
            e.Property(x => x.Id).IsPrimary(true).IsIdentity(true);
            e.Property(x => x.GroupId).IsNullable(false);
            e.Property(x => x.UserId).IsNullable(false);
            e.Property(x => x.Role).MapType(typeof(int)).IsNullable(false);
            e.Property(x => x.LastReadMessageId).IsNullable(false);
            e.Property(x => x.MutedUntil).IsNullable(true);
            e.Property(x => x.JoinedAt).IsNullable(false);
        });
    }

    private static void ConfigureMessaging(IFreeSql fsql)
    {
        fsql.CodeFirst.ConfigEntity<PrivateMessage>(e =>
        {
            e.Name("PrivateMessage");
            e.Property(x => x.Id).IsPrimary(true).IsIdentity(true);
            e.Property(x => x.SenderId).IsNullable(false);
            e.Property(x => x.ReceiverId).IsNullable(false);
            e.Property(x => x.ClientMessageId).StringLength(64);
            // StringLength(-2) = text，长内容不截断
            e.Property(x => x.Content).IsNullable(false).StringLength(-2);
            e.Property(x => x.Kind).Name("MessageType").MapType(typeof(int)).IsNullable(false);
            e.Property(x => x.IsRead).IsNullable(false);
            e.Property(x => x.IsDeleted).IsNullable(false);
            e.Property(x => x.ReplyMessageId).StringLength(64);
            e.Property(x => x.ReplyContent).StringLength(200);
            e.Property(x => x.ReplySenderName).StringLength(50);
            e.Property(x => x.SentAt).IsNullable(false);
        });

        fsql.CodeFirst.ConfigEntity<GroupMessage>(e =>
        {
            e.Name("GroupMessage");
            e.Property(x => x.Id).IsPrimary(true).IsIdentity(true);
            e.Property(x => x.GroupId).IsNullable(false);
            e.Property(x => x.SenderId).IsNullable(false);
            e.Property(x => x.ClientMessageId).StringLength(64);
            e.Property(x => x.Mentions).StringLength(500);
            e.Property(x => x.Content).IsNullable(false).StringLength(-2);
            e.Property(x => x.Kind).Name("MessageType").MapType(typeof(int)).IsNullable(false);
            e.Property(x => x.IsDeleted).IsNullable(false);
            e.Property(x => x.ReplyMessageId).StringLength(64);
            e.Property(x => x.ReplyContent).StringLength(200);
            e.Property(x => x.ReplySenderName).StringLength(50);
            e.Property(x => x.SentAt).IsNullable(false);
        });

        fsql.CodeFirst.ConfigEntity<SessionSetting>(e =>
        {
            e.Name("SessionSetting");
            e.Property(x => x.Id).IsPrimary(true).IsIdentity(true);
            e.Property(x => x.UserId).IsNullable(false);
            e.Property(x => x.SessionType).IsNullable(false).StringLength(10);
            e.Property(x => x.SessionId).IsNullable(false);
            e.Property(x => x.IsPinned).IsNullable(false);
            e.Property(x => x.Muted).IsNullable(false);
            e.Property(x => x.UpdatedAt).IsNullable(false);
        });
    }

    private static void ConfigureRobots(IFreeSql fsql)
    {
        fsql.CodeFirst.ConfigEntity<Robot>(e =>
        {
            e.Name("RobotProfile");
            e.Property(x => x.Id).IsPrimary(true).IsIdentity(true);
            e.Property(x => x.UserId).IsNullable(false);
            e.Property(x => x.OwnerId).IsNullable(false);
            e.Property(x => x.Name).IsNullable(false).StringLength(50);
            e.Property(x => x.Avatar).StringLength(500);
            // 属性名 WebhookUrlValue 对应既有列名 WebhookUrl
            e.Property(x => x.WebhookUrlValue).Name("WebhookUrl").IsNullable(false).StringLength(500);
            e.Property(x => x.WebhookSecret).StringLength(200);
            e.Property(x => x.TimeoutMs).IsNullable(false);
            e.Property(x => x.Token).StringLength(100);
            e.Property(x => x.Enabled).IsNullable(false);
            e.Property(x => x.PushCount).IsNullable(false);
            e.Property(x => x.CallbackFailCount).IsNullable(false);
            e.Property(x => x.CreatedAt).IsNullable(false);
        });
    }

    private static void ConfigureAdmins(IFreeSql fsql)
    {
        // Admin / AdminLog 的字符串列在改造前既没声明 StringLength 也没声明 IsNullable，
        // 走的是 FreeSql 默认（varchar(255)、可空）。这里刻意也不声明：
        //   - 声明更短的长度 → SyncStructure 会 ALTER 缩列，存量数据可能超长而失败
        //   - 声明 IsNullable(false) → 会 ALTER SET NOT NULL，存量若有 NULL 则启动直接崩
        // 非空本就由领域模型保证（属性都是非可空 string 且构造时必填），
        // 不值得为此在启动路径上引入一次可能失败的 DDL。
        fsql.CodeFirst.ConfigEntity<Admin>(e =>
        {
            e.Name("Admin");
            e.Property(x => x.Id).IsPrimary(true).IsIdentity(true);
            e.Property(x => x.Role).MapType(typeof(int)).IsNullable(false);
            e.Property(x => x.Status).MapType(typeof(int)).IsNullable(false);
            e.Property(x => x.CreatedAt).IsNullable(false);
            e.Property(x => x.LastLoginAt).IsNullable(true);
        });

        fsql.CodeFirst.ConfigEntity<AdminAuditLog>(e =>
        {
            e.Name("AdminLog");
            e.Property(x => x.Id).IsPrimary(true).IsIdentity(true);
            e.Property(x => x.AdminId).IsNullable(false);
            e.Property(x => x.CreatedAt).IsNullable(false);
        });
    }
}
