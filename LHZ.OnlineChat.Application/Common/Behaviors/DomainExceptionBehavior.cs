using System.Reflection;
using LHZ.OnlineChat.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LHZ.OnlineChat.Application.Common.Behaviors;

/// <summary>
/// 把领域异常翻译成 ApiResponse.Fail。
///
/// 这是「让领域层摆脱 ApiResponse」的关键一环：
/// 改造前每个 Service 方法都是 `if (...) return ApiResponse.Fail("提示语")` 一路向上传递，
/// 业务规则和 HTTP 响应形状死死绑在一起；
/// 改造后领域层只 `throw new DomainException("提示语")`，由这里统一落地成既有响应格式，
/// 前端拿到的 JSON 一模一样。
/// </summary>
public sealed class DomainExceptionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<DomainExceptionBehavior<TRequest, TResponse>> _logger;

    public DomainExceptionBehavior(ILogger<DomainExceptionBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            // 业务规则驳回属于预期路径，记 Debug 而非 Error，避免污染错误日志
            _logger.LogDebug("业务规则驳回 {Request}: {Message}", typeof(TRequest).Name, ex.Message);

            if (TryCreateFailure(ex.Message, out var failure)) return failure!;
            throw;
        }
    }

    /// <summary>
    /// 反射构造 ApiResponse&lt;T&gt;.Fail(message)。
    /// 只在「已经确定要返回失败」的路径上执行，不在正常路径上产生开销。
    /// </summary>
    private static bool TryCreateFailure(string message, out TResponse? failure)
    {
        failure = default;
        var responseType = typeof(TResponse);

        // 非 ApiResponse 系列的返回类型（理论上不存在）交回上层处理
        if (!IsApiResponse(responseType)) return false;

        var factory = responseType.GetMethod(
            nameof(ApiResponse.Fail),
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
            new[] { typeof(string) });

        // ApiResponse（非泛型）自己声明了 new Fail；ApiResponse<T> 用基类声明
        factory ??= responseType.GetMethod(
            nameof(ApiResponse.Fail),
            BindingFlags.Public | BindingFlags.Static,
            new[] { typeof(string) });

        if (factory is null) return false;

        failure = (TResponse?)factory.Invoke(null, new object[] { message });
        return failure is not null;
    }

    private static bool IsApiResponse(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current == typeof(ApiResponse)) return true;
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ApiResponse<>)) return true;
        }
        return false;
    }
}
