using LHZ.OnlineChat.Application.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LHZ.OnlineChat.Application.Common.Behaviors;

/// <summary>
/// 把每个 Command 包进一个数据库事务，并把领域事件派发推迟到提交之后。
///
/// 只拦 <see cref="ICommand{TResponse}"/>：Query 是只读的，开事务只会白占连接。
///
/// 管道位置必须在 DomainExceptionBehavior 之内 —— 业务规则驳回是以
/// DomainException 的形式抛出的，要先让它穿过这里触发回滚，再被翻译成 ApiResponse.Fail。
/// 否则「已经是该群成员」这类驳回会带着半截写入提交掉。
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly DomainEventOutbox _outbox;
    private readonly IDomainEventDispatcher _events;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

    public TransactionBehavior(
        IUnitOfWork unitOfWork,
        DomainEventOutbox outbox,
        IDomainEventDispatcher events,
        ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    {
        _unitOfWork = unitOfWork;
        _outbox = outbox;
        _events = events;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ICommand<TResponse>) return await next(cancellationToken);

        // 嵌套（用例内又发了一个 Command）：外层已经有事务和缓冲，直接透传
        if (_outbox.IsDeferring) return await next(cancellationToken);

        _outbox.BeginDeferring();

        TResponse response;
        try
        {
            response = await _unitOfWork
                .ExecuteAsync(ct => next(ct), cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // 事务已回滚，积压的事件对应的写入并不存在，必须丢弃
            _outbox.Discard();
            throw;
        }

        var pending = _outbox.StopDeferringAndDrain();
        if (pending.Count > 0)
        {
            _logger.LogDebug(
                "{Request} 事务已提交，派发 {Count} 个领域事件", typeof(TRequest).Name, pending.Count);

            await _events.DispatchAsync(pending, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }
}
