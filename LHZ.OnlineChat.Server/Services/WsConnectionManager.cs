using System.Collections.Concurrent;
using LHZ.WebSocket.Interfaces;

namespace LHZ.OnlineChat.Server.Services;

/// <summary>
/// WebSocket 连接管理器（单例）
/// 管理用户ID与 WebSocket 连接的映射关系
/// </summary>
public class WsConnectionManager
{
    private readonly ConcurrentDictionary<int, IWebSocketClient> _connections = new();
    private readonly RedisService _redis;

    public WsConnectionManager(RedisService redis)
    {
        _redis = redis;
    }

    /// <summary>
    /// 当前连接数
    /// </summary>
    public int ConnectionCount => _connections.Count;

    /// <summary>
    /// 添加连接
    /// </summary>
    public async Task AddConnectionAsync(int userId, IWebSocketClient client)
    {
        _connections[userId] = client;

        // Redis 记录在线状态
        await _redis.SetJsonAsync($"ws:online:{userId}", new
        {
            UserId = userId,
            ConnectedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        Console.WriteLine($"[WS] 用户 {userId} 上线，当前在线: {_connections.Count}");
    }

    /// <summary>
    /// 移除连接。
    /// 仅当字典中登记的就是该 client 时才移除并清理 Redis，
    /// 避免同一账号新连接已替换旧连接后，旧连接的关闭回调误删新连接。
    /// </summary>
    public async Task<bool> RemoveConnectionAsync(int userId, IWebSocketClient client)
    {
        if (!_connections.TryGetValue(userId, out var current) || !ReferenceEquals(current, client))
            return false;

        _connections.TryRemove(userId, out _);

        // Redis 删除在线状态
        await _redis.DeleteKeyAsync($"ws:online:{userId}");

        Console.WriteLine($"[WS] 用户 {userId} 下线，当前在线: {_connections.Count}");
        return true;
    }

    /// <summary>
    /// 移除连接（不校验身份，仅保留兼容旧调用）
    /// </summary>
    public async Task<bool> RemoveConnectionAsync(int userId)
    {
        if (!_connections.TryGetValue(userId, out var current))
            return false;
        return await RemoveConnectionAsync(userId, current);
    }

    /// <summary>
    /// 获取用户的 WebSocket 连接
    /// </summary>
    public IWebSocketClient? GetConnection(int userId)
    {
        _connections.TryGetValue(userId, out var client);
        return client;
    }

    /// <summary>
    /// 检查用户是否在线
    /// </summary>
    public bool IsOnline(int userId)
        => _connections.ContainsKey(userId);

    /// <summary>
    /// 获取所有在线用户ID
    /// </summary>
    public IEnumerable<int> GetOnlineUserIds()
        => _connections.Keys;

    /// <summary>
    /// 获取所有连接（快照）
    /// </summary>
    public IReadOnlyDictionary<int, IWebSocketClient> GetAllConnections()
        => _connections;
}
