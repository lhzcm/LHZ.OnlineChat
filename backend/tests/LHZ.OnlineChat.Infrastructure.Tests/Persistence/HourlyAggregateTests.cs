using FreeSql;
using LHZ.OnlineChat.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging;

namespace LHZ.OnlineChat.Infrastructure.Tests.Persistence;

/// <summary>
/// 仪表盘小时分布聚合的失败路径。
///
/// 为什么测这个：聚合失败被有意设计成「不影响仪表盘其余部分」，
/// 于是失败唯一的表现就是日志。原实现写的是 Console.WriteLine ——
/// 没有级别、进不了结构化日志，等于这条失败路径实际上是隐形的。
/// 这里用不可达的连接串触发失败，断言它确实落到了 ILogger。
/// </summary>
public class HourlyAggregateTests
{
    /// <summary>
    /// 连接串本身不合法（端口不是数字）：连接一打开就立刻失败，
    /// 不会像指向不可达端口那样付一次网络超时 —— 单测不该为一次连接尝试等 2 秒。
    /// </summary>
    private static IFreeSql BuildUnreachableOrm()
        => new FreeSqlBuilder()
            .UseConnectionString(
                DataType.PostgreSQL,
                "Host=127.0.0.1;Port=not-a-port;Database=never_connected;Username=x;Password=x")
            .UseAutoSyncStructure(false)
            .Build();

    [Fact]
    public void 查询失败时返回空结果_并以Warning级别记入日志()
    {
        using var fsql = BuildUnreachableOrm();
        var logger = new RecordingLogger();

        var result = HourlyAggregate.Query(fsql, "GroupMessage", 24, logger);

        // 仪表盘其余部分必须照常渲染，所以这里只能是空字典而不是异常
        Assert.Empty(result);

        // 失败唯一的表现就是日志 —— 原实现写 Console.WriteLine，没有级别也进不了结构化日志
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        // 表名必须出现在消息里，否则两个聚合的失败分不清是哪个
        Assert.Contains("GroupMessage", entry.Message, StringComparison.Ordinal);
        Assert.NotNull(entry.Error);
    }

    private sealed class RecordingLogger : ILogger
    {
        internal List<(LogLevel Level, string Message, Exception? Error)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
