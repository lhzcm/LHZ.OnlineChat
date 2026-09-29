namespace LHZ.OnlineChat.Domain.Common;

/// <summary>
/// 领域时钟。领域方法一律接收外部传入的 now，不直接读 DateTime.UtcNow，便于测试与跨层一致。
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

/// <summary>
/// UTC 时间归一化。
/// Npgsql/FreeSql 读出的 DateTime.Kind 可能是 Unspecified，序列化后缺少 Z 后缀，
/// 前端按本地时间解析会偏移 8 小时 —— 原先这段逻辑重复散落在 MessageService / WsMessageHandler。
/// </summary>
public static class UtcTime
{
    public static DateTime Normalize(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static DateTime? Normalize(DateTime? value)
        => value.HasValue ? Normalize(value.Value) : null;

    public static long ToUnixMilliseconds(DateTime value)
        => new DateTimeOffset(Normalize(value)).ToUnixTimeMilliseconds();
}

/// <summary>分页请求：页码/页长的夹紧规则收敛点（原先每个 Service 各写一遍 Math.Clamp）</summary>
public readonly struct PageRequest : IEquatable<PageRequest>
{
    public PageRequest(int page, int pageSize, int maxPageSize = 50)
    {
        Page = Math.Max(1, page);
        PageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, maxPageSize);
    }

    public int Page { get; }

    public int PageSize { get; }

    public int Skip => (Page - 1) * PageSize;

    /// <summary>跨表合并分页时需要「各表先取前 N 条」的条数</summary>
    public int TakeThroughCurrentPage => Page * PageSize;

    public bool Equals(PageRequest other) => Page == other.Page && PageSize == other.PageSize;

    public override bool Equals(object? obj) => obj is PageRequest other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Page, PageSize);

    public static bool operator ==(PageRequest left, PageRequest right) => left.Equals(right);

    public static bool operator !=(PageRequest left, PageRequest right) => !left.Equals(right);

    public override string ToString() => $"page={Page},size={PageSize}";
}

/// <summary>目标不存在（应用层同样映射为 Fail，与原实现的 "xxx不存在" 语义一致）</summary>
public sealed class EntityNotFoundException : DomainException
{
    public EntityNotFoundException(string message) : base(message) { }
}

/// <summary>无权执行该操作</summary>
public sealed class ForbiddenOperationException : DomainException
{
    public ForbiddenOperationException(string message) : base(message) { }
}
