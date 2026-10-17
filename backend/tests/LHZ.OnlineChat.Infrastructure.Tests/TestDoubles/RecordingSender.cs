using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace LHZ.OnlineChat.Infrastructure.Tests.TestDoubles;

/// <summary>
/// 记录型 ISender：把派发出去的 Command/Query 收起来供断言，不执行任何 Handler。
/// 与 Application.Tests 的 DirectSender 不同 —— 这里只关心"分发器是否把正确的命令发出去了"。
/// </summary>
internal sealed class RecordingSender : ISender
{
    internal List<object> Sent { get; } = new();

    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        Sent.Add(request);
        return Task.FromResult(default(TResponse)!);
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        Sent.Add(request!);
        return Task.CompletedTask;
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        Sent.Add(request);
        return Task.FromResult<object?>(null);
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("测试替身不支持流式请求");

    public IAsyncEnumerable<object?> CreateStream(
        object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("测试替身不支持流式请求");
}

/// <summary>
/// 只解析 ISender 的作用域工厂替身：分发器每条报文都会 CreateScope，
/// 这里同时记录创建次数（心跳路径不该建作用域）。
/// </summary>
internal sealed class FakeScopeFactory : IServiceScopeFactory
{
    private readonly ISender _sender;

    public FakeScopeFactory(ISender sender) => _sender = sender;

    internal int Created { get; private set; }

    public IServiceScope CreateScope()
    {
        Created++;
        return new Scope(_sender);
    }

    private sealed class Scope : IServiceScope
    {
        public Scope(ISender sender) => ServiceProvider = new Provider(sender);

        public IServiceProvider ServiceProvider { get; }

        public void Dispose()
        {
        }
    }

    private sealed class Provider : IServiceProvider
    {
        private readonly ISender _sender;

        public Provider(ISender sender) => _sender = sender;

        public object? GetService(Type serviceType)
            => serviceType == typeof(ISender) ? _sender : null;
    }
}
