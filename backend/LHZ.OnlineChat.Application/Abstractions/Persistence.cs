namespace LHZ.OnlineChat.Application.Abstractions;

/// <summary>
/// 工作单元：把一个用例内的多次写操作收进单个数据库事务。
///
/// 为什么需要它：像「建群」这种用例要连写 Group_ 和 GroupMember 两张表，
/// 没有事务时第二步失败会留下零成员的孤儿群 —— 群主自己都看不到它，也无法解散。
///
/// 由 TransactionBehavior 统一包裹全部 Command，业务代码不直接调用。
/// 嵌套调用（Behavior 已开事务时用例内再调）复用外层事务，不会开出第二个。
/// </summary>
public interface IUnitOfWork
{
    /// <summary>在事务中执行；operation 抛出任何异常即回滚</summary>
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken ct = default);
}

/// <summary>
/// 唯一约束冲突。
/// 「先查重再插入」在并发下必然有窗口期（两个请求可以都通过查重），
/// 真正的兜底是数据库唯一索引；索引报错后由仓储翻译成这个异常，
/// 让应用层能给出和查重分支一致的用户提示，而不是 500。
/// </summary>
public sealed class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException(string constraintHint, Exception innerException)
        : base($"唯一约束冲突：{constraintHint}", innerException)
        => ConstraintHint = constraintHint;

    /// <summary>冲突的约束名/列名线索（用于判断是哪一条唯一约束）</summary>
    public string ConstraintHint { get; }
}
