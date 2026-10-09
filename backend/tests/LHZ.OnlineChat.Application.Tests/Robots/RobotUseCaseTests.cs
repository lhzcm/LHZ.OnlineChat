using LHZ.OnlineChat.Application.Robots;
using LHZ.OnlineChat.Application.Robots.Commands;
using LHZ.OnlineChat.Application.Robots.EventHandlers;
using LHZ.OnlineChat.Application.Tests.TestDoubles;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Robots;
using Microsoft.Extensions.Logging.Abstractions;

namespace LHZ.OnlineChat.Application.Tests.Robots;

public class CreateRobotTests
{
    private readonly ApplicationTestContext _ctx = new();

    private CreateRobotHandler Handler()
        => new(_ctx.Robots, _ctx.Users, _ctx.Friendships, _ctx.Cipher, _ctx.WebhookTargets, _ctx.Events, _ctx.Clock);

    [Fact]
    public async Task 创建成功_同时建账号建配置建好友()
    {
        var owner = _ctx.GivenUser();

        var result = await Handler().Handle(new CreateRobotCommand
        {
            OwnerId = owner.Id, Name = "助理", WebhookUrl = "https://example.com/hook"
        }, default);

        Assert.True(result.Success);
        Assert.Equal("助理", result.Data!.Name);

        // 机器人账号（IsBot）
        var botUser = Assert.Single(_ctx.Users.All, u => u.IsBot);
        Assert.Equal("助理", botUser.Nickname);
        Assert.Null(botUser.Email);
        Assert.Null(botUser.PasswordHash);

        // 配置
        var robot = Assert.Single(_ctx.Robots.All);
        Assert.Equal(botUser.Id, robot.UserId);
        Assert.Equal(owner.Id, robot.OwnerId);

        // 与创建者自动成为好友（私聊即触发）
        Assert.True(await _ctx.Friendships.AreFriendsAsync(owner.Id, botUser.Id));
    }

    [Fact]
    public async Task 令牌在主键回填之后生成()
    {
        // 令牌是「加密后的主键」，插入前生成会得到 tok-0
        var owner = _ctx.GivenUser();

        var result = await Handler().Handle(new CreateRobotCommand
        {
            OwnerId = owner.Id, Name = "助理", WebhookUrl = ""
        }, default);

        var robot = Assert.Single(_ctx.Robots.All);
        Assert.Equal(_ctx.Cipher.Encode(robot.Id), result.Data!.Token);
        Assert.Equal(robot.Id, _ctx.Cipher.Decode(result.Data.Token));
        Assert.NotEqual(0, robot.Id);
    }

    [Fact]
    public async Task Webhook留空即纯推送模式()
    {
        var owner = _ctx.GivenUser();

        var result = await Handler().Handle(new CreateRobotCommand
        {
            OwnerId = owner.Id, Name = "推送机器人", WebhookUrl = ""
        }, default);

        Assert.Equal(string.Empty, result.Data!.WebhookUrl);
        Assert.False(Assert.Single(_ctx.Robots.All).RespondsToMessages);
    }

    [Fact]
    public async Task 非法Webhook协议被拒且不建任何记录()
    {
        var owner = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new CreateRobotCommand
            {
                OwnerId = owner.Id, Name = "助理", WebhookUrl = "ftp://bad"
            }, default));

        Assert.Equal("Webhook 地址必须是 http/https 开头", ex.Message);
        Assert.Empty(_ctx.Robots.All);
        Assert.DoesNotContain(_ctx.Users.All, u => u.IsBot);
    }

    [Fact]
    public async Task 名称为空时被拒()
    {
        var owner = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new CreateRobotCommand { OwnerId = owner.Id, Name = "  ", WebhookUrl = "" }, default));

        Assert.Equal("机器人名称不能为空", ex.Message);
    }

    [Fact]
    public async Task 发出机器人账号创建事件()
    {
        var owner = _ctx.GivenUser();

        await Handler().Handle(new CreateRobotCommand
        {
            OwnerId = owner.Id, Name = "助理", WebhookUrl = ""
        }, default);

        var e = _ctx.Events.SingleEvent<Domain.Users.BotAccountCreated>();
        Assert.Equal(owner.Id, e.OwnerId);
        Assert.Equal("助理", e.Name);
        Assert.NotEqual(0, e.UserId);
    }
}

/// <summary>
/// Webhook 出站目标校验（SSRF 防护）。
/// 这个地址由用户填写、由服务端主动 POST，默认必须挡住内网/本机目标。
/// </summary>
public class RobotWebhookTargetTests
{
    private readonly ApplicationTestContext _ctx = new();

    private CreateRobotHandler CreateHandler()
        => new(_ctx.Robots, _ctx.Users, _ctx.Friendships, _ctx.Cipher,
            _ctx.WebhookTargets, _ctx.Events, _ctx.Clock);

    private UpdateRobotHandler UpdateHandler()
        => new(_ctx.Robots, _ctx.Users, _ctx.Cipher, _ctx.WebhookTargets, _ctx.Clock);

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://127.0.0.1:9000/hook")]
    [InlineData("http://192.168.1.10:9000/hook")]
    [InlineData("http://postgres:5432/hook")]
    public async Task 创建时拒绝内网回调地址_且不留下半个机器人(string url)
    {
        var owner = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(() => CreateHandler().Handle(
            new CreateRobotCommand { OwnerId = owner.Id, Name = "助理", WebhookUrl = url }, default));

        Assert.Equal(WebhookUrl.BlockedTargetMessage, ex.Message);
        Assert.Empty(_ctx.Robots.All);
        Assert.DoesNotContain(_ctx.Users.All, u => u.IsBot);
    }

    [Fact]
    public async Task 更新时拒绝把地址改成内网_原值不变()
    {
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => UpdateHandler().Handle(
            new UpdateRobotCommand
            {
                OwnerId = owner.Id, RobotId = robot.Id, WebhookUrl = "http://127.0.0.1:9000/hook"
            }, default));

        Assert.Equal(WebhookUrl.BlockedTargetMessage, ex.Message);
        Assert.Equal("https://example.com/hook", robot.WebhookUrlValue);
    }

    [Fact]
    public async Task 配置显式开启后允许内网地址()
    {
        // 自建 Webhook 部署在内网是合理需求，由部署方用开关显式放行
        _ctx.WebhookTargets.AllowPrivateTargets = true;
        var owner = _ctx.GivenUser();

        var result = await CreateHandler().Handle(new CreateRobotCommand
        {
            OwnerId = owner.Id, Name = "内网助理", WebhookUrl = "http://127.0.0.1:9000/hook"
        }, default);

        Assert.True(result.Success);
        Assert.Equal("http://127.0.0.1:9000/hook", Assert.Single(_ctx.Robots.All).WebhookUrlValue);
    }
}

public class UpdateDeleteRobotTests
{
    private readonly ApplicationTestContext _ctx = new();

    private UpdateRobotHandler UpdateHandler()
        => new(_ctx.Robots, _ctx.Users, _ctx.Cipher, _ctx.WebhookTargets, _ctx.Clock);

    [Fact]
    public async Task 改名时同步机器人账号昵称()
    {
        var owner = _ctx.GivenUser();
        var (robot, botUser) = _ctx.GivenRobot(owner.Id, name: "原名");

        var result = await UpdateHandler().Handle(new UpdateRobotCommand
        {
            OwnerId = owner.Id, RobotId = robot.Id, Name = "新名"
        }, default);

        Assert.Equal("新名", result.Data!.Name);
        Assert.Equal("新名", (await _ctx.Users.FindByIdAsync(botUser.Id))!.Nickname);
    }

    [Fact]
    public async Task 未改名时不动账号昵称()
    {
        var owner = _ctx.GivenUser();
        var (robot, botUser) = _ctx.GivenRobot(owner.Id, name: "原名");
        var updatesBefore = _ctx.Users.UpdateCount;

        await UpdateHandler().Handle(new UpdateRobotCommand
        {
            OwnerId = owner.Id, RobotId = robot.Id, Enabled = false
        }, default);

        Assert.Equal(updatesBefore, _ctx.Users.UpdateCount);
        Assert.Equal("原名", (await _ctx.Users.FindByIdAsync(botUser.Id))!.Nickname);
    }

    [Fact]
    public async Task 非创建者更新时报不存在_不泄露存在性()
    {
        var owner = _ctx.GivenUser("创建者", "o@test.local");
        var other = _ctx.GivenUser("别人", "x@test.local");
        var (robot, _) = _ctx.GivenRobot(owner.Id);

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(() => UpdateHandler().Handle(
            new UpdateRobotCommand { OwnerId = other.Id, RobotId = robot.Id, Name = "越权" },
            default));

        Assert.Equal("机器人不存在", ex.Message);
    }

    [Fact]
    public async Task 机器人不存在时抛出()
    {
        var owner = _ctx.GivenUser();

        await Assert.ThrowsAsync<EntityNotFoundException>(() => UpdateHandler().Handle(
            new UpdateRobotCommand { OwnerId = owner.Id, RobotId = 99999, Name = "x" }, default));
    }

    [Fact]
    public async Task 历史数据缺令牌时自动补齐()
    {
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id);
        // 模拟早期数据：令牌列为空
        typeof(Domain.Robots.Robot).GetProperty(nameof(Domain.Robots.Robot.Token))!
            .SetValue(robot, null);
        await _ctx.Robots.UpdateAsync(robot);

        var result = await UpdateHandler().Handle(new UpdateRobotCommand
        {
            OwnerId = owner.Id, RobotId = robot.Id, Enabled = true
        }, default);

        Assert.Equal(_ctx.Cipher.Encode(robot.Id), result.Data!.Token);
    }

    [Fact]
    public async Task 删除机器人时级联清理账号好友与群成员()
    {
        var owner = _ctx.GivenUser();
        var (robot, botUser) = _ctx.GivenRobot(owner.Id);
        var group = _ctx.GivenGroup(owner.Id, botUser.Id);

        var result = await new DeleteRobotHandler(
                _ctx.Robots, _ctx.Users, _ctx.Friendships, _ctx.GroupMembers)
            .Handle(new DeleteRobotCommand { RobotId = robot.Id, OwnerId = owner.Id }, default);

        Assert.Equal("机器人已删除", result.Message);
        Assert.Empty(_ctx.Robots.All);
        Assert.Null(await _ctx.Users.FindByIdAsync(botUser.Id));
        Assert.False(await _ctx.Friendships.AreFriendsAsync(owner.Id, botUser.Id));
        Assert.False(await _ctx.GroupMembers.ExistsAsync(group.Id, botUser.Id));
    }

    [Fact]
    public async Task 非创建者不能删除()
    {
        var owner = _ctx.GivenUser("创建者", "o@test.local");
        var other = _ctx.GivenUser("别人", "x@test.local");
        var (robot, _) = _ctx.GivenRobot(owner.Id);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => new DeleteRobotHandler(
                _ctx.Robots, _ctx.Users, _ctx.Friendships, _ctx.GroupMembers)
            .Handle(new DeleteRobotCommand { RobotId = robot.Id, OwnerId = other.Id }, default));

        Assert.Single(_ctx.Robots.All);
    }

    [Fact]
    public async Task 管理后台删除时绕过创建者校验()
    {
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id);

        var result = await new DeleteRobotHandler(
                _ctx.Robots, _ctx.Users, _ctx.Friendships, _ctx.GroupMembers)
            .Handle(new DeleteRobotCommand { RobotId = robot.Id, OwnerId = null }, default);

        Assert.True(result.Success);
        Assert.Empty(_ctx.Robots.All);
    }

    [Fact]
    public async Task 我的机器人列表只含自己创建的()
    {
        var owner = _ctx.GivenUser("我", "o@test.local");
        var other = _ctx.GivenUser("别人", "x@test.local");
        _ctx.GivenRobot(owner.Id, name: "我的");
        _ctx.GivenRobot(other.Id, name: "别人的");

        var result = await new GetMyRobotsHandler(_ctx.Robots, _ctx.Cipher).Handle(
            new GetMyRobotsQuery { OwnerId = owner.Id }, default);

        Assert.Equal("我的", Assert.Single(result.Data!).Name);
    }
}

public class GroupRobotTests
{
    private readonly ApplicationTestContext _ctx = new();

    private AddGroupRobotHandler AddHandler()
        => new(_ctx.GroupMembers, _ctx.GroupMessages, _ctx.Robots, _ctx.Clock);

    [Fact]
    public async Task 群主可拉自己的机器人进群()
    {
        var owner = _ctx.GivenUser();
        var (_, botUser) = _ctx.GivenRobot(owner.Id, name: "助理");
        var group = _ctx.GivenGroup(owner.Id);

        var result = await AddHandler().Handle(new AddGroupRobotCommand
        {
            GroupId = group.Id, OperatorId = owner.Id, RobotUserId = botUser.Id
        }, default);

        Assert.Equal("已添加机器人「助理」", result.Message);
        Assert.True(await _ctx.GroupMembers.ExistsAsync(group.Id, botUser.Id));
    }

    [Fact]
    public async Task 只能拉自己创建的机器人()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var other = _ctx.GivenUser("别人", "x@test.local");
        var (_, botUser) = _ctx.GivenRobot(other.Id);
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => AddHandler().Handle(
            new AddGroupRobotCommand
            {
                GroupId = group.Id, OperatorId = owner.Id, RobotUserId = botUser.Id
            }, default));

        Assert.Equal("只能添加自己创建的机器人", ex.Message);
    }

    [Fact]
    public async Task 普通成员不能拉机器人()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var (_, botUser) = _ctx.GivenRobot(member.Id);
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => AddHandler().Handle(
            new AddGroupRobotCommand
            {
                GroupId = group.Id, OperatorId = member.Id, RobotUserId = botUser.Id
            }, default));

        Assert.Equal("只有群主或管理员可以添加机器人", ex.Message);
    }

    [Fact]
    public async Task 重复拉入被拒()
    {
        var owner = _ctx.GivenUser();
        var (_, botUser) = _ctx.GivenRobot(owner.Id);
        var group = _ctx.GivenGroup(owner.Id, botUser.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => AddHandler().Handle(
            new AddGroupRobotCommand
            {
                GroupId = group.Id, OperatorId = owner.Id, RobotUserId = botUser.Id
            }, default));

        Assert.Equal("该机器人已在群中", ex.Message);
    }

    [Fact]
    public async Task 机器人入群时已读游标设为最新_不补发历史()
    {
        var owner = _ctx.GivenUser();
        var (_, botUser) = _ctx.GivenRobot(owner.Id);
        var group = _ctx.GivenGroup(owner.Id);
        var latest = _ctx.GivenGroupMessage(group.Id, owner.Id, "历史消息");

        await AddHandler().Handle(new AddGroupRobotCommand
        {
            GroupId = group.Id, OperatorId = owner.Id, RobotUserId = botUser.Id
        }, default);

        Assert.Equal(latest.Id,
            (await _ctx.GroupMembers.FindAsync(group.Id, botUser.Id))!.LastReadMessageId);
    }

    [Fact]
    public async Task 移除群机器人()
    {
        var owner = _ctx.GivenUser();
        var (_, botUser) = _ctx.GivenRobot(owner.Id);
        var group = _ctx.GivenGroup(owner.Id, botUser.Id);

        var result = await new RemoveGroupRobotHandler(_ctx.GroupMembers).Handle(
            new RemoveGroupRobotCommand
            {
                GroupId = group.Id, OperatorId = owner.Id, RobotUserId = botUser.Id
            }, default);

        Assert.Equal("已移除机器人", result.Message);
        Assert.False(await _ctx.GroupMembers.ExistsAsync(group.Id, botUser.Id));
    }

    [Fact]
    public async Task 移除不在群的机器人时被拒()
    {
        var owner = _ctx.GivenUser();
        var (_, botUser) = _ctx.GivenRobot(owner.Id);
        var group = _ctx.GivenGroup(owner.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => new RemoveGroupRobotHandler(_ctx.GroupMembers).Handle(
                new RemoveGroupRobotCommand
                {
                    GroupId = group.Id, OperatorId = owner.Id, RobotUserId = botUser.Id
                }, default));

        Assert.Equal("该机器人不在群中", ex.Message);
    }

    [Fact]
    public async Task 群内机器人列表只含机器人()
    {
        var owner = _ctx.GivenUser();
        var (_, botUser) = _ctx.GivenRobot(owner.Id);
        var group = _ctx.GivenGroup(owner.Id, botUser.Id);

        var result = await new GetGroupRobotsHandler(_ctx.GroupMembers, _ctx.Robots).Handle(
            new GetGroupRobotsQuery { GroupId = group.Id, RequesterId = owner.Id }, default);

        Assert.Equal(botUser.Id, Assert.Single(result.Data!).UserId);
    }
}

public class RobotReplyTests
{
    private readonly ApplicationTestContext _ctx = new();

    private SendRobotMessageHandler SendHandler()
        => new(_ctx.Robots, _ctx.Users, _ctx.PrivateMessages, _ctx.GroupMessages,
            _ctx.GroupMembers, _ctx.Cache, _ctx.Notifier, _ctx.Clock);

    [Fact]
    public async Task 机器人私聊回复走统一消息通道()
    {
        var owner = _ctx.GivenUser();
        var (robot, botUser) = _ctx.GivenRobot(owner.Id);

        await SendHandler().Handle(new SendRobotMessageCommand
        {
            RobotId = robot.Id,
            SessionType = ChatSessionType.Private,
            SessionId = owner.Id,
            Content = "机器人回复"
        }, default);

        // 落库
        var message = Assert.Single(_ctx.PrivateMessages.All);
        Assert.Equal(botUser.Id, message.SenderId);
        Assert.Equal(owner.Id, message.ReceiverId);
        // 缓存
        Assert.Single(_ctx.Cache.Appended);
        // 广播
        Assert.Single(_ctx.Notifier.OfKind(PushKind.PrivateMessage));
    }

    [Fact]
    public async Task 机器人回复累计推送次数()
    {
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id);
        var command = new SendRobotMessageCommand
        {
            RobotId = robot.Id,
            SessionType = ChatSessionType.Private,
            SessionId = owner.Id,
            Content = "回复"
        };

        await SendHandler().Handle(command, default);
        await SendHandler().Handle(command, default);

        Assert.Equal(2, (await _ctx.Robots.FindByIdAsync(robot.Id))!.PushCount);
    }

    [Fact]
    public async Task 机器人群回复广播给全部成员()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var (robot, botUser) = _ctx.GivenRobot(owner.Id);
        var group = _ctx.GivenGroup(owner.Id, member.Id, botUser.Id);

        await SendHandler().Handle(new SendRobotMessageCommand
        {
            RobotId = robot.Id,
            SessionType = ChatSessionType.Group,
            SessionId = group.Id,
            Content = "群里回复"
        }, default);

        Assert.Equal(3, _ctx.Notifier.OfKind(PushKind.GroupMessage).Count());
        Assert.Single(_ctx.GroupMessages.All);
    }

    [Fact]
    public async Task 机器人回复可携带引用()
    {
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id);

        await SendHandler().Handle(new SendRobotMessageCommand
        {
            RobotId = robot.Id,
            SessionType = ChatSessionType.Private,
            SessionId = owner.Id,
            Content = "回复",
            QuotedMessageId = "origin-1",
            QuotedContent = "你好",
            QuotedSenderName = "张三"
        }, default);

        var message = Assert.Single(_ctx.PrivateMessages.All);
        Assert.Equal("origin-1", message.ReplyMessageId);
        Assert.Equal("你好", message.ReplyContent);
    }
}

public class HandleRobotReplyTests
{
    private readonly ApplicationTestContext _ctx = new();

    /// <summary>第三方推送用例依赖 ISender 去发 SendRobotMessageCommand，这里用直连替身</summary>
    private sealed class DirectSender : MediatR.ISender
    {
        private readonly ApplicationTestContext _ctx;

        internal DirectSender(ApplicationTestContext ctx) => _ctx = ctx;

        internal List<SendRobotMessageCommand> Sent { get; } = new();

        public async Task<TResponse> Send<TResponse>(
            MediatR.IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is SendRobotMessageCommand command)
            {
                Sent.Add(command);
                await new SendRobotMessageHandler(
                        _ctx.Robots, _ctx.Users, _ctx.PrivateMessages, _ctx.GroupMessages,
                        _ctx.GroupMembers, _ctx.Cache, _ctx.Notifier, _ctx.Clock)
                    .Handle(command, cancellationToken);
                return (TResponse)(object)MediatR.Unit.Value;
            }

            throw new NotSupportedException($"测试替身未覆盖 {request.GetType().Name}");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : MediatR.IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            MediatR.IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private (HandleRobotReplyHandler Handler, DirectSender Sender) Build()
    {
        var sender = new DirectSender(_ctx);
        return (new HandleRobotReplyHandler(
            _ctx.Robots, _ctx.Cipher, _ctx.Signer,
            _ctx.Friendships, _ctx.GroupMembers, sender), sender);
    }

    [Fact]
    public async Task 第三方私聊推送成功()
    {
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id);
        var (handler, sender) = Build();

        var result = await handler.Handle(new HandleRobotReplyCommand
        {
            Token = _ctx.Cipher.Encode(robot.Id),
            RawBody = "{}",
            Payload = new RobotReplyPayload
            {
                SessionType = "private", SessionId = owner.Id, Content = "推送内容"
            }
        }, default);

        Assert.Equal("已发送", result.Message);
        Assert.Single(sender.Sent);
        Assert.Equal("推送内容", Assert.Single(_ctx.PrivateMessages.All).Content);
    }

    [Fact]
    public async Task 令牌无效时被拒()
    {
        var (handler, _) = Build();

        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new HandleRobotReplyCommand
            {
                Token = "garbage", RawBody = "{}",
                Payload = new RobotReplyPayload { SessionId = 1, Content = "x" }
            }, default));

        Assert.Equal("无效的机器人标识", ex.Message);
    }

    [Fact]
    public async Task 请求体为空时被拒()
    {
        var (handler, _) = Build();

        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new HandleRobotReplyCommand { Token = "tok-1", RawBody = "  " }, default));

        Assert.Equal("请求体不能为空", ex.Message);
    }

    [Fact]
    public async Task 配了密钥时验签失败被拒()
    {
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id, secret: "s3cret");
        _ctx.Signer.VerifyResult = false;
        var (handler, _) = Build();

        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new HandleRobotReplyCommand
            {
                Token = _ctx.Cipher.Encode(robot.Id), RawBody = "{}", Signature = "bad",
                Payload = new RobotReplyPayload
                {
                    SessionType = "private", SessionId = owner.Id, Content = "x"
                }
            }, default));

        Assert.Equal("签名验证失败", ex.Message);
    }

    [Fact]
    public async Task 未配密钥时免验签()
    {
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id, secret: null);
        _ctx.Signer.VerifyResult = false;   // 即使验签器返回 false 也不应被调用
        var (handler, _) = Build();

        var result = await handler.Handle(new HandleRobotReplyCommand
        {
            Token = _ctx.Cipher.Encode(robot.Id), RawBody = "{}",
            Payload = new RobotReplyPayload
            {
                SessionType = "private", SessionId = owner.Id, Content = "x"
            }
        }, default);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task 私聊目标非好友时被拒_防骚扰()
    {
        var owner = _ctx.GivenUser("创建者", "o@test.local");
        var stranger = _ctx.GivenUser("陌生人", "s@test.local");
        var (robot, _) = _ctx.GivenRobot(owner.Id);
        var (handler, _) = Build();

        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new HandleRobotReplyCommand
            {
                Token = _ctx.Cipher.Encode(robot.Id), RawBody = "{}",
                Payload = new RobotReplyPayload
                {
                    SessionType = "private", SessionId = stranger.Id, Content = "骚扰"
                }
            }, default));

        Assert.Equal("目标用户与机器人不是好友关系", ex.Message);
        Assert.Empty(_ctx.PrivateMessages.All);
    }

    [Fact]
    public async Task 群推送时机器人须在群中()
    {
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id);
        var group = _ctx.GivenGroup(owner.Id);
        var (handler, _) = Build();

        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new HandleRobotReplyCommand
            {
                Token = _ctx.Cipher.Encode(robot.Id), RawBody = "{}",
                Payload = new RobotReplyPayload
                {
                    SessionType = "group", SessionId = group.Id, Content = "x"
                }
            }, default));

        Assert.Equal("机器人不在该群中", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task 回复内容为空时被拒(string content)
    {
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id);
        var (handler, _) = Build();

        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new HandleRobotReplyCommand
            {
                Token = _ctx.Cipher.Encode(robot.Id), RawBody = "{}",
                Payload = new RobotReplyPayload
                {
                    SessionType = "private", SessionId = owner.Id, Content = content
                }
            }, default));

        Assert.Equal("回复内容不能为空", ex.Message);
    }

    [Fact]
    public async Task 无效会话类型被拒()
    {
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id);
        var (handler, _) = Build();

        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new HandleRobotReplyCommand
            {
                Token = _ctx.Cipher.Encode(robot.Id), RawBody = "{}",
                Payload = new RobotReplyPayload
                {
                    SessionType = "bogus", SessionId = owner.Id, Content = "x"
                }
            }, default));

        Assert.Equal("无效的会话类型", ex.Message);
    }

    [Fact]
    public async Task 请求体解析失败时被拒()
    {
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id);
        var (handler, _) = Build();

        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new HandleRobotReplyCommand
            {
                Token = _ctx.Cipher.Encode(robot.Id), RawBody = "not-json", Payload = null
            }, default));

        Assert.Equal("请求格式错误", ex.Message);
    }
}

public class TestRobotTests
{
    private readonly ApplicationTestContext _ctx = new();

    private TestRobotHandler Handler()
        => new(_ctx.Robots, _ctx.Users, _ctx.Webhooks, _ctx.Clock);

    [Fact]
    public async Task 测试触发返回同步回复()
    {
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id);
        _ctx.Webhooks.NextResult = new Abstractions.WebhookDispatchResult
        {
            Success = true, Reply = "机器人说你好"
        };

        var result = await Handler().Handle(new TestRobotCommand
        {
            OwnerId = owner.Id, RobotId = robot.Id, Content = "测试"
        }, default);

        Assert.True(result.Data!.Success);
        Assert.Equal("机器人说你好", result.Data.Reply);
        Assert.Single(_ctx.Webhooks.Dispatched);
    }

    [Fact]
    public async Task 事件载荷以创建者为发送者()
    {
        var owner = _ctx.GivenUser(nickname: "创建者");
        var (robot, botUser) = _ctx.GivenRobot(owner.Id);

        await Handler().Handle(new TestRobotCommand
        {
            OwnerId = owner.Id, RobotId = robot.Id, Content = "测试内容"
        }, default);

        var payload = Assert.Single(_ctx.Webhooks.Dispatched);
        Assert.Equal(owner.Id, payload.From.UserId);
        Assert.Equal("创建者", payload.From.Name);
        Assert.Equal(botUser.Id, payload.Robot.UserId);
        Assert.True(payload.Robot.IsBot);
        Assert.Equal("测试内容", payload.Message.Content);
    }

    [Fact]
    public async Task 内容为空时用默认问候语()
    {
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id);

        await Handler().Handle(new TestRobotCommand
        {
            OwnerId = owner.Id, RobotId = robot.Id, Content = "  "
        }, default);

        Assert.Equal("你好", Assert.Single(_ctx.Webhooks.Dispatched).Message.Content);
    }

    [Fact]
    public async Task 纯推送机器人不支持测试触发()
    {
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id, webhook: "");

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new TestRobotCommand { OwnerId = owner.Id, RobotId = robot.Id }, default));

        Assert.Equal("该机器人未配置 Webhook 地址，仅支持第三方主动推送", ex.Message);
        Assert.Empty(_ctx.Webhooks.Dispatched);
    }

    [Fact]
    public async Task 非创建者不能测试()
    {
        var owner = _ctx.GivenUser("创建者", "o@test.local");
        var other = _ctx.GivenUser("别人", "x@test.local");
        var (robot, _) = _ctx.GivenRobot(owner.Id);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => Handler().Handle(
            new TestRobotCommand { OwnerId = other.Id, RobotId = robot.Id }, default));
    }

    [Fact]
    public async Task 调用失败时返回失败信息()
    {
        var owner = _ctx.GivenUser();
        var (robot, _) = _ctx.GivenRobot(owner.Id);
        _ctx.Webhooks.NextResult = new Abstractions.WebhookDispatchResult
        {
            Success = false, Message = "Webhook 调用失败（已重试 1 次）"
        };

        var result = await Handler().Handle(new TestRobotCommand
        {
            OwnerId = owner.Id, RobotId = robot.Id
        }, default);

        Assert.False(result.Data!.Success);
        Assert.Equal("Webhook 调用失败（已重试 1 次）", result.Message);
    }
}

/// <summary>
/// 机器人触发链路：消息事件 → 订阅方判断是否该触发 → 后台调度 Webhook。
/// 改造前这段逻辑硬编码在 WsMessageHandler 里，消息链路因此依赖机器人功能。
/// </summary>
public class RobotTriggerTests
{
    private readonly ApplicationTestContext _ctx = new();

    private TriggerRobotOnPrivateMessage PrivateTrigger()
        => new(_ctx.Robots, _ctx.Users, _ctx.PrivateMessages, _ctx.RobotConversations,
            NullLogger<TriggerRobotOnPrivateMessage>.Instance);

    private TriggerRobotOnGroupMessage GroupTrigger()
        => new(_ctx.Robots, _ctx.Users, _ctx.Groups, _ctx.GroupMessages,
            _ctx.RobotConversations, NullLogger<TriggerRobotOnGroupMessage>.Instance);

    [Fact]
    public async Task 私聊给机器人时触发调度()
    {
        var owner = _ctx.GivenUser();
        var (robot, botUser) = _ctx.GivenRobot(owner.Id);
        var message = _ctx.GivenPrivateMessage(owner.Id, botUser.Id, "你好机器人");

        _ctx.Events.Subscribe(PrivateTrigger());
        await _ctx.Events.DispatchAsync(new[]
        {
            new PrivateMessageSent(message.Id, owner.Id, botUser.Id, _ctx.Now)
        });

        var dispatched = Assert.Single(_ctx.RobotConversations.Dispatched);
        Assert.Equal(robot.Id, dispatched.RobotId);
        Assert.Equal("你好机器人", dispatched.Payload.Message.Content);
        Assert.Equal(ChatSessionType.Private, dispatched.Target.SessionType);
        Assert.Equal(owner.Id, dispatched.Target.SessionId);
    }

    [Fact]
    public async Task 收件人不是机器人时不触发()
    {
        var (a, b) = _ctx.GivenFriends();
        var message = _ctx.GivenPrivateMessage(a.Id, b.Id);

        _ctx.Events.Subscribe(PrivateTrigger());
        await _ctx.Events.DispatchAsync(new[]
        {
            new PrivateMessageSent(message.Id, a.Id, b.Id, _ctx.Now)
        });

        Assert.Empty(_ctx.RobotConversations.Dispatched);
    }

    [Fact]
    public async Task 纯推送模式的机器人不被消息触发()
    {
        var owner = _ctx.GivenUser();
        var (_, botUser) = _ctx.GivenRobot(owner.Id, webhook: "");
        var message = _ctx.GivenPrivateMessage(owner.Id, botUser.Id);

        _ctx.Events.Subscribe(PrivateTrigger());
        await _ctx.Events.DispatchAsync(new[]
        {
            new PrivateMessageSent(message.Id, owner.Id, botUser.Id, _ctx.Now)
        });

        Assert.Empty(_ctx.RobotConversations.Dispatched);
    }

    [Fact]
    public async Task 已停用的机器人不被触发()
    {
        var owner = _ctx.GivenUser();
        var (_, botUser) = _ctx.GivenRobot(owner.Id, enabled: false);
        var message = _ctx.GivenPrivateMessage(owner.Id, botUser.Id);

        _ctx.Events.Subscribe(PrivateTrigger());
        await _ctx.Events.DispatchAsync(new[]
        {
            new PrivateMessageSent(message.Id, owner.Id, botUser.Id, _ctx.Now)
        });

        Assert.Empty(_ctx.RobotConversations.Dispatched);
    }

    [Fact]
    public async Task 机器人之间互不触发_防死循环()
    {
        var owner = _ctx.GivenUser();
        var (_, botA) = _ctx.GivenRobot(owner.Id, name: "甲");
        var (_, botB) = _ctx.GivenRobot(owner.Id, name: "乙");
        var message = _ctx.GivenPrivateMessage(botA.Id, botB.Id);

        _ctx.Events.Subscribe(PrivateTrigger());
        await _ctx.Events.DispatchAsync(new[]
        {
            new PrivateMessageSent(message.Id, botA.Id, botB.Id, _ctx.Now)
        });

        Assert.Empty(_ctx.RobotConversations.Dispatched);
    }

    [Fact]
    public async Task 群里被At时触发()
    {
        var owner = _ctx.GivenUser();
        var (robot, botUser) = _ctx.GivenRobot(owner.Id);
        var group = _ctx.GivenGroup(owner.Id, botUser.Id);

        var message = Domain.Messaging.GroupMessage.Send(
            group.Id, owner.Id, "@助理 帮我查一下", MessageKind.Text, null,
            MentionList.From(new[] { botUser.Id }), null, _ctx.Now);
        await _ctx.GroupMessages.AddAsync(message);

        _ctx.Events.Subscribe(GroupTrigger());
        await _ctx.Events.DispatchAsync(new[]
        {
            new GroupMessageSent(message.Id, group.Id, owner.Id, _ctx.Now)
        });

        var dispatched = Assert.Single(_ctx.RobotConversations.Dispatched);
        Assert.Equal(robot.Id, dispatched.RobotId);
        Assert.Equal(ChatSessionType.Group, dispatched.Target.SessionType);
        Assert.Equal(group.Id, dispatched.Target.SessionId);
        Assert.Contains(botUser.Id, dispatched.Payload.Mentions);
    }

    [Fact]
    public async Task 群消息未At机器人时不触发()
    {
        var owner = _ctx.GivenUser();
        var (_, botUser) = _ctx.GivenRobot(owner.Id);
        var group = _ctx.GivenGroup(owner.Id, botUser.Id);
        var message = _ctx.GivenGroupMessage(group.Id, owner.Id, "普通发言");

        _ctx.Events.Subscribe(GroupTrigger());
        await _ctx.Events.DispatchAsync(new[]
        {
            new GroupMessageSent(message.Id, group.Id, owner.Id, _ctx.Now)
        });

        Assert.Empty(_ctx.RobotConversations.Dispatched);
    }

    [Fact]
    public async Task At了多个机器人时逐个触发()
    {
        var owner = _ctx.GivenUser();
        var (_, botA) = _ctx.GivenRobot(owner.Id, name: "甲");
        var (_, botB) = _ctx.GivenRobot(owner.Id, name: "乙");
        var group = _ctx.GivenGroup(owner.Id, botA.Id, botB.Id);

        var message = Domain.Messaging.GroupMessage.Send(
            group.Id, owner.Id, "@甲 @乙", MessageKind.Text, null,
            MentionList.From(new[] { botA.Id, botB.Id }), null, _ctx.Now);
        await _ctx.GroupMessages.AddAsync(message);

        _ctx.Events.Subscribe(GroupTrigger());
        await _ctx.Events.DispatchAsync(new[]
        {
            new GroupMessageSent(message.Id, group.Id, owner.Id, _ctx.Now)
        });

        Assert.Equal(2, _ctx.RobotConversations.Dispatched.Count);
    }

    [Fact]
    public async Task At的是普通成员时不触发()
    {
        var owner = _ctx.GivenUser("群主", "o@test.local");
        var member = _ctx.GivenUser("成员", "m@test.local");
        var group = _ctx.GivenGroup(owner.Id, member.Id);

        var message = Domain.Messaging.GroupMessage.Send(
            group.Id, owner.Id, "@成员", MessageKind.Text, null,
            MentionList.From(new[] { member.Id }), null, _ctx.Now);
        await _ctx.GroupMessages.AddAsync(message);

        _ctx.Events.Subscribe(GroupTrigger());
        await _ctx.Events.DispatchAsync(new[]
        {
            new GroupMessageSent(message.Id, group.Id, owner.Id, _ctx.Now)
        });

        Assert.Empty(_ctx.RobotConversations.Dispatched);
    }
}

/// <summary>
/// 名称校验顺序的回归测试。
/// 创建机器人时会先建 User 账号，若不先按「机器人名称」规则校验，
/// 提示语会变成账号那边的「昵称不能为空」——对用户是错的语境。
/// </summary>
public class RobotNameValidationOrderTests
{
    private readonly ApplicationTestContext _ctx = new();

    private CreateRobotHandler Handler()
        => new(_ctx.Robots, _ctx.Users, _ctx.Friendships, _ctx.Cipher, _ctx.WebhookTargets, _ctx.Events, _ctx.Clock);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task 名称为空时用机器人语境的提示语(string? name)
    {
        var owner = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new CreateRobotCommand { OwnerId = owner.Id, Name = name!, WebhookUrl = "" }, default));

        Assert.Equal("机器人名称不能为空", ex.Message);
    }

    [Fact]
    public async Task 名称超长时用机器人语境的提示语()
    {
        var owner = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new CreateRobotCommand
            {
                OwnerId = owner.Id,
                Name = new string('x', Domain.Robots.Robot.MaxNameLength + 1),
                WebhookUrl = ""
            }, default));

        Assert.Equal($"机器人名称不能超过 {Domain.Robots.Robot.MaxNameLength} 个字符", ex.Message);
    }

    [Fact]
    public async Task 校验失败时不留下机器人账号()
    {
        var owner = _ctx.GivenUser();

        await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new CreateRobotCommand { OwnerId = owner.Id, Name = "  ", WebhookUrl = "" }, default));

        Assert.DoesNotContain(_ctx.Users.All, u => u.IsBot);
        Assert.Empty(_ctx.Robots.All);
    }

    [Fact]
    public async Task 名称首尾空白被去除后同时用于账号与配置()
    {
        var owner = _ctx.GivenUser();

        await Handler().Handle(new CreateRobotCommand
        {
            OwnerId = owner.Id, Name = "  助理  ", WebhookUrl = ""
        }, default);

        Assert.Equal("助理", Assert.Single(_ctx.Robots.All).Name);
        Assert.Equal("助理", Assert.Single(_ctx.Users.All, u => u.IsBot).Nickname);
    }
}
