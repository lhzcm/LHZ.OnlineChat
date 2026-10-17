using LHZ.OnlineChat.Infrastructure.Realtime;
using LHZ.OnlineChat.Infrastructure.Tests.TestDoubles;
using LHZ.WebSocket.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace LHZ.OnlineChat.Infrastructure.Tests.Realtime;

/// <summary>
/// 连接表：多端登录、重复建连、僵尸连接、广播失败隔离。
/// 这些分支以前一条测试都没有 —— 出问题表现为"某个设备收不到消息"或"下线状态永远不广播"，很难现场复现。
/// </summary>
public class WsConnectionManagerTests
{
    private static WsConnectionManager Create(FakePresenceStore presence)
        => new(presence, NullLogger<WsConnectionManager>.Instance);

    [Fact(DisplayName = "AddConnection_同一会话重复建连时关掉旧连接")]
    public async Task Add_SameSessionTwice_ClosesPreviousConnection()
    {
        var presence = new FakePresenceStore();
        var manager = Create(presence);
        var oldClient = new FakeWebSocketClient("old");
        var newClient = new FakeWebSocketClient("new");

        await manager.AddConnectionAsync(10001, "sid-1", oldClient);
        await manager.AddConnectionAsync(10001, "sid-1", newClient);

        Assert.True(oldClient.CloseCalled);
        Assert.False(newClient.CloseCalled);
        Assert.Equal(1, manager.ConnectionCount);
    }

    [Fact(DisplayName = "AddConnection_登记后标记在线")]
    public async Task Add_MarksUserOnline()
    {
        var presence = new FakePresenceStore();
        var manager = Create(presence);

        await manager.AddConnectionAsync(10001, "sid-1", new FakeWebSocketClient());

        Assert.Equal(new[] { 10001 }, presence.OnlineCalls);
        Assert.True(manager.IsOnline(10001));
        Assert.Equal(1, manager.OnlineUserCount);
    }

    [Fact(DisplayName = "RemoveConnection_旧连接的关闭事件不会误删新连接")]
    public async Task Remove_StaleClient_DoesNotRemoveNewConnection()
    {
        var presence = new FakePresenceStore();
        var manager = Create(presence);
        var oldClient = new FakeWebSocketClient("old");
        var newClient = new FakeWebSocketClient("new");

        await manager.AddConnectionAsync(10001, "sid-1", oldClient);
        await manager.AddConnectionAsync(10001, "sid-1", newClient);   // 旧连接被顶掉

        var removed = await manager.RemoveConnectionAsync(10001, "sid-1", oldClient);

        Assert.False(removed);
        Assert.Equal(1, manager.ConnectionCount);
        Assert.Contains(newClient, manager.GetConnections(10001));
    }

    [Fact(DisplayName = "RemoveConnection_最后一个连接断开才标记离线")]
    public async Task Remove_LastConnection_MarksOffline()
    {
        var presence = new FakePresenceStore();
        var manager = Create(presence);
        var phone = new FakeWebSocketClient("phone");
        var pc = new FakeWebSocketClient("pc");

        await manager.AddConnectionAsync(10001, "sid-phone", phone);
        await manager.AddConnectionAsync(10001, "sid-pc", pc);

        await manager.RemoveConnectionAsync(10001, "sid-phone", phone);
        Assert.Empty(presence.OfflineCalls);          // 还有一台在线

        await manager.RemoveConnectionAsync(10001, "sid-pc", pc);
        Assert.Equal(new[] { 10001 }, presence.OfflineCalls);
        Assert.False(manager.IsOnline(10001));
    }

    [Fact(DisplayName = "GetConnections_只返回仍处于打开状态的连接")]
    public async Task GetConnections_SkipsClosedClients()
    {
        var presence = new FakePresenceStore();
        var manager = Create(presence);
        var alive = new FakeWebSocketClient("alive");
        var dead = new FakeWebSocketClient("dead");

        await manager.AddConnectionAsync(10001, "sid-a", alive);
        await manager.AddConnectionAsync(10002, "sid-b", dead);
        dead.Status = ClientStatus.Close;

        Assert.Equal(new[] { alive }, manager.GetConnections(10001));
        Assert.Empty(manager.GetConnections(10002));
    }

    [Fact(DisplayName = "Broadcast_推到该用户全部在线设备并返回送达数")]
    public async Task Broadcast_ReachesAllDevicesAndCounts()
    {
        var presence = new FakePresenceStore();
        var manager = Create(presence);
        var phone = new FakeWebSocketClient("phone");
        var pc = new FakeWebSocketClient("pc");

        await manager.AddConnectionAsync(10001, "sid-phone", phone);
        await manager.AddConnectionAsync(10001, "sid-pc", pc);

        var sent = manager.Broadcast(10001, """{"type":"typing"}""");

        Assert.Equal(2, sent);
        Assert.All(new[] { phone, pc }, c => Assert.Contains("""{"type":"typing"}""", c.Sent));
    }

    [Fact(DisplayName = "Broadcast_单个连接抛异常不影响其他设备")]
    public async Task Broadcast_OneDeviceFailing_DoesNotAffectOthers()
    {
        var presence = new FakePresenceStore();
        var manager = Create(presence);
        var broken = new FakeWebSocketClient("broken") { ThrowOnSend = true };
        var healthy = new FakeWebSocketClient("healthy");

        await manager.AddConnectionAsync(10001, "sid-broken", broken);
        await manager.AddConnectionAsync(10001, "sid-healthy", healthy);

        var sent = manager.Broadcast(10001, """{"type":"kicked"}""");

        Assert.Equal(1, sent);
        Assert.Contains("""{"type":"kicked"}""", healthy.Sent);
    }

    [Fact(DisplayName = "CloseSession_先推kicked再关连接")]
    public async Task CloseSession_SendsKickedThenCloses()
    {
        var presence = new FakePresenceStore();
        var manager = Create(presence);
        var client = new FakeWebSocketClient();
        await manager.AddConnectionAsync(10001, "sid-1", client);

        manager.CloseSession("sid-1");

        // kicked 是同步推的（不能等 300ms 宽限期）
        Assert.Contains("kicked", Assert.Single(client.Sent));
        // 关闭是延迟的：给帧留送达时间
        Assert.True(await WaitUntilAsync(() => client.CloseCalled), "300ms 内应完成关闭");
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }
}
