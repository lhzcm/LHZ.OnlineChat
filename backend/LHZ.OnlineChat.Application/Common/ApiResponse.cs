namespace LHZ.OnlineChat.Application.Common;

/// <summary>
/// 统一响应包装。
/// 这是「应用层契约」而非领域概念 —— 原先它定义在 Models/DTOs 里却被业务服务层到处返回，
/// 导致 HTTP 关注点渗透进业务逻辑。现在领域层只抛 DomainException，
/// 由 DomainExceptionBehavior 统一转换为 Fail，形状与旧接口完全一致（前端无需改动）。
/// </summary>
/// <summary>
/// 响应的非泛型视图。
/// 表现层靠它用一个方法处理所有用例的返回值 ——
/// 否则 ApiResponse 与 ApiResponse&lt;T&gt; 要各写一个重载，
/// 而 MediatR 的 IRequest&lt;out T&gt; 是协变的，两个重载会产生调用歧义。
/// </summary>
public interface IApiResponse
{
    bool Success { get; }

    string Message { get; }
}

public class ApiResponse<T> : IApiResponse
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "success")
        => new() { Success = true, Message = message, Data = data };

    public static ApiResponse<T> Fail(string message)
        => new() { Success = false, Message = message };
}

/// <summary>无数据载荷的响应</summary>
public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse Ok(string message = "success")
        => new() { Success = true, Message = message };

    public static new ApiResponse Fail(string message)
        => new() { Success = false, Message = message };
}

/// <summary>分页结果</summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();

    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public static PagedResult<T> Create(IEnumerable<T> items, int total, Domain.Common.PageRequest page)
        => new()
        {
            Items = items.ToList(),
            Total = total,
            Page = page.Page,
            PageSize = page.PageSize
        };

    public static PagedResult<T> Empty(Domain.Common.PageRequest page)
        => new() { Items = new List<T>(), Total = 0, Page = page.Page, PageSize = page.PageSize };
}
