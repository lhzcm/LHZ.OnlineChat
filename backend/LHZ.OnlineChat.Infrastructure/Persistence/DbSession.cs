using System.Data.Common;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Domain.Common;
using Npgsql;

namespace LHZ.OnlineChat.Infrastructure.Persistence;

/// <summary>
/// 每请求的数据库会话：持有 FreeSql 实例与「当前环境事务」。
///
/// 仓储一律经由本类发起查询（而不是直接用 IFreeSql），这样 TransactionBehavior
/// 开启事务后，同一请求内的所有读写会自动挂到同一个 DbTransaction 上 ——
/// 不需要在每个仓储方法里手动传递事务对象。
///
/// 没有事务时（Query 路径）Transaction 为 null，行为与直接用 IFreeSql 完全一致。
/// </summary>
internal sealed class DbSession
{
    public DbSession(IFreeSql orm) => Orm = orm;

    /// <summary>底层 FreeSql 实例（建表、原生 SQL 等不需要事务的场合直接用）</summary>
    public IFreeSql Orm { get; }

    /// <summary>当前环境事务；由 FreeSqlUnitOfWork 在事务期间设置</summary>
    internal DbTransaction? Transaction { get; set; }

    internal FreeSql.ISelect<TEntity> Select<TEntity>() where TEntity : class
    {
        var select = Orm.Select<TEntity>();
        return Transaction is null ? select : select.WithTransaction(Transaction);
    }

    internal FreeSql.IInsert<TEntity> Insert<TEntity>(TEntity entity) where TEntity : class
    {
        var insert = Orm.Insert(entity);
        return Transaction is null ? insert : insert.WithTransaction(Transaction);
    }

    internal FreeSql.IUpdate<TEntity> Update<TEntity>() where TEntity : class
    {
        var update = Orm.Update<TEntity>();
        return Transaction is null ? update : update.WithTransaction(Transaction);
    }

    internal FreeSql.IDelete<TEntity> Delete<TEntity>() where TEntity : class
    {
        var delete = Orm.Delete<TEntity>();
        return Transaction is null ? delete : delete.WithTransaction(Transaction);
    }

    /// <summary>
    /// 插入聚合并回填数据库生成的自增主键（int 主键）。
    ///
    /// 必须用 ExecuteIdentity 而不是 ExecuteAffrows：后者会把默认值 0 当作显式主键插入，破坏自增。
    /// FreeSql 不会自动写回带非公开 setter 的 Id，因此显式调用领域层的 AssignPersistedId 接缝。
    /// </summary>
    internal async Task InsertWithIdentityAsync<TEntity>(TEntity entity, CancellationToken ct)
        where TEntity : AggregateRoot<int>
    {
        var id = await Insert(entity).ExecuteIdentityAsync(ct);
        entity.AssignPersistedId((int)id);
    }

    /// <summary>插入聚合并回填自增主键（long 主键）</summary>
    internal async Task InsertWithLongIdentityAsync<TEntity>(TEntity entity, CancellationToken ct)
        where TEntity : AggregateRoot<long>
    {
        var id = await Insert(entity).ExecuteIdentityAsync(ct);
        entity.AssignPersistedId(id);
    }
}

/// <summary>
/// FreeSql 事务实现。
///
/// 用 CreateUnitOfWork 而不是 fsql.Transaction(Action)：后者基于线程局部状态，
/// 在 async/await 里跨线程续跑就会丢失事务上下文。
/// </summary>
internal sealed class FreeSqlUnitOfWork : IUnitOfWork
{
    private readonly DbSession _session;

    public FreeSqlUnitOfWork(DbSession session) => _session = session;

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // 已在事务中（嵌套调用）：复用外层事务，不开新的
        if (_session.Transaction is not null) return await operation(ct).ConfigureAwait(false);

        using var unitOfWork = _session.Orm.CreateUnitOfWork();
        _session.Transaction = unitOfWork.GetOrBeginTransaction();

        try
        {
            var result = await operation(ct).ConfigureAwait(false);
            unitOfWork.Commit();
            return result;
        }
        catch (DbException ex) when (TryDescribeUniqueViolation(ex, out var hint))
        {
            unitOfWork.Rollback();
            throw new UniqueConstraintViolationException(hint!, ex);
        }
        catch
        {
            unitOfWork.Rollback();
            throw;
        }
        finally
        {
            _session.Transaction = null;
        }
    }

    /// <summary>
    /// PostgreSQL 唯一约束冲突（SQLSTATE 23505）翻译。
    /// 「先查重再插入」在并发下总有窗口期，唯一索引是最终兜底；
    /// 这里把驱动异常转成应用层能识别的类型，好让用例返回友好提示而不是 500。
    /// </summary>
    private static bool TryDescribeUniqueViolation(DbException exception, out string? constraintHint)
    {
        constraintHint = null;

        if (exception is not PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg) return false;

        constraintHint = string.IsNullOrEmpty(pg.ConstraintName) ? pg.MessageText : pg.ConstraintName;
        return true;
    }
}
