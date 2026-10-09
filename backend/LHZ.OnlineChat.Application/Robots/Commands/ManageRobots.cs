using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Friends;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Robots;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Robots.Commands;

/// <summary>创建机器人：建机器人账号 + 配置 + 与创建者自动成为好友</summary>
public sealed class CreateRobotCommand : ICommand<ApiResponse<RobotInfo>>
{
    public int OwnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    /// <summary>可留空 = 纯推送模式</summary>
    public string WebhookUrl { get; set; } = string.Empty;

    public string? WebhookSecret { get; set; }

    public int? TimeoutMs { get; set; }
}

internal sealed class CreateRobotHandler : IRequestHandler<CreateRobotCommand, ApiResponse<RobotInfo>>
{
    private readonly IRobotRepository _robots;
    private readonly IUserRepository _users;
    private readonly IFriendshipRepository _friendships;
    private readonly IRobotTokenCipher _cipher;
    private readonly IWebhookTargetPolicy _webhookTargets;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public CreateRobotHandler(
        IRobotRepository robots,
        IUserRepository users,
        IFriendshipRepository friendships,
        IRobotTokenCipher cipher,
        IWebhookTargetPolicy webhookTargets,
        IDomainEventDispatcher events,
        IClock clock)
    {
        _robots = robots;
        _users = users;
        _friendships = friendships;
        _cipher = cipher;
        _webhookTargets = webhookTargets;
        _events = events;
        _clock = clock;
    }

    public async Task<ApiResponse<RobotInfo>> Handle(CreateRobotCommand command, CancellationToken ct)
    {
        // SSRF 防护：这个地址由用户填写、由服务端主动 POST，默认不允许内网/本机目标
        WebhookUrl.EnsureTargetAllowed(command.WebhookUrl, _webhookTargets.AllowPrivateTargets);
        var webhook = WebhookUrl.Parse(command.WebhookUrl);

        // 名称必须先按「机器人名称」规则校验：
        // 下一步建的是 User 账号，它校验的是昵称规则，提示语会变成「昵称不能为空」，
        // 对正在创建机器人的用户是错的语境。
        var name = Robot.NormalizeName(command.Name);
        var now = _clock.UtcNow;

        // 1) 机器人账号（IsBot=true，无邮箱无口令 → 不可登录）
        var botUser = User.CreateBot(name, command.Avatar, now);
        await _users.AddAsync(botUser, ct).ConfigureAwait(false);

        // 2) Webhook 配置
        var robot = Robot.Create(
            command.OwnerId, botUser.Id, name, command.Avatar,
            webhook, command.WebhookSecret, command.TimeoutMs, now);
        await _robots.AddAsync(robot, ct).ConfigureAwait(false);

        // 3) 对外令牌由主键加密而来，因此必须落库拿到 Id 之后再生成
        robot.AssignToken(_cipher.Encode(robot.Id));
        await _robots.UpdateAsync(robot, ct).ConfigureAwait(false);

        // 4) 与创建者建立好友关系（私聊即可触发）
        await _friendships
            .AddAsync(Friendship.EstablishDirectly(command.OwnerId, botUser.Id, now), ct)
            .ConfigureAwait(false);

        botUser.ConfirmBotCreation(command.OwnerId, now);
        await _events.DispatchEventsOfAsync(botUser, ct).ConfigureAwait(false);

        return ApiResponse<RobotInfo>.Ok(robot.ToInfo(), "机器人创建成功");
    }
}

/// <summary>更新机器人配置（仅创建者）</summary>
public sealed class UpdateRobotCommand : ICommand<ApiResponse<RobotInfo>>
{
    public int OwnerId { get; set; }

    public long RobotId { get; set; }

    public string? Name { get; set; }

    public string? Avatar { get; set; }

    public string? WebhookUrl { get; set; }

    public string? WebhookSecret { get; set; }

    public int? TimeoutMs { get; set; }

    public bool? Enabled { get; set; }
}

internal sealed class UpdateRobotHandler : IRequestHandler<UpdateRobotCommand, ApiResponse<RobotInfo>>
{
    private readonly IRobotRepository _robots;
    private readonly IUserRepository _users;
    private readonly IRobotTokenCipher _cipher;
    private readonly IWebhookTargetPolicy _webhookTargets;
    private readonly IClock _clock;

    public UpdateRobotHandler(
        IRobotRepository robots, IUserRepository users, IRobotTokenCipher cipher,
        IWebhookTargetPolicy webhookTargets, IClock clock)
    {
        _robots = robots;
        _users = users;
        _cipher = cipher;
        _webhookTargets = webhookTargets;
        _clock = clock;
    }

    public async Task<ApiResponse<RobotInfo>> Handle(UpdateRobotCommand command, CancellationToken ct)
    {
        var robot = await _robots.FindByIdAsync(command.RobotId, ct).ConfigureAwait(false)
                    ?? throw new EntityNotFoundException("机器人不存在");
        robot.EnsureOwnedBy(command.OwnerId);

        // 改地址同样要过 SSRF 校验 —— 否则创建时填公网、之后改成内网就绕过了
        if (command.WebhookUrl is not null)
        {
            WebhookUrl.EnsureTargetAllowed(command.WebhookUrl, _webhookTargets.AllowPrivateTargets);
        }

        var nameChanged = robot.UpdateConfiguration(
            command.Name, command.Avatar, command.WebhookUrl,
            command.WebhookSecret, command.TimeoutMs, command.Enabled);

        // 兼容历史数据：早期记录没有持久化令牌
        if (string.IsNullOrEmpty(robot.Token)) robot.AssignToken(_cipher.Encode(robot.Id));

        await _robots.UpdateAsync(robot, ct).ConfigureAwait(false);

        // 机器人显示名与账号昵称保持同步
        if (nameChanged)
        {
            var botUser = await _users.FindByIdAsync(robot.UserId, ct).ConfigureAwait(false);
            if (botUser is not null)
            {
                botUser.Rename(robot.Name, _clock.UtcNow);
                await _users.UpdateAsync(botUser, ct).ConfigureAwait(false);
            }
        }

        return ApiResponse<RobotInfo>.Ok(robot.ToInfo(), "已保存");
    }
}

/// <summary>删除机器人（清理账号/好友关系/群成员记录）</summary>
public sealed class DeleteRobotCommand : ICommand<ApiResponse>
{
    public long RobotId { get; set; }

    /// <summary>创建者校验；管理后台传 null 表示绕过</summary>
    public int? OwnerId { get; set; }
}

internal sealed class DeleteRobotHandler : IRequestHandler<DeleteRobotCommand, ApiResponse>
{
    private readonly IRobotRepository _robots;
    private readonly IUserRepository _users;
    private readonly IFriendshipRepository _friendships;
    private readonly IGroupMemberRepository _members;

    public DeleteRobotHandler(
        IRobotRepository robots,
        IUserRepository users,
        IFriendshipRepository friendships,
        IGroupMemberRepository members)
    {
        _robots = robots;
        _users = users;
        _friendships = friendships;
        _members = members;
    }

    public async Task<ApiResponse> Handle(DeleteRobotCommand command, CancellationToken ct)
    {
        var robot = await _robots.FindByIdAsync(command.RobotId, ct).ConfigureAwait(false)
                    ?? throw new EntityNotFoundException("机器人不存在");

        if (command.OwnerId.HasValue) robot.EnsureOwnedBy(command.OwnerId.Value);

        await _robots.DeleteAsync(robot.Id, ct).ConfigureAwait(false);
        await _friendships.DeleteAllOfAsync(robot.UserId, ct).ConfigureAwait(false);
        await _members.DeleteAllOfUserAsync(robot.UserId, ct).ConfigureAwait(false);
        await _users.DeleteAsync(robot.UserId, ct).ConfigureAwait(false);

        return ApiResponse.Ok("机器人已删除");
    }
}

/// <summary>我的机器人列表</summary>
public sealed class GetMyRobotsQuery : IQuery<ApiResponse<List<RobotInfo>>>
{
    public int OwnerId { get; set; }
}

internal sealed class GetMyRobotsHandler : IRequestHandler<GetMyRobotsQuery, ApiResponse<List<RobotInfo>>>
{
    private readonly IRobotRepository _robots;
    private readonly IRobotTokenCipher _cipher;

    public GetMyRobotsHandler(IRobotRepository robots, IRobotTokenCipher cipher)
    {
        _robots = robots;
        _cipher = cipher;
    }

    public async Task<ApiResponse<List<RobotInfo>>> Handle(GetMyRobotsQuery query, CancellationToken ct)
    {
        var robots = await _robots.ListByOwnerAsync(query.OwnerId, ct).ConfigureAwait(false);
        var items = new List<RobotInfo>(robots.Count);

        foreach (var robot in robots)
        {
            // 兼容历史数据：缺令牌的补齐并持久化
            if (string.IsNullOrEmpty(robot.Token))
            {
                robot.AssignToken(_cipher.Encode(robot.Id));
                await _robots.UpdateAsync(robot, ct).ConfigureAwait(false);
            }
            items.Add(robot.ToInfo());
        }

        return ApiResponse<List<RobotInfo>>.Ok(items);
    }
}
