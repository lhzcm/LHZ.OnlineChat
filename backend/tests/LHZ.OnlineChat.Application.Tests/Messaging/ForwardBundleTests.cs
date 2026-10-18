using LHZ.OnlineChat.Application.Messaging;
using LHZ.OnlineChat.Application.Messaging.Commands;
using LHZ.OnlineChat.Application.Tests.TestDoubles;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Application.Tests.Messaging;

/// <summary>
/// 「聊天记录」合并转发载荷（messageType = 4）的校验。
///
/// 为什么单独测：content 是 JSON 快照，Domain 只校验"非空 + 长度"，
/// 结构校验在应用层。一旦放过畸形载荷，前端解析失败只会渲染成空卡片，
/// 而发送方拿不到任何提示。
/// </summary>
public class ForwardBundleTests
{
    private static string Bundle(params (string Name, string Content, int Type)[] items)
    {
        var json = items.Select(i =>
            $$"""{"n":{{System.Text.Json.JsonSerializer.Serialize(i.Name)}},"c":{{System.Text.Json.JsonSerializer.Serialize(i.Content)}},"t":{{i.Type}}}""");
        return $$"""{"v":1,"items":[{{string.Join(",", json)}}]}""";
    }

    [Fact(DisplayName = "合法的转发载荷通过校验")]
    public void EnsureValid_ValidBundle_Passes()
    {
        ForwardBundle.EnsureValid(Bundle(("张三", "你好", 0), ("李四", "# 标题", 3), ("王五", "/uploads/a.png", 1)));
    }

    [Fact(DisplayName = "单条转发同样合法（卡片里只有一条也算一次转发）")]
    public void EnsureValid_SingleItem_Passes()
    {
        ForwardBundle.EnsureValid(Bundle(("张三", "就一句", 0)));
    }

    [Fact(DisplayName = "缺少 t 时按文字处理（兼容老客户端）")]
    public void EnsureValid_MissingKind_DefaultsToText()
    {
        ForwardBundle.EnsureValid("""{"items":[{"n":"张三","c":"你好"}]}""");
    }

    [Theory(DisplayName = "畸形载荷被拒绝")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("不是 JSON")]
    [InlineData("{}")]
    [InlineData("""{"items":{}}""")]
    [InlineData("""{"items":[]}""")]
    [InlineData("""{"items":[{}]}""")]                     // 缺内容
    [InlineData("""{"items":[{"n":"张三","c":""}]}""")]    // 内容为空
    [InlineData("""{"items":[{"n":"张三","c":"x","t":2}]}""")]  // File 不允许
    [InlineData("""{"items":[{"n":"张三","c":"x","t":4}]}""")]  // 嵌套转发不允许
    public void EnsureValid_Malformed_Throws(string content)
    {
        Assert.Throws<DomainException>(() => ForwardBundle.EnsureValid(content));
    }

    [Fact(DisplayName = "超过条数上限被拒绝")]
    public void EnsureValid_TooManyItems_Throws()
    {
        var items = Enumerable.Range(0, ForwardBundle.MaxItems + 1)
            .Select(i => ($"用户{i}", "内容", 0))
            .ToArray();

        var ex = Assert.Throws<DomainException>(() => ForwardBundle.EnsureValid(Bundle(items)));

        Assert.Contains(
            ForwardBundle.MaxItems.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ex.Message,
            StringComparison.Ordinal);
    }

    [Fact(DisplayName = "单条内容超长被拒绝")]
    public void EnsureValid_ItemTooLong_Throws()
    {
        var tooLong = new string('A', ForwardBundle.MaxItemLength + 1);

        Assert.Throws<DomainException>(() => ForwardBundle.EnsureValid(Bundle(("张三", tooLong, 0))));
    }

    [Fact(DisplayName = "来源昵称超长被拒绝")]
    public void EnsureValid_SenderNameTooLong_Throws()
    {
        var longName = new string('A', ForwardBundle.MaxSenderNameLength + 1);

        Assert.Throws<DomainException>(() => ForwardBundle.EnsureValid(Bundle((longName, "内容", 0))));
    }
}

/// <summary>
/// 发送链路是否真的调了转发载荷校验（校验函数写对了但忘记接线，等于没做）。
/// </summary>
public class ForwardSendWiringTests
{
    private readonly ApplicationTestContext _ctx = new();

    private const string ValidBundle = """{"v":1,"items":[{"n":"张三","c":"原文","t":0}]}""";

    [Fact(DisplayName = "私聊发送畸形转发载荷被拒，且不落库")]
    public async Task Private_MalformedForward_IsRejectedAndNotPersisted()
    {
        var (a, b) = _ctx.GivenFriends();

        await Assert.ThrowsAsync<DomainException>(() => new SendPrivateMessageHandler(
                _ctx.PrivateMessages, _ctx.Blacklist, _ctx.Friendships, _ctx.Users,
                _ctx.Cache, _ctx.Notifier, _ctx.Events, _ctx.Clock)
            .Handle(new SendPrivateMessageCommand
            {
                SenderId = a.Id, ReceiverId = b.Id, Kind = MessageKind.Forward,
                Content = "不是 JSON", ClientMessageId = "cmid-fwd"
            }, default));

        Assert.Empty(_ctx.PrivateMessages.All);
    }

    [Fact(DisplayName = "私聊发送合法转发载荷成功落库并保留 messageType")]
    public async Task Private_ValidForward_Persists()
    {
        var (a, b) = _ctx.GivenFriends();

        var result = await new SendPrivateMessageHandler(
                _ctx.PrivateMessages, _ctx.Blacklist, _ctx.Friendships, _ctx.Users,
                _ctx.Cache, _ctx.Notifier, _ctx.Events, _ctx.Clock)
            .Handle(new SendPrivateMessageCommand
            {
                SenderId = a.Id, ReceiverId = b.Id, Kind = MessageKind.Forward,
                Content = ValidBundle, ClientMessageId = "cmid-fwd"
            }, default);

        Assert.True(result.Delivered);
        var message = Assert.Single(_ctx.PrivateMessages.All);
        Assert.Equal(MessageKind.Forward, message.Kind);
        Assert.Equal(ValidBundle, message.Content);
    }

    [Fact(DisplayName = "群聊发送畸形转发载荷被拒")]
    public async Task Group_MalformedForward_IsRejected()
    {
        var owner = _ctx.GivenUser("群主", "owner@test.local");
        var group = _ctx.GivenGroup(owner.Id);

        await Assert.ThrowsAsync<DomainException>(() => new SendGroupMessageHandler(
                _ctx.GroupMessages, _ctx.GroupMembers, _ctx.Users, _ctx.Cache,
                _ctx.Notifier, _ctx.Events, _ctx.MuteFormatter, _ctx.Clock)
            .Handle(new SendGroupMessageCommand
            {
                GroupId = group.Id, SenderId = owner.Id, Kind = MessageKind.Forward,
                Content = """{"items":[{"c":""}]}""", ClientMessageId = "cmid-fwd-g"
            }, default));
    }
}
