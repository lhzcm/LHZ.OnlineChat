using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Infrastructure.Persistence;

/// <summary>
/// 仓储实现的公共动作。
/// </summary>
internal static class FreeSqlExtensions
{
    /// <summary>
    /// 插入聚合并回填数据库生成的自增主键（int 主键）。
    ///
    /// 必须用 ExecuteIdentity 而不是 ExecuteAffrows：后者会把默认值 0 当作显式主键插入，破坏自增。
    /// FreeSql 不会自动写回带非公开 setter 的 Id，因此显式调用领域层的 AssignPersistedId 接缝。
    /// </summary>
    internal static async Task InsertWithIdentityAsync<TEntity>(
        this IFreeSql fsql, TEntity entity, CancellationToken ct)
        where TEntity : AggregateRoot<int>
    {
        var id = await fsql.Insert(entity).ExecuteIdentityAsync(ct);
        entity.AssignPersistedId((int)id);
    }

    /// <summary>插入聚合并回填自增主键（long 主键）</summary>
    internal static async Task InsertWithLongIdentityAsync<TEntity>(
        this IFreeSql fsql, TEntity entity, CancellationToken ct)
        where TEntity : AggregateRoot<long>
    {
        var id = await fsql.Insert(entity).ExecuteIdentityAsync(ct);
        entity.AssignPersistedId(id);
    }

    /// <summary>
    /// 构造 LIKE 的通配模式。
    /// 值对象类型的列无法出现在 LINQ 谓词的成员访问里，只能走原生 SQL 条件，
    /// 参数化以避免注入。
    /// </summary>
    internal static string ToLikePattern(string keyword) => $"%{keyword}%";
}
