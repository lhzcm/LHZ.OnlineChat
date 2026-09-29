namespace LHZ.OnlineChat.Domain.Common;

/// <summary>
/// 领域事件标记接口。
/// 领域层只负责「发生了什么」，不关心谁来响应（推送 WS、写审计、发邮件都在应用层订阅）。
/// </summary>
public interface IDomainEvent
{
    /// <summary>事件发生时刻（UTC）</summary>
    DateTime OccurredAt { get; }
}

/// <summary>
/// 实体基类：以标识相等，而非属性相等。
/// Id 为 protected set —— 新建时为默认值，由持久化层回填自增主键。
/// </summary>
public abstract class Entity<TId> where TId : struct, IEquatable<TId>
{
    public TId Id { get; protected set; }

    /// <summary>
    /// 是否尚未持久化（Id 仍为默认值）。
    /// 只读计算属性不会被 FreeSql 当作数据列（已验证），可安全使用。
    /// </summary>
    public bool IsTransient => Id.Equals(default);

    /// <summary>
    /// 持久化层回填数据库生成的主键。
    ///
    /// 这是留给基础设施层的唯一接缝（经 InternalsVisibleTo 暴露，对业务代码不可见）：
    /// FreeSql 的 ExecuteIdentity / ExecuteInserted 都不会写回带非公开 setter 的 Id，
    /// 而 Id 又不应该开放 public set —— 否则业务代码可以随意篡改实体标识。
    /// </summary>
    internal void AssignPersistedId(TId id)
    {
        if (!IsTransient && !Id.Equals(id))
            throw new InvalidOperationException("实体标识已确定，不能重新赋值");
        Id = id;
    }

    public override bool Equals(object? obj)
        => obj is Entity<TId> other
           && other.GetType() == GetType()
           && !IsTransient && !other.IsTransient
           && Id.Equals(other.Id);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}

/// <summary>
/// 聚合根基类：一致性边界的入口，唯一可被仓储直接加载/保存的对象。
/// 领域事件用「私有字段 + 方法暴露」而非属性，避免被 ORM 误当作数据列。
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId> where TId : struct, IEquatable<TId>
{
    private readonly List<IDomainEvent> _domainEvents = new();

    /// <summary>取出并清空累积的领域事件（由应用层在持久化成功后派发）</summary>
    public IReadOnlyList<IDomainEvent> DequeueDomainEvents()
    {
        if (_domainEvents.Count == 0) return Array.Empty<IDomainEvent>();
        var snapshot = _domainEvents.ToArray();
        _domainEvents.Clear();
        return snapshot;
    }

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}

/// <summary>
/// 值对象基类：以「全部分量相等」判定相等，不可变。
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>参与相等判定的分量</summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other)
        => other is not null
           && other.GetType() == GetType()
           && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());

    public override bool Equals(object? obj) => Equals(obj as ValueObject);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var component in GetEqualityComponents()) hash.Add(component);
        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}

/// <summary>
/// 业务规则被违反。
/// 应用层管道统一捕获并转换为 ApiResponse.Fail(Message)，因此 Message 面向终端用户可读。
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }

    public DomainException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>条件不成立时抛出（守卫语句）</summary>
    public static void Ensure(bool condition, string message)
    {
        if (!condition) throw new DomainException(message);
    }
}
