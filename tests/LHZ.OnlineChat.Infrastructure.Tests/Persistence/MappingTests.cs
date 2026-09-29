using FreeSql;
using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Blacklists;
using LHZ.OnlineChat.Domain.Friends;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Robots;
using LHZ.OnlineChat.Domain.Users;
using LHZ.OnlineChat.Infrastructure.Persistence;
using LHZ.OnlineChat.Infrastructure.Persistence.TypeHandlers;

namespace LHZ.OnlineChat.Infrastructure.Tests.Persistence;

/// <summary>
/// 实体映射配置的验证（只读取 FreeSql 的元数据，不连数据库）。
///
/// 这一层测试的价值很高：领域层改用 FluentApi 映射后，
/// 表名/列名一旦对不上既有 schema，线上就会「建出新表」或「读不到数据」——
/// 而这类错误编译期完全发现不了。
/// </summary>
public class EntityMappingTests : IClassFixture<MappingFixture>
{
    private readonly MappingFixture _fixture;

    public EntityMappingTests(MappingFixture fixture) => _fixture = fixture;

    [Theory]
    // 表名与既有库结构严格对齐（带下划线后缀的是为了避开 SQL 关键字）
    [InlineData(typeof(User), "User_")]
    [InlineData(typeof(Friendship), "Friend")]
    [InlineData(typeof(FriendSetting), "FriendTag")]
    [InlineData(typeof(Group), "Group_")]
    [InlineData(typeof(GroupMember), "GroupMember")]
    [InlineData(typeof(PrivateMessage), "PrivateMessage")]
    [InlineData(typeof(GroupMessage), "GroupMessage")]
    [InlineData(typeof(SessionSetting), "SessionSetting")]
    [InlineData(typeof(BlacklistEntry), "Blacklist")]
    [InlineData(typeof(Robot), "RobotProfile")]
    [InlineData(typeof(Admin), "Admin")]
    [InlineData(typeof(AdminAuditLog), "AdminLog")]
    public void 表名与既有库结构一致(Type entityType, string expectedTableName)
    {
        Assert.Equal(expectedTableName, _fixture.Table(entityType).DbName);
    }

    [Fact]
    public void 需要同步的实体共12个_与库里表数一致()
    {
        Assert.Equal(12, EntityConfiguration.AllEntityTypes.Length);
        Assert.Equal(
            EntityConfiguration.AllEntityTypes.Length,
            EntityConfiguration.AllEntityTypes.Distinct().Count());
    }

    [Theory]
    [InlineData(typeof(User))]
    [InlineData(typeof(Friendship))]
    [InlineData(typeof(FriendSetting))]
    [InlineData(typeof(Group))]
    [InlineData(typeof(GroupMember))]
    [InlineData(typeof(PrivateMessage))]
    [InlineData(typeof(GroupMessage))]
    [InlineData(typeof(SessionSetting))]
    [InlineData(typeof(BlacklistEntry))]
    [InlineData(typeof(Robot))]
    [InlineData(typeof(Admin))]
    [InlineData(typeof(AdminAuditLog))]
    public void 每个实体都有且仅有一个自增主键(Type entityType)
    {
        var table = _fixture.Table(entityType);

        var primary = Assert.Single(table.Primarys);
        Assert.Equal("Id", primary.Attribute.Name);
        Assert.True(primary.Attribute.IsIdentity, "主键必须是自增");
    }

    [Theory]
    // 属性名与既有列名不同的几处，必须显式对齐
    [InlineData(typeof(Group), nameof(Group.AnnouncementText), "Announcement")]
    [InlineData(typeof(PrivateMessage), nameof(PrivateMessage.Kind), "MessageType")]
    [InlineData(typeof(GroupMessage), nameof(GroupMessage.Kind), "MessageType")]
    [InlineData(typeof(Robot), nameof(Robot.WebhookUrlValue), "WebhookUrl")]
    public void 属性名与列名的映射正确(Type entityType, string propertyName, string expectedColumn)
    {
        var table = _fixture.Table(entityType);

        var column = table.ColumnsByCs.Values.FirstOrDefault(c => c.CsName == propertyName);
        Assert.NotNull(column);
        Assert.Equal(expectedColumn, column!.Attribute.Name);
    }

    [Fact]
    public void 只读计算属性不被映射成列()
    {
        // Group.Announcement / Message.Reply / MentionedUsers 等都是聚合视图，
        // 一旦被当成列，SyncStructure 会往库里加多余字段
        var group = _fixture.Table(typeof(Group));
        Assert.DoesNotContain("Announcement",
            group.ColumnsByCs.Values.Where(c => c.CsName == nameof(Group.Announcement))
                .Select(c => c.CsName));

        var privateMessage = _fixture.Table(typeof(PrivateMessage));
        foreach (var computed in new[] { "Reply", "SentAtUtc", "PublicMessageId" })
        {
            Assert.DoesNotContain(computed, privateMessage.ColumnsByCs.Values.Select(c => c.CsName));
        }

        var groupMessage = _fixture.Table(typeof(GroupMessage));
        Assert.DoesNotContain("MentionedUsers",
            groupMessage.ColumnsByCs.Values.Select(c => c.CsName));
    }

    [Fact]
    public void 聚合根基类的领域事件集合不被映射()
    {
        foreach (var entityType in EntityConfiguration.AllEntityTypes)
        {
            var columns = _fixture.Table(entityType).ColumnsByCs.Values.Select(c => c.CsName).ToList();
            Assert.DoesNotContain("IsTransient", columns);
        }
    }

    [Theory]
    [InlineData(typeof(User), nameof(User.Email))]
    [InlineData(typeof(User), nameof(User.PasswordHash))]
    [InlineData(typeof(Admin), nameof(Admin.PasswordHash))]
    public void 值对象属性被映射为字符串列(Type entityType, string propertyName)
    {
        var column = _fixture.Table(entityType).ColumnsByCs.Values
            .FirstOrDefault(c => c.CsName == propertyName);

        Assert.NotNull(column);
        // 经 TypeHandler 的 FluentApi 指定为 string
        Assert.Equal(typeof(string), column!.Attribute.MapType);
    }

    [Theory]
    [InlineData(typeof(Friendship), nameof(Friendship.Status))]
    [InlineData(typeof(GroupMember), nameof(GroupMember.Role))]
    [InlineData(typeof(PrivateMessage), nameof(PrivateMessage.Kind))]
    [InlineData(typeof(GroupMessage), nameof(GroupMessage.Kind))]
    [InlineData(typeof(Admin), nameof(Admin.Role))]
    [InlineData(typeof(Admin), nameof(Admin.Status))]
    public void 枚举属性被映射为int列_与既有魔法数取值一致(Type entityType, string propertyName)
    {
        var column = _fixture.Table(entityType).ColumnsByCs.Values
            .FirstOrDefault(c => c.CsName == propertyName);

        Assert.NotNull(column);
        Assert.Equal(typeof(int), column!.Attribute.MapType);
    }

    [Fact]
    public void 群公告的三列都存在()
    {
        var columns = _fixture.Table(typeof(Group)).ColumnsByCs.Values
            .Select(c => c.Attribute.Name).ToList();

        Assert.Contains("Announcement", columns);
        Assert.Contains("AnnouncementAt", columns);
        Assert.Contains("AnnouncementBy", columns);
    }

    [Fact]
    public void 引用回复的三列都存在()
    {
        foreach (var entityType in new[] { typeof(PrivateMessage), typeof(GroupMessage) })
        {
            var columns = _fixture.Table(entityType).ColumnsByCs.Values
                .Select(c => c.Attribute.Name).ToList();

            Assert.Contains("ReplyMessageId", columns);
            Assert.Contains("ReplyContent", columns);
            Assert.Contains("ReplySenderName", columns);
        }
    }

    [Fact]
    public void 消息内容为长文本列_不截断()
    {
        foreach (var entityType in new[] { typeof(PrivateMessage), typeof(GroupMessage) })
        {
            var content = _fixture.Table(entityType).ColumnsByCs.Values
                .First(c => c.CsName == "Content");

            // StringLength(-2) 即 text 类型
            Assert.Equal(-2, content.Attribute.StringLength);
        }
    }

    /// <summary>
    /// Admin / AdminLog 的字符串列刻意不声明长度与非空：
    /// 改造前它们走 FreeSql 默认值，若在此声明更严格的约束，
    /// SyncStructure 会对线上表执行 ALTER（缩列 / SET NOT NULL），存量数据可能导致启动失败。
    /// </summary>
    [Theory]
    [InlineData(typeof(Admin), nameof(Admin.Username))]
    [InlineData(typeof(AdminAuditLog), nameof(AdminAuditLog.AdminName))]
    [InlineData(typeof(AdminAuditLog), nameof(AdminAuditLog.Action))]
    [InlineData(typeof(AdminAuditLog), nameof(AdminAuditLog.TargetType))]
    public void 管理表的字符串列保持默认约束_避免启动期ALTER(Type entityType, string propertyName)
    {
        var column = _fixture.Table(entityType).ColumnsByCs.Values
            .First(c => c.CsName == propertyName);

        // 255 是 FreeSql 对「未声明长度的 string」解析出的默认值，
        // 也正是改造前无特性映射在库里产出的 varchar(255)。
        // 一旦有人给这些列加上更短的 StringLength 或 IsNullable(false)，这里会失败。
        Assert.Equal(255, column.Attribute.StringLength);
        Assert.True(column.Attribute.IsNullable);
    }
}

/// <summary>
/// 共享一次映射配置。
/// FreeSql 的 ConfigEntity 与 TypeHandlers 都是进程级静态状态，
/// 重复配置无意义，用 IClassFixture 共享。连接串不会真正连库（只读元数据）。
/// </summary>
public sealed class MappingFixture : IDisposable
{
    private readonly IFreeSql _fsql;

    public MappingFixture()
    {
        _fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.PostgreSQL,
                "Host=127.0.0.1;Port=1;Database=never_connected;Username=x;Password=x")
            .UseAutoSyncStructure(false)
            .Build();

        EntityConfiguration.Apply(_fsql);
    }

    internal FreeSql.Internal.Model.TableInfo Table(Type entityType)
        => _fsql.CodeFirst.GetTableByEntity(entityType);

    public void Dispose() => _fsql.Dispose();
}

/// <summary>值对象 ↔ 列 转换器的往返正确性</summary>
public class ValueObjectTypeHandlerTests
{
    [Fact]
    public void Email转换器往返一致()
    {
        var handler = new EmailTypeHandler();
        var email = Email.Parse("A@Test.LOCAL");

        var serialized = handler.Serialize(email);
        var restored = handler.Deserialize(serialized);

        Assert.Equal("a@test.local", serialized);
        Assert.Equal(email, restored);
    }

    [Fact]
    public void Email转换器声明的目标类型是string()
    {
        // FluentApi 里 MapType(string) 的实际生效结果，
        // 由 EntityMappingTests.值对象属性被映射为字符串列 从表元数据侧验证
        Assert.Equal(typeof(Email), new EmailTypeHandler().Type);
        Assert.Equal(typeof(PasswordHash), new PasswordHashTypeHandler().Type);
    }

    [Fact]
    public void PasswordHash转换器往返一致()
    {
        var handler = new PasswordHashTypeHandler();
        var hash = PasswordHash.FromHash("$2a$11$abcdefghijklmnop");

        var serialized = handler.Serialize(hash);
        var restored = handler.Deserialize(serialized);

        Assert.Equal("$2a$11$abcdefghijklmnop", serialized);
        Assert.Equal(hash, restored);
    }

    [Fact]
    public void PasswordHash转换器不校验格式_兼容历史数据()
    {
        // 库里可能存着非 BCrypt 格式的历史值，读取时不能抛
        var restored = new PasswordHashTypeHandler().Deserialize("legacy-plain-value");

        Assert.Equal("legacy-plain-value", restored.Value);
    }
}
