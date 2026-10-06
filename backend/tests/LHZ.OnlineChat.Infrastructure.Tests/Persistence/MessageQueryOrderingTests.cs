using FreeSql;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Infrastructure.Persistence;

namespace LHZ.OnlineChat.Infrastructure.Tests.Persistence;

/// <summary>
/// 消息查询的 SQL 形状（只生成 SQL，不连数据库）。
///
/// 为什么单独测这个：分页排序是否稳定属于 SQL 语义 ——
/// 用例测试跑在内存仓储上，这类缺陷在那里永远不会失败。
/// FreeSql 的 ToSql() 不需要真实连接，因此可以把前提钉在这里。
///
/// 同时这也验证了「多次调用 OrderBy 会追加而不是覆盖」这一 FreeSql 行为：
/// 如果它其实是覆盖，第二次调用会把 SentAt 丢掉，这条断言就会失败。
/// </summary>
public class MessageQueryOrderingTests
{
    [Fact]
    public void 私聊分页按时间与ID双列排序()
    {
        using var fsql = BuildOrm();

        var sql = fsql.Select<PrivateMessage>()
            .Where(m => (m.SenderId == 1 && m.ReceiverId == 2)
                        || (m.SenderId == 2 && m.ReceiverId == 1))
            .OrderByDescending(m => m.SentAt)
            .OrderByDescending(m => m.Id)
            .Skip(0)
            .Take(20)
            .ToSql();

        Assert.Contains("\"SentAt\" DESC", sql);
        // 同一毫秒写入的多条消息（机器人批量推送、并发发送）若没有次级排序，
        // OFFSET 分页的顺序由数据库自由决定，翻页会重复或漏行
        Assert.Contains("\"Id\" DESC", sql);
    }

    [Fact]
    public void 群消息分页按时间与ID双列排序()
    {
        using var fsql = BuildOrm();

        var sql = fsql.Select<GroupMessage>()
            .Where(m => m.GroupId == 1)
            .OrderByDescending(m => m.SentAt)
            .OrderByDescending(m => m.Id)
            .Skip(20)
            .Take(20)
            .ToSql();

        Assert.Contains("\"SentAt\" DESC", sql);
        Assert.Contains("\"Id\" DESC", sql);
    }

    [Fact]
    public void 群离线补发按时间与ID升序()
    {
        using var fsql = BuildOrm();

        var sql = fsql.Select<GroupMessage>()
            .Where(m => m.GroupId == 1 && m.Id > 10 && !m.IsDeleted)
            .OrderBy(m => m.SentAt)
            .OrderBy(m => m.Id)
            .Take(100)
            .ToSql();

        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"SentAt\"", sql);
        Assert.Contains("\"Id\"", sql);
        // 补发必须排除已撤回的消息，计数同口径由仓储里的单一过滤表达式保证
        Assert.Contains("IsDeleted", sql);
    }

    /// <summary>映射配置与真实环境一致（列名、表名），并关闭自动建表</summary>
    private static IFreeSql BuildOrm()
    {
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.PostgreSQL,
                "Host=127.0.0.1;Port=1;Database=never_connected;Username=x;Password=x")
            .UseAutoSyncStructure(false)
            .Build();

        EntityConfiguration.Apply(fsql);
        return fsql;
    }
}
