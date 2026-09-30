using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Common;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LHZ.OnlineChat.Infrastructure.Persistence;

/// <summary>初始超级管理员配置（Admin 表为空时创建）</summary>
public sealed class AdminBootstrapOptions
{
    public string InitialUsername { get; set; } = string.Empty;

    public string InitialPassword { get; set; } = string.Empty;
}

/// <summary>
/// 启动期数据库准备工作。
/// 改造前这是 Program.cs 底部四个 static 方法（建库、账号 ID 迁移、搜索索引、初始超管），
/// 现在收拢成一个可测试的服务，由宿主在启动时调用一次。
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly IFreeSql _fsql;
    private readonly IAdminRepository _admins;
    private readonly IPasswordHasher _hasher;
    private readonly AdminBootstrapOptions _adminOptions;
    private readonly IClock _clock;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        IFreeSql fsql,
        IAdminRepository admins,
        IPasswordHasher hasher,
        AdminBootstrapOptions adminOptions,
        IClock clock,
        ILogger<DatabaseInitializer> logger)
    {
        _fsql = fsql;
        _admins = admins;
        _hasher = hasher;
        _adminOptions = adminOptions;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// 若目标数据库不存在则创建（连到默认的 postgres 库执行 CREATE DATABASE）。
    /// 必须在构建 IFreeSql 之前调用，因此是静态方法。
    /// </summary>
    public static void EnsureDatabaseExists(string connectionString, ILogger? logger = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var database = builder.Database;
        if (string.IsNullOrWhiteSpace(database)) return;

        builder.Database = "postgres";
        using var connection = new NpgsqlConnection(builder.ConnectionString);
        connection.Open();

        using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        exists.Parameters.AddWithValue("name", database);
        if (exists.ExecuteScalar() is not null) return;

        // 数据库名不能参数化，用引号包裹（名字来自本机配置，非外部输入）
        using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
        create.ExecuteNonQuery();
        logger?.LogInformation("数据库 {Database} 已创建", database);
    }

    /// <summary>建表 + 迁移 + 索引 + 初始超管</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        SyncStructure();
        EnsureAccountIdSchema();
        EnsureSearchIndexes();
        EnsureUniqueConstraints();
        EnsurePerformanceIndexes();
        await EnsureInitialAdminAsync(ct).ConfigureAwait(false);
    }

    /// <summary>CodeFirst 同步表结构</summary>
    private void SyncStructure()
    {
        _fsql.CodeFirst.SyncStructure(EntityConfiguration.AllEntityTypes);
        _logger.LogInformation("表结构已同步（{Count} 个实体）", EntityConfiguration.AllEntityTypes.Length);
    }

    /// <summary>
    /// 账号 ID 迁移（幂等）：用户 ID 相关列统一为 integer，
    /// User_ 序列起始值设为 ≥10000（账号从 10000 开始自增）。
    /// </summary>
    private void EnsureAccountIdSchema()
    {
        const string sql = """
            DO $$
            BEGIN
              -- 用户 ID 相关列从 bigint 降为 integer（数据均在 int 范围内）
              IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='User_' AND column_name='Id' AND data_type='bigint') THEN
                ALTER TABLE "User_" ALTER COLUMN "Id" TYPE integer;
              END IF;
              IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='User_' AND column_name='Username') THEN
                ALTER TABLE "User_" DROP COLUMN "Username";
              END IF;
              IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='Friend' AND column_name='UserId' AND data_type='bigint') THEN
                ALTER TABLE "Friend" ALTER COLUMN "UserId" TYPE integer, ALTER COLUMN "FriendId" TYPE integer;
              END IF;
              IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='GroupMember' AND column_name='UserId' AND data_type='bigint') THEN
                ALTER TABLE "GroupMember" ALTER COLUMN "UserId" TYPE integer;
              END IF;
              IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='Group_' AND column_name='OwnerId' AND data_type='bigint') THEN
                ALTER TABLE "Group_" ALTER COLUMN "OwnerId" TYPE integer;
              END IF;
              IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='PrivateMessage' AND column_name='SenderId' AND data_type='bigint') THEN
                ALTER TABLE "PrivateMessage" ALTER COLUMN "SenderId" TYPE integer, ALTER COLUMN "ReceiverId" TYPE integer;
              END IF;
              IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='GroupMessage' AND column_name='SenderId' AND data_type='bigint') THEN
                ALTER TABLE "GroupMessage" ALTER COLUMN "SenderId" TYPE integer;
              END IF;
            END $$;
            SELECT setval(pg_get_serial_sequence('"User_"', 'Id'),
                          GREATEST((SELECT COALESCE(MAX("Id"), 0) FROM "User_"), 9999));
            """;

        _fsql.Ado.ExecuteNonQuery(sql);
        _logger.LogInformation("账号 ID 迁移完成（int 类型，起始 10000 自增）");
    }

    /// <summary>
    /// pg_trgm（trigram）GIN 索引，让消息内容的 LIKE '%关键词%'（含中文）走索引。
    /// 创建失败不阻塞启动，搜索退化为全表扫描。
    /// </summary>
    private void EnsureSearchIndexes()
    {
        try
        {
            _fsql.Ado.ExecuteNonQuery("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            _fsql.Ado.ExecuteNonQuery(
                """CREATE INDEX IF NOT EXISTS "idx_privmsg_content_trgm" ON "PrivateMessage" USING gin ("Content" gin_trgm_ops);""");
            _fsql.Ado.ExecuteNonQuery(
                """CREATE INDEX IF NOT EXISTS "idx_grpmsg_content_trgm" ON "GroupMessage" USING gin ("Content" gin_trgm_ops);""");

            _logger.LogInformation("pg_trgm 搜索索引就绪（PrivateMessage / GroupMessage.Content）");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "pg_trgm 索引创建失败，消息搜索将退化为全表扫描");
        }
    }

    /// <summary>
    /// 唯一约束（幂等）。
    ///
    /// 所有这些位置应用层都已经「先查重再写入」，但那在并发下必然有窗口期：
    /// 两个注册请求可以同时通过邮箱查重，产出两个同邮箱账号 —— 之后按邮箱登录
    /// 只会命中其中一个，另一个账号永久无法登录也无法重置密码。
    /// 唯一索引是这类竞态的唯一可靠兜底，冲突由 FreeSqlUnitOfWork 翻译成友好提示。
    ///
    /// 存量库若已有重复数据，建索引会失败 —— 此时只告警不阻塞启动（否则整个服务起不来），
    /// 由运维按日志提示清理后重启。
    /// </summary>
    private void EnsureUniqueConstraints()
    {
        // 机器人账号没有邮箱，用部分索引把 NULL 排除在唯一性之外
        Execute(
            "邮箱唯一",
            """CREATE UNIQUE INDEX IF NOT EXISTS "ux_user_email" ON "User_" ("Email") WHERE "Email" IS NOT NULL;""",
            duplicatesPossible: true);

        Execute(
            "群成员不重复",
            """CREATE UNIQUE INDEX IF NOT EXISTS "ux_groupmember_group_user" ON "GroupMember" ("GroupId", "UserId");""",
            duplicatesPossible: true);

        Execute(
            "好友关系不重复",
            """CREATE UNIQUE INDEX IF NOT EXISTS "ux_friend_user_friend" ON "Friend" ("UserId", "FriendId");""",
            duplicatesPossible: true);

        Execute(
            "好友设置不重复",
            """CREATE UNIQUE INDEX IF NOT EXISTS "ux_friendtag_user_friend" ON "FriendTag" ("UserId", "FriendId");""",
            duplicatesPossible: true);

        Execute(
            "黑名单不重复",
            """CREATE UNIQUE INDEX IF NOT EXISTS "ux_blacklist_user_blocked" ON "Blacklist" ("UserId", "BlockedUserId");""",
            duplicatesPossible: true);

        Execute(
            "会话设置不重复",
            """CREATE UNIQUE INDEX IF NOT EXISTS "ux_sessionsetting_user_session" ON "SessionSetting" ("UserId", "SessionType", "SessionId");""",
            duplicatesPossible: true);

        Execute(
            "机器人账号一对一",
            """CREATE UNIQUE INDEX IF NOT EXISTS "ux_robotprofile_user" ON "RobotProfile" ("UserId");""",
            duplicatesPossible: true);

        Execute(
            "管理员用户名唯一",
            """CREATE UNIQUE INDEX IF NOT EXISTS "ux_admin_username" ON "Admin" ("Username");""",
            duplicatesPossible: true);
    }

    /// <summary>
    /// 热点查询的二级索引（幂等）。
    ///
    /// 改造前除主键和两个 trgm GIN 索引外没有任何二级索引，
    /// 而「每发一条群消息都要按 (GroupId, UserId) 查一次成员」这类调用在每条消息上都会发生，
    /// 数据量涨起来后全是顺序扫描。列顺序按实际谓词的最左前缀排。
    /// </summary>
    private void EnsurePerformanceIndexes()
    {
        var statements = new (string Purpose, string Sql)[]
        {
            // 私聊：会话历史双向查（两个 OR 分支各走一条索引）、时间倒序分页
            ("私聊按发送方",
                """CREATE INDEX IF NOT EXISTS "ix_privmsg_sender_receiver_sent" ON "PrivateMessage" ("SenderId", "ReceiverId", "SentAt" DESC);"""),
            ("私聊按接收方",
                """CREATE INDEX IF NOT EXISTS "ix_privmsg_receiver_sender_sent" ON "PrivateMessage" ("ReceiverId", "SenderId", "SentAt" DESC);"""),
            // 未读统计只关心未读行，部分索引比全量索引小得多
            ("私聊未读统计",
                """CREATE INDEX IF NOT EXISTS "ix_privmsg_unread" ON "PrivateMessage" ("ReceiverId", "SenderId") WHERE NOT "IsRead";"""),
            ("私聊按客户端消息号（撤回/去重）",
                """CREATE INDEX IF NOT EXISTS "ix_privmsg_clientid" ON "PrivateMessage" ("ClientMessageId") WHERE "ClientMessageId" IS NOT NULL;"""),
            ("私聊按时间（仪表盘统计）",
                """CREATE INDEX IF NOT EXISTS "ix_privmsg_sent" ON "PrivateMessage" ("SentAt");"""),

            // 群聊：历史分页与「已读游标之后」计数都是 (GroupId, Id) 最左前缀
            ("群聊按群+自增号",
                """CREATE INDEX IF NOT EXISTS "ix_grpmsg_group_id" ON "GroupMessage" ("GroupId", "Id" DESC);"""),
            ("群聊按发送者",
                """CREATE INDEX IF NOT EXISTS "ix_grpmsg_sender" ON "GroupMessage" ("SenderId");"""),
            ("群聊按客户端消息号（撤回/去重）",
                """CREATE INDEX IF NOT EXISTS "ix_grpmsg_clientid" ON "GroupMessage" ("ClientMessageId") WHERE "ClientMessageId" IS NOT NULL;"""),
            ("群聊按时间（仪表盘统计）",
                """CREATE INDEX IF NOT EXISTS "ix_grpmsg_sent" ON "GroupMessage" ("SentAt");"""),

            // 「我加入的群」；(GroupId, UserId) 方向已由唯一索引覆盖
            ("群成员按用户",
                """CREATE INDEX IF NOT EXISTS "ix_groupmember_user" ON "GroupMember" ("UserId");"""),

            // 好友：待确认申请查 FriendId，(UserId, FriendId) 方向已由唯一索引覆盖
            ("好友按被申请方",
                """CREATE INDEX IF NOT EXISTS "ix_friend_friend_status" ON "Friend" ("FriendId", "Status");"""),

            ("黑名单反查",
                """CREATE INDEX IF NOT EXISTS "ix_blacklist_blocked" ON "Blacklist" ("BlockedUserId");"""),
            ("我的机器人列表",
                """CREATE INDEX IF NOT EXISTS "ix_robotprofile_owner" ON "RobotProfile" ("OwnerId");"""),
            ("群按群主",
                """CREATE INDEX IF NOT EXISTS "ix_group_owner" ON "Group_" ("OwnerId");"""),
            ("审计日志按时间倒序",
                """CREATE INDEX IF NOT EXISTS "ix_adminlog_created" ON "AdminLog" ("CreatedAt" DESC);"""),
            ("审计日志按管理员",
                """CREATE INDEX IF NOT EXISTS "ix_adminlog_admin" ON "AdminLog" ("AdminId");"""),
        };

        foreach (var (purpose, sql) in statements) Execute(purpose, sql, duplicatesPossible: false);

        _logger.LogInformation("二级索引就绪（{Count} 条）", statements.Length);
    }

    /// <summary>
    /// 执行单条 DDL，失败只告警不抛 —— 一条索引建不出来不该让整个服务起不来。
    /// </summary>
    private void Execute(string purpose, string sql, bool duplicatesPossible)
    {
        try
        {
            _fsql.Ado.ExecuteNonQuery(sql);
        }
        catch (Exception ex)
        {
            if (duplicatesPossible)
            {
                _logger.LogWarning(
                    ex,
                    "唯一索引「{Purpose}」创建失败，很可能是存量数据已有重复。"
                    + "清理重复行后重启即可生效；在此之前该约束不起作用（并发下仍可能产生重复数据）",
                    purpose);
            }
            else
            {
                _logger.LogWarning(ex, "索引「{Purpose}」创建失败，相关查询将退化为顺序扫描", purpose);
            }
        }
    }

    /// <summary>Admin 表为空且配置了初始账号时，创建首个超级管理员</summary>
    private async Task EnsureInitialAdminAsync(CancellationToken ct)
    {
        try
        {
            if (await _admins.AnyAsync(ct).ConfigureAwait(false)) return;

            var username = _adminOptions.InitialUsername?.Trim() ?? string.Empty;
            var password = _adminOptions.InitialPassword ?? string.Empty;

            if (username.Length == 0 || password.Length < 6)
            {
                _logger.LogWarning(
                    "未配置 Admin__InitialUsername/Admin__InitialPassword（或密码短于 6 位），跳过初始管理员创建");
                return;
            }

            var admin = Admin.Create(username, _hasher.Hash(password), AdminRole.Super, _clock.UtcNow);
            await _admins.AddAsync(admin, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "初始超级管理员已创建：{Username}（ID={Id}，管理后台 /admin）", username, admin.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "初始管理员创建失败");
        }
    }
}
