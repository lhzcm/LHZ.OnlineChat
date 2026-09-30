namespace LHZ.OnlineChat.Infrastructure.Persistence;

/// <summary>
/// 仓储实现的公共动作。
/// 插入并回填自增主键的辅助方法在 <see cref="DbSession"/> 上（需要环境事务）。
/// </summary>
internal static class FreeSqlExtensions
{
    /// <summary>
    /// 构造 LIKE 的通配模式。
    /// 值对象类型的列无法出现在 LINQ 谓词的成员访问里，只能走原生 SQL 条件，
    /// 参数化以避免注入。
    /// </summary>
    internal static string ToLikePattern(string keyword) => $"%{keyword}%";
}
