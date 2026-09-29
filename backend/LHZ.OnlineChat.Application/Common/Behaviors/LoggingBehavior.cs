using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LHZ.OnlineChat.Application.Common.Behaviors;

/// <summary>
/// 用例级耗时与异常日志。
/// 原先「500 也必须留痕」是 Program.cs 里一段 try/catch 中间件，只能看到 HTTP 路径；
/// 放到管道里之后，WebSocket 触发的用例同样被覆盖。
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>超过该耗时记 Warning，便于发现慢用例</summary>
    private const long SlowThresholdMs = 1000;

    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger) => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds >= SlowThresholdMs)
                _logger.LogWarning("用例 {UseCase} 耗时 {Elapsed}ms（偏慢）", name, stopwatch.ElapsedMilliseconds);
            else
                _logger.LogDebug("用例 {UseCase} 完成，耗时 {Elapsed}ms", name, stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Domain.Common.DomainException)
        {
            // 业务驳回由 DomainExceptionBehavior 处理，这里不重复记录
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "用例 {UseCase} 执行失败，耗时 {Elapsed}ms", name, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
