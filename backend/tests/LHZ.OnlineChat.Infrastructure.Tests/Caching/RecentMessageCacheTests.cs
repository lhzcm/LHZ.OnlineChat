using LHZ.OnlineChat.Infrastructure.Caching;

namespace LHZ.OnlineChat.Infrastructure.Tests.Caching;

/// <summary>
/// 撤回时「这条缓存是不是要删的那条」的判定。
///
/// 为什么单独测：原先的实现在原始 JSON 上做子串匹配
/// （<c>json.Contains("\"messageId\":\"...\"")</c>），
/// 只要消息正文里恰好出现同样的片段就会误命中 —— 删掉一条完全无关的缓存。
/// 这类缺陷用真实 Redis 测不出来（那次误删在功能上「看起来正常」），
/// 所以把判定抽成纯函数直接验。
/// </summary>
public class RecentMessageCacheMatchTests
{
    /// <summary>与 CachedPayload.Create 序列化出来的形状一致（camelCase）</summary>
    private static string Payload(string content, string messageId) =>
        $$"""
        {"from":"10001","content":{{System.Text.Json.JsonSerializer.Serialize(content)}},"messageType":0,"messageId":"{{messageId}}","senderName":"张三","senderAvatar":null,"timestamp":1700000000000}
        """;

    [Fact]
    public void 客户端消息号命中时判定为要删除()
    {
        var json = Payload("普通内容", "cmid-1");

        Assert.True(RecentMessageCache.IsTargetMessage(json, "cmid-1", "77"));
    }

    [Fact]
    public void 数据库ID作为消息号时同样命中()
    {
        // 没有客户端 ID 的消息，其 messageId 就是数据库 ID 的字符串形式
        var json = Payload("普通内容", "77");

        Assert.True(RecentMessageCache.IsTargetMessage(json, "cmid-1", "77"));
    }

    [Fact]
    public void 不相关的消息不命中()
    {
        var json = Payload("普通内容", "cmid-2");

        Assert.False(RecentMessageCache.IsTargetMessage(json, "cmid-1", "77"));
    }

    [Fact]
    public void 正文里出现同样的messageId片段时不误删()
    {
        // 回归测试：用户手打一段 JSON 作为消息内容
        var json = Payload("""看看这段 {"messageId":"cmid-1"} 是什么""", "cmid-999");

        Assert.False(RecentMessageCache.IsTargetMessage(json, "cmid-1", "77"));
    }

    [Fact]
    public void 正文里出现数据库ID片段时不误删()
    {
        var json = Payload("""引用 {"messageId":"77"} 这条""", "cmid-999");

        Assert.False(RecentMessageCache.IsTargetMessage(json, "cmid-1", "77"));
    }

    [Fact]
    public void 解析失败时按不匹配处理_宁可留着也不误删()
    {
        Assert.False(RecentMessageCache.IsTargetMessage("这不是 JSON", "cmid-1", "77"));
        Assert.False(RecentMessageCache.IsTargetMessage(string.Empty, "cmid-1", "77"));
    }

    [Fact]
    public void 缓存里没有消息号时不匹配()
    {
        var json = Payload("内容", string.Empty);

        Assert.False(RecentMessageCache.IsTargetMessage(json, "cmid-1", "77"));
    }
}
