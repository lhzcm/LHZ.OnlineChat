using System.Collections.Concurrent;
using LHZ.FastJson;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.WebSocket.Enums;
using LHZ.WebSocket.Interfaces;
using Microsoft.Extensions.Logging;

namespace LHZ.OnlineChat.Infrastructure.Realtime;

/// <summary>
/// WebSocket 连接管理器（单例）。
/// 多端登录：按「登录会话 ID（sid）」管理连接，同一用户可多设备同时在线。
/// 同时实现应用层的 IConnectionRegistry —— 应用层只看到「谁在线 / 踢会话」，看不到连接对象。
/// </summary>
internal sealed class WsConnectionManager : IConnectionRegistry
{
    /// <summary>踢下线时先推 kicked 再断开，留出送达时间</summary>
    private static readonly TimeSpan KickGracePeriod = TimeSpan.FromMilliseconds(300);

    private readonly ConcurrentDictionary<string, IWebSocketClient> _connections = new();
    private readonly ConcurrentDictionary<int, HashSet<string>> _userSessions = new();
    private readonly IPresenceStore _presence;
    private readonly ILogger<WsConnectionManager> _logger;

    public WsConnectionManager(IPresenceStore presence, ILogger<WsConnectionManager> logger)
    {
        _presence = presence;
        _logger = logger;
    }

    public int ConnectionCount => _connections.Count;

    public int OnlineUserCount => _userSessions.Count;

    /// <summary>登记新连接（sessionId 来自 JWT 的 sid claim）</summary>
    public async Task AddConnectionAsync(int userId, string sessionId, IWebSocketClient client)
    {
        // 同一会话重复建连（客户端重连时旧 socket 尚未被服务端察觉）：
        // 必须把旧连接关掉再登记新的。只覆盖字典项的话，旧 socket 会变成
        // 「没人引用但依然打开、依然在分发入站命令」的僵尸连接 ——
        // RemoveConnectionAsync 有 ReferenceEquals 校验，所以它断开时也不会误删新连接，
        // 但在此之前它的报文照样被执行。
        if (_connections.TryGetValue(sessionId, out var previous) && !ReferenceEquals(previous, client))
        {
            _logger.LogWarning(
                "会话 {Session} 重复建连，关闭旧连接（用户 {UserId}）", Abbreviate(sessionId), userId);

            try
            {
                previous.Close();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "关闭会话 {Session} 的旧连接时出错", Abbreviate(sessionId));
            }
        }

        _connections[sessionId] = client;
        _userSessions.AddOrUpdate(
            userId,
            _ => new HashSet<string> { sessionId },
            (_, set) => { lock (set) { set.Add(sessionId); } return set; });

        await _presence.MarkOnlineAsync(userId).ConfigureAwait(false);

        _logger.LogInformation(
            "用户 {UserId} 上线（会话 {Session}），当前连接数 {Count}",
            userId, Abbreviate(sessionId), _connections.Count);
    }

    /// <summary>
    /// 移除连接（带身份校验：仅当登记在册的就是该 client 时才移除，旧连接不会误删新连接）。
    /// 返回是否真的移除；调用方据此决定要不要广播离线。
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

        if (!IsOnline(userId))
            await _presence.MarkOfflineAsync(userId).ConfigureAwait(false);

        _logger.LogInformation(
            "用户 {UserId} 连接断开（会话 {Session}），当前连接数 {Count}",
            userId, Abbreviate(sessionId), _connections.Count);

        return true;
    }

    /// <summary>踢下线：先推 kicked 通知，延迟后关闭（否则帧来不及送达）</summary>
    public void CloseSession(string sessionId)
    {
        if (!_connections.TryGetValue(sessionId, out var client)) return;

        TrySend(client, JsonConvert.Serialize(new WsMessage { Type = WsMessageType.Kicked }));

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(KickGracePeriod).ConfigureAwait(false);
                client.Close();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "关闭会话 {Session} 的连接时出错", Abbreviate(sessionId));
            }
        });

        _logger.LogInformation("会话 {Session} 已被踢下线", Abbreviate(sessionId));
    }

    public bool IsOnline(int userId) => GetConnections(userId).Length > 0;

    public IReadOnlyDictionary<int, bool> GetOnlineStates(IEnumerable<int> userIds)
        => userIds.Distinct().ToDictionary(id => id, IsOnline);

    /// <summary>该用户的全部在线连接（多端广播）</summary>
    public IWebSocketClient[] GetConnections(int userId)
    {
        if (!_userSessions.TryGetValue(userId, out var set)) return Array.Empty<IWebSocketClient>();

        lock (set)
        {
            var list = new List<IWebSocketClient>(set.Count);
            foreach (var sessionId in set)
            {
                if (_connections.TryGetValue(sessionId, out var client)
                    && client.Status == ClientStatus.Opened)
                {
                    list.Add(client);
                }
            }
            return list.ToArray();
        }
    }

    /// <summary>该用户的任一在线连接（只需推一份的场景，如离线消息补发）</summary>
    public IWebSocketClient? GetAnyConnection(int userId)
    {
        var connections = GetConnections(userId);
        return connections.Length > 0 ? connections[0] : null;
    }

    /// <summary>向该用户全部在线设备推送一段报文，返回送达的连接数</summary>
    public int Broadcast(int userId, string payload)
    {
        var sent = 0;
        foreach (var client in GetConnections(userId))
        {
            if (TrySend(client, payload)) sent++;
        }
        return sent;
    }

    private bool TrySend(IWebSocketClient client, string payload)
    {
        try
        {
            if (client.Status != ClientStatus.Opened) return false;
            client.SendMessage(payload);
            return true;
        }
        catch (Exception ex)
        {
            // 单个连接发送失败不影响其他设备
            _logger.LogDebug(ex, "WS 推送失败");
            return false;
        }
    }

    private static string Abbreviate(string sessionId)
        => sessionId.Length <= 8 ? sessionId : sessionId[..8];
}
