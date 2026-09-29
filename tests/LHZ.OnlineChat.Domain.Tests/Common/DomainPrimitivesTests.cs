using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Tests.Common;

public class EntityTests
{
    /// <summary>用具体子类验证基类语义（AggregateRoot 是抽象的）</summary>
    private sealed class Probe : AggregateRoot<int>
    {
        internal static Probe New() => new();

        internal void Emit(IDomainEvent e) => Raise(e);
    }

    private sealed class OtherProbe : AggregateRoot<int>
    {
    }

    private sealed record Ping(DateTime OccurredAt) : IDomainEvent;

    [Fact]
    public void IsTransient_未持久化时为真()
    {
        var entity = Probe.New();

        Assert.True(entity.IsTransient);
        Assert.Equal(0, entity.Id);
    }

    [Fact]
    public void AssignPersistedId_回填后不再是瞬态()
    {
        var entity = Probe.New();

        entity.AssignPersistedId(42);

        Assert.False(entity.IsTransient);
        Assert.Equal(42, entity.Id);
    }

    [Fact]
    public void AssignPersistedId_重复赋不同值时抛出()
    {
        var entity = Probe.New();
        entity.AssignPersistedId(42);

        var ex = Assert.Throws<InvalidOperationException>(() => entity.AssignPersistedId(43));

        Assert.Contains("不能重新赋值", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssignPersistedId_重复赋相同值时幂等()
    {
        var entity = Probe.New();
        entity.AssignPersistedId(42);

        entity.AssignPersistedId(42);

        Assert.Equal(42, entity.Id);
    }

    [Fact]
    public void Equals_相同类型同标识判定相等()
    {
        var a = Probe.New();
        var b = Probe.New();
        a.AssignPersistedId(7);
        b.AssignPersistedId(7);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_标识不同判定不等()
    {
        var a = Probe.New();
        var b = Probe.New();
        a.AssignPersistedId(7);
        b.AssignPersistedId(8);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equals_两个瞬态实体永不相等()
    {
        // 都还没有标识，不能因为"都是 0"就认为是同一个对象
        var a = Probe.New();
        var b = Probe.New();

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equals_标识相同但类型不同判定不等()
    {
        var a = Probe.New();
        var b = new OtherProbe();
        a.AssignPersistedId(7);
        b.AssignPersistedId(7);

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void DequeueDomainEvents_取出后清空()
    {
        var entity = Probe.New();
        entity.Emit(new Ping(T.Now));
        entity.Emit(new Ping(T.Now));

        var first = entity.DequeueDomainEvents();
        var second = entity.DequeueDomainEvents();

        Assert.Equal(2, first.Count);
        Assert.Empty(second);
    }

    [Fact]
    public void DequeueDomainEvents_无事件时返回空集合而非null()
    {
        var events = Probe.New().DequeueDomainEvents();

        Assert.NotNull(events);
        Assert.Empty(events);
    }

    [Fact]
    public void DequeueDomainEvents_返回快照_后续新增不影响已取出的集合()
    {
        var entity = Probe.New();
        entity.Emit(new Ping(T.Now));

        var snapshot = entity.DequeueDomainEvents();
        entity.Emit(new Ping(T.Now));

        Assert.Single(snapshot);
    }
}

public class ValueObjectTests
{
    private sealed class Point : ValueObject
    {
        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }

        public int Y { get; }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return X;
            yield return Y;
        }
    }

    private sealed class Tag : ValueObject
    {
        public Tag(int x) => X = x;

        public int X { get; }

        protected override IEnumerable<object?> GetEqualityComponents() { yield return X; }
    }

    [Fact]
    public void 分量全部相同则相等()
    {
        Assert.Equal(new Point(1, 2), new Point(1, 2));
        Assert.Equal(new Point(1, 2).GetHashCode(), new Point(1, 2).GetHashCode());
    }

    [Fact]
    public void 任一分量不同则不等()
    {
        Assert.NotEqual(new Point(1, 2), new Point(1, 3));
    }

    [Fact]
    public void 分量相同但类型不同则不等()
    {
        Assert.False(new Tag(1).Equals(new Point(1, 0)));
    }

    [Fact]
    public void 与null比较不相等()
    {
        Assert.False(new Point(1, 2).Equals(null));
    }

    [Fact]
    public void 相等运算符处理null()
    {
        Point? left = null;
        Point? right = null;

        Assert.True(left == right);
        Assert.False(left != right);
        Assert.False(new Point(1, 2) == null);
        Assert.True(new Point(1, 2) != null);
    }
}

public class DomainExceptionTests
{
    [Fact]
    public void Ensure_条件为真时不抛出()
    {
        DomainException.Ensure(true, "不该抛");
    }

    [Fact]
    public void Ensure_条件为假时抛出并携带面向用户的提示语()
    {
        var ex = Assert.Throws<DomainException>(() => DomainException.Ensure(false, "昵称不能为空"));

        Assert.Equal("昵称不能为空", ex.Message);
    }

    [Fact]
    public void 派生异常仍是DomainException_能被统一管道捕获()
    {
        // DomainExceptionBehavior 只 catch DomainException，
        // 派生类型必须落在同一个捕获范围内，否则会漏成 500
        Assert.IsAssignableFrom<DomainException>(new EntityNotFoundException("x"));
        Assert.IsAssignableFrom<DomainException>(new ForbiddenOperationException("x"));
    }
}

public class UtcTimeTests
{
    [Fact]
    public void Normalize_Unspecified补上UTC标识()
    {
        // 这是改造前的真实缺陷来源：Npgsql 读出的 Kind 是 Unspecified，
        // 序列化后缺少 Z 后缀，前端按本地时间解析会偏移 8 小时
        var fromDatabase = new DateTime(2026, 3, 14, 10, 0, 0, DateTimeKind.Unspecified);

        var normalized = UtcTime.Normalize(fromDatabase);

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(fromDatabase.Ticks, normalized.Ticks);
    }

    [Fact]
    public void Normalize_已是UTC时原样返回()
    {
        var normalized = UtcTime.Normalize(T.Now);

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(T.Now, normalized);
    }

    [Fact]
    public void Normalize_可空版本保留null()
    {
        Assert.Null(UtcTime.Normalize((DateTime?)null));
        Assert.Equal(DateTimeKind.Utc, UtcTime.Normalize((DateTime?)T.Now)!.Value.Kind);
    }

    [Fact]
    public void ToUnixMilliseconds_按UTC换算()
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 1, DateTimeKind.Utc);

        Assert.Equal(1000, UtcTime.ToUnixMilliseconds(epoch));
    }

    [Fact]
    public void ToUnixMilliseconds_Unspecified也按UTC换算_不引入时区偏移()
    {
        var unspecified = new DateTime(1970, 1, 1, 0, 0, 1, DateTimeKind.Unspecified);

        Assert.Equal(1000, UtcTime.ToUnixMilliseconds(unspecified));
    }
}

public class PageRequestTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(9, 9)]
    public void 页码下限夹紧到1(int input, int expected)
    {
        Assert.Equal(expected, new PageRequest(input, 20).Page);
    }

    [Theory]
    [InlineData(0, 20)]    // 0 视为未指定，回落默认值
    [InlineData(-1, 20)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(999, 50)]  // 超出上限被夹紧
    public void 页长按默认值与上限夹紧(int input, int expected)
    {
        Assert.Equal(expected, new PageRequest(1, input).PageSize);
    }

    [Fact]
    public void 可自定义页长上限()
    {
        Assert.Equal(100, new PageRequest(1, 999, maxPageSize: 100).PageSize);
    }

    [Fact]
    public void Skip按页码与页长计算()
    {
        Assert.Equal(0, new PageRequest(1, 20).Skip);
        Assert.Equal(40, new PageRequest(3, 20).Skip);
    }

    [Fact]
    public void TakeThroughCurrentPage_用于跨表合并分页()
    {
        // 两张表各取前 N 条再合并排序，N 必须覆盖到当前页末尾
        Assert.Equal(60, new PageRequest(3, 20).TakeThroughCurrentPage);
    }

    [Fact]
    public void 值语义相等()
    {
        Assert.Equal(new PageRequest(2, 30), new PageRequest(2, 30));
        Assert.True(new PageRequest(2, 30) == new PageRequest(2, 30));
        Assert.True(new PageRequest(2, 30) != new PageRequest(3, 30));
    }
}
