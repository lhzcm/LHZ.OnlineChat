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
