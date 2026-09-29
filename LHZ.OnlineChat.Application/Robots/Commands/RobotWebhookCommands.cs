using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Application.Robots.EventHandlers;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Friends;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Robots;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Robots.Commands;

/// <summary>
/// 第三方异步回复 / 主动推送：POST /api/robots/{令牌}/reply。
/// 令牌是加密后的机器人 ID（不泄露内部自增 ID）；
/// 签名可选 —— 配置了 WebhookSecret 才强制验签。
/// </summary>
public sealed class HandleRobotReplyCommand : ICommand<ApiResponse>
{
    /// <summary>URL 里的加密令牌</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>原始请求体（验签要用原文，不能用反序列化后再序列化的结果）</summary>
    public string RawBody { get; set; } = string.Empty;

    public string? Signature { get; set; }

    /// <summary>已由表现层反序列化的请求体</summary>
    public RobotReplyPayload? Payload { get; set; }
}

internal sealed class HandleRobotReplyHandler : IRequestHandler<HandleRobotReplyCommand, ApiResponse>
{
    private readonly IRobotRepository _robots;
    private readonly IRobotTokenCipher _cipher;
    private readonly IWebhookSigner _signer;
    private readonly IFriendshipRepository _friendships;
    private readonly IGroupMemberRepository _members;
    private readonly ISender _sender;

    public HandleRobotReplyHandler(
        IRobotRepository robots,
        IRobotTokenCipher cipher,
        IWebhookSigner signer,
        IFriendshipRepository friendships,
        IGroupMemberRepository members,
        ISender sender)
    {
        _robots = robots;
        _cipher = cipher;
        _signer = signer;
        _friendships = friendships;
        _members = members;
        _sender = sender;
    }

    public async Task<ApiResponse> Handle(HandleRobotReplyCommand command, CancellationToken ct)
    {
        DomainException.Ensure(!string.IsNullOrWhiteSpace(command.RawBody), "请求体不能为空");

        var robotId = _cipher.Decode(command.Token);
        DomainException.Ensure(robotId > 0, "无效的机器人标识");

        var robot = await _robots.FindByIdAsync(robotId, ct).ConfigureAwait(false)
                    ?? throw new EntityNotFoundException("机器人不存在");

        // 签名可选：配了密钥才验
        if (robot.SignatureRequired)
        {
            var valid = _signer.Verify(robot.WebhookSecret!, command.RawBody, command.Signature);
            DomainException.Ensure(valid, "签名验证失败");
        }

        var payload = command.Payload ?? throw new DomainException("请求格式错误");
        Robot.EnsureReplyContentValid(payload.Content);

        var sessionType = ChatSessionTypeNames.TryParse(payload.SessionType)
                          ?? throw new DomainException("无效的会话类型");

        // 目标可达性校验：私聊必须是好友（防骚扰），群聊必须机器人在群里
        if (sessionType == ChatSessionType.Private)
        {
            var targetId = (int)payload.SessionId;
            var areFriends = await _friendships
                .AreFriendsAsync(targetId, robot.UserId, ct)
                .ConfigureAwait(false);
            DomainException.Ensure(areFriends, "目标用户与机器人不是好友关系");
        }
        else
        {
            var inGroup = await _members
                .ExistsAsync(payload.SessionId, robot.UserId, ct)
                .ConfigureAwait(false);
            DomainException.Ensure(inGroup, "机器人不在该群中");
        }

        await _sender.Send(new SendRobotMessageCommand
        {
            RobotId = robot.Id,
            SessionType = sessionType,
            SessionId = payload.SessionId,
            Content = payload.Content,
            QuotedMessageId = payload.ReplyTo
        }, ct).ConfigureAwait(false);

        return ApiResponse.Ok("已发送");
    }
}

/// <summary>测试触发：以创建者为发送者构造一条私聊事件，返回机器人的同步回复</summary>
public sealed class TestRobotCommand : ICommand<ApiResponse<RobotTestResult>>
{
    public int OwnerId { get; set; }

    public long RobotId { get; set; }

    public string Content { get; set; } = "你好";
}

internal sealed class TestRobotHandler : IRequestHandler<TestRobotCommand, ApiResponse<RobotTestResult>>
{
    private readonly IRobotRepository _robots;
    private readonly IUserRepository _users;
    private readonly IWebhookDispatcher _dispatcher;
    private readonly IClock _clock;

    public TestRobotHandler(
        IRobotRepository robots, IUserRepository users, IWebhookDispatcher dispatcher, IClock clock)
    {
        _robots = robots;
        _users = users;
        _dispatcher = dispatcher;
        _clock = clock;
    }

    public async Task<ApiResponse<RobotTestResult>> Handle(TestRobotCommand command, CancellationToken ct)
    {
        var robot = await _robots.FindByIdAsync(command.RobotId, ct).ConfigureAwait(false)
                    ?? throw new EntityNotFoundException("机器人不存在");
        robot.EnsureOwnedBy(command.OwnerId);
        robot.EnsureTestable();

        var owner = await _users.FindByIdAsync(command.OwnerId, ct).ConfigureAwait(false);
        var ownerName = owner?.Nickname ?? "我";

        var payload = new WebhookEvent
        {
            Robot = new WebhookActor
            {
                UserId = robot.UserId, Name = robot.Name, Avatar = robot.Avatar, IsBot = true
            },
            Session = new WebhookSession
            {
                Type = ChatSessionType.Private, Id = command.OwnerId, Name = ownerName
            },
            From = new WebhookActor
            {
                UserId = command.OwnerId, Name = ownerName, Avatar = owner?.Avatar
            },
            Message = new WebhookMessage
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Content = string.IsNullOrWhiteSpace(command.Content) ? "你好" : command.Content,
                Kind = MessageKind.Text,
                Timestamp = UtcTime.ToUnixMilliseconds(_clock.UtcNow)
            }
        };

        var result = await _dispatcher.DispatchAsync(robot, payload, ct).ConfigureAwait(false);

        return ApiResponse<RobotTestResult>.Ok(new RobotTestResult
        {
            Success = result.Success,
            Reply = result.Reply,
            Message = result.Message
        }, result.Success ? "测试完成" : result.Message);
    }
}
