using System.Collections.Concurrent;
using LHZ.WebSocket.Interfaces;

namespace LHZ.OnlineChat.Server.Services;

/// <summary>
/// WebSocket 连接管理器（单例）
/// 多端登录：按「登录会话 ID（sid）」管理连接，同一用户可多设备同时在线；
/// 提供 userId 维度的兼容方法（取该用户任一连接 / 全部连接）供消息转发使用。
/// </summary>
public class WsConnectionManager
{
    private readonly ConcurrentDictionary<string, IWebSocketClient> _connections = new();
    private readonly ConcurrentDictionary<int, HashSet<string>> _userSessions = new();
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
    /// 添加连接（sessionId 为登录会话 ID，来自 JWT sid claim；无 sid 的旧 Token 用一次性随机 key）
    /// </summary>
    public async Task AddConnectionAsync(int userId, string sessionId, IWebSocketClient client)
    {
        _connections[sessionId] = client;
        _userSessions.AddOrUpdate(userId,
            _ => new HashSet<string> { sessionId },
            (_, set) => { lock (set) set.Add(sessionId); return set; });

        // Redis 记录在线状态
        await _redis.SetJsonAsync($"ws:online:{userId}", new
        {
            UserId = userId,
            ConnectedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        Console.WriteLine($"[WS] 用户 {userId} 上线（会话 {sessionId[..Math.Min(8, sessionId.Length)]}），当前在线连接 {_connections.Count}");
    }

    /// <summary>
    /// 移除连接（带身份校验：仅当登记在册的是该 client 时才移除）。
    /// 返回是否成功移除；调用方应通过 IsOnline(userId) 判断是否广播离线。
    /// </summary>
    public async Task<bool> RemoveConnectionAsync(int userId, string sessionId, IWebSocketClient client)
    {
        if (!_connections.TryGetValue(sessionId, out var current) || !ReferenceEquals(current, client))
            return false;

        _connections.TryRemove(sessionId, out _);
        if (_userSessions.TryGetValue(userId, out var set))
        {
            lock (set)
            {
                set.Remove(sessionId);
                if (set.Count == 0) _userSessions.TryRemove(userId, out _);
            }
        }

        // Redis 删除在线状态（用户已无任何连接时）
        if (!IsOnline(userId))
            await _redis.DeleteKeyAsync($"ws:online:{userId}");

        Console.WriteLine($"[WS] 用户 {userId} 连接断开（会话 {sessionId[..Math.Min(8, sessionId.Length)]}），当前在线连接 {_connections.Count}");
        return true;
    }

    /// <summary>
    /// 强制关闭指定会话的所有连接（踢下线）：先推送 kicked 通知再断开
    /// </summary>
    public void CloseSession(string sessionId)
    {
        if (!_connections.TryGetValue(sessionId, out var client)) return;

        try
        {
            if (client.Status == LHZ.WebSocket.Enums.ClientStatus.Opend)
            {
                client.SendMessage("{\"type\":\"kicked\"}");
            }
        }
        catch { }

        // 立即 Close 会让 kicked 帧来不及送达：延迟 300ms 再关闭，
        // 确保前端收到 kicked 通知并自行登出（连接随后自然断开）
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300);
                client.Close();
            }
            catch { }
        });

        Console.WriteLine($"[WS] 会话 {sessionId[..Math.Min(8, sessionId.Length)]} 已被踢下线");
    }

    /// <summary>
    /// 获取用户任一在线连接（兼容旧调用：消息转发默认取第一个）
    /// </summary>
    public IWebSocketClient? GetConnection(int userId)
    {
        if (!_userSessions.TryGetValue(userId, out var set)) return null;
        lock (set)
        {
            foreach (var sid in set)
            {
                if (_connections.TryGetValue(sid, out var client) && client.Status == LHZ.WebSocket.Enums.ClientStatus.Opend)
                    return client;
            }
        }
        return null;
    }

    /// <summary>
    /// 获取用户的全部在线连接（多端广播）
    /// </summary>
    public IWebSocketClient[] GetConnections(int userId)
    {
        if (!_userSessions.TryGetValue(userId, out var set)) return Array.Empty<IWebSocketClient>();
        lock (set)
        {
            var list = new List<IWebSocketClient>(set.Count);
            foreach (var sid in set)
            {
                if (_connections.TryGetValue(sid, out var client) && client.Status == LHZ.WebSocket.Enums.ClientStatus.Opend)
                    list.Add(client);
            }
            return list.ToArray();
        }
    }

    /// <summary>
    /// 检查用户是否在线（任一连接）
    /// </summary>
    public bool IsOnline(int userId)
        => _userSessions.ContainsKey(userId) && GetConnection(userId) != null;

    /// <summary>
    /// 获取所有在线用户ID
    /// </summary>
    public IEnumerable<int> GetOnlineUserIds()
        => _userSessions.Keys;

    /// <summary>
    /// 获取所有连接（快照：sessionId → client）
    /// </summary>
    public IReadOnlyDictionary<string, IWebSocketClient> GetAllConnections()
        => _connections;
}
