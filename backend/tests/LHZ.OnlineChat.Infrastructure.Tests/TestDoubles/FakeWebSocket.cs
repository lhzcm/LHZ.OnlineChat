using LHZ.OnlineChat.Application.Abstractions;
using LHZ.WebSocket.Core;
using LHZ.WebSocket.Enums;
using LHZ.WebSocket.Interfaces;

namespace LHZ.OnlineChat.Infrastructure.Tests.TestDoubles;

/// <summary>
/// WS 客户端替身：记录发出的报文与 Close 调用，Status 可调（模拟半开/已关闭连接）。
/// 事件故意不触发 —— Realtime 这几层的测试只驱动**出站**路径，入站解析由 dispatcher 的测试负责。
///
/// 注意签名按 LHZ.WebSocket 1.2.0 的真实接口：ID 是 Guid、MaxMessageSize 是 int、
/// 事件是 EventHandler&lt;IWebSocketClient, T&gt;、并且实现了 IDisposable。
/// </summary>
internal sealed class FakeWebSocketClient : IWebSocketClient
{
    /// <param name="label">仅用于断言失败时的可读性（ID 是 Guid，直接看没有意义）</param>
    public FakeWebSocketClient(string label = "fake") => Label = label;

    internal string Label { get; }

    internal List<string> Sent { get; } = new();

    internal bool CloseCalled { get; private set; }

    internal bool Disposed { get; private set; }

    /// <summary>让下一次 SendMessage 抛异常，用来验证"单个连接失败不影响其他设备"</summary>
    internal bool ThrowOnSend { get; set; }

    public Guid ID { get; } = Guid.NewGuid();

    public ClientStatus Status { get; set; } = ClientStatus.Opened;

    public int MaxMessageSize { get; set; }

    public void SendMessage(string message)
    {
        if (ThrowOnSend) throw new InvalidOperationException("发送失败（测试替身）");
        Sent.Add(message);
    }

    public Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        SendMessage(message);
        return Task.CompletedTask;
    }

    public void SendByte(byte[] data)
    {
    }

    public Task SendByteAsync(byte[] data, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Ping(byte[] data)
    {
    }

    public Task PingAsync(byte[] data, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Pong(byte[] data)
    {
    }

    public Task PongAsync(byte[] data, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Open() => Status = ClientStatus.Opened;

    public void Close()
    {
        CloseCalled = true;
        Status = ClientStatus.Close;
    }

    public void Dispose() => Disposed = true;

    // 替身不主动触发事件：Realtime 测试只驱动出站路径。
    // 事件类型是库自带的 LHZ.WebSocket.Delegates.EventHandler<TSender, TEventArgs>，
    // 而 .NET 10 的 System 命名空间里也有同名同 arity 的委托，必须写全名才不会歧义。
#pragma warning disable CS0067
    public event LHZ.WebSocket.Delegates.EventHandler<IWebSocketClient, string>? OnMessageReceived;

    public event LHZ.WebSocket.Delegates.EventHandler<IWebSocketClient, byte[]>? OnBytesReceived;

    public event LHZ.WebSocket.Delegates.EventHandler<IWebSocketClient, CloseMessage>? OnCloseReceived;

    public event LHZ.WebSocket.Delegates.EventHandler<IWebSocketClient, byte[]>? OnPingReceived;

    public event LHZ.WebSocket.Delegates.EventHandler<IWebSocketClient, byte[]>? OnPongReceived;

    public event LHZ.WebSocket.Delegates.EventHandler<IWebSocketClient, Exception>? OnError;

    public event Action<IWebSocketClient>? OnClientClose;
#pragma warning restore CS0067
}

/// <summary>在线状态存储替身：记录调用顺序，用来断言"最后一个连接断开才广播离线"</summary>
internal sealed class FakePresenceStore : IPresenceStore
{
    internal List<int> OnlineCalls { get; } = new();

    internal List<int> OfflineCalls { get; } = new();

    internal List<int> RefreshCalls { get; } = new();

    internal bool Online { get; private set; }

    public Task MarkOnlineAsync(int userId, CancellationToken ct = default)
    {
        OnlineCalls.Add(userId);
        Online = true;
        return Task.CompletedTask;
    }

    public Task RefreshAsync(int userId, CancellationToken ct = default)
    {
        RefreshCalls.Add(userId);
        return Task.CompletedTask;
    }

    public Task MarkOfflineAsync(int userId, CancellationToken ct = default)
    {
        OfflineCalls.Add(userId);
        Online = false;
        return Task.CompletedTask;
    }

    public Task<bool> IsOnlineAsync(int userId, CancellationToken ct = default)
        => Task.FromResult(Online);

    public Task<IReadOnlyDictionary<int, bool>> GetStatesAsync(
        IEnumerable<int> userIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<int, bool>>(
            userIds.Distinct().ToDictionary(id => id, _ => Online));
}
