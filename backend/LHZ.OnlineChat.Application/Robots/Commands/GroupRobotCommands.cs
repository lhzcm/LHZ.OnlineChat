using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Robots;
using MediatR;

namespace LHZ.OnlineChat.Application.Robots.Commands;

/// <summary>添加机器人到群（群主/管理员，只能添加自己创建的机器人）</summary>
public sealed class AddGroupRobotCommand : ICommand<ApiResponse>
{
    public long GroupId { get; set; }

    public int OperatorId { get; set; }

    /// <summary>机器人账号 ID（User.Id）</summary>
    public int RobotUserId { get; set; }
}

internal sealed class AddGroupRobotHandler : IRequestHandler<AddGroupRobotCommand, ApiResponse>
{
    private readonly IGroupMemberRepository _members;
    private readonly IGroupMessageRepository _messages;
    private readonly IRobotRepository _robots;
    private readonly IClock _clock;

    public AddGroupRobotHandler(
        IGroupMemberRepository members,
        IGroupMessageRepository messages,
        IRobotRepository robots,
        IClock clock)
    {
        _members = members;
        _messages = messages;
        _robots = robots;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(AddGroupRobotCommand command, CancellationToken ct)
    {
        var op = await _members.GetRequiredAsync(command.GroupId, command.OperatorId, ct: ct).ConfigureAwait(false);
        op.EnsureCanManageGroup("添加机器人");

        var robot = await _robots.FindByBotUserIdAsync(command.RobotUserId, ct).ConfigureAwait(false);
        DomainException.Ensure(robot is not null && robot.OwnerId == command.OperatorId,
            "只能添加自己创建的机器人");

        var already = await _members.ExistsAsync(command.GroupId, command.RobotUserId, ct).ConfigureAwait(false);
        DomainException.Ensure(!already, "该机器人已在群中");

        var latestId = await _messages.MaxIdOfGroupAsync(command.GroupId, ct).ConfigureAwait(false);
        await _members
            .AddAsync(GroupMember.Join(command.GroupId, command.RobotUserId, latestId, _clock.UtcNow), ct)
            .ConfigureAwait(false);

        return ApiResponse.Ok($"已添加机器人「{robot!.Name}」");
    }
}

/// <summary>从群移除机器人（群主/管理员）</summary>
public sealed class RemoveGroupRobotCommand : ICommand<ApiResponse>
{
    public long GroupId { get; set; }

    public int OperatorId { get; set; }

    public int RobotUserId { get; set; }
}

internal sealed class RemoveGroupRobotHandler : IRequestHandler<RemoveGroupRobotCommand, ApiResponse>
{
    private readonly IGroupMemberRepository _members;

    public RemoveGroupRobotHandler(IGroupMemberRepository members) => _members = members;

    public async Task<ApiResponse> Handle(RemoveGroupRobotCommand command, CancellationToken ct)
    {
        var op = await _members.GetRequiredAsync(command.GroupId, command.OperatorId, ct: ct).ConfigureAwait(false);
        op.EnsureCanManageGroup("移除机器人");

        var removed = await _members
            .RemoveAsync(command.GroupId, command.RobotUserId, ct)
            .ConfigureAwait(false);

        DomainException.Ensure(removed > 0, "该机器人不在群中");
        return ApiResponse.Ok("已移除机器人");
    }
}

/// <summary>群内机器人列表</summary>
public sealed class GetGroupRobotsQuery : IQuery<ApiResponse<List<RobotInfo>>>
{
    public long GroupId { get; set; }
}

internal sealed class GetGroupRobotsHandler : IRequestHandler<GetGroupRobotsQuery, ApiResponse<List<RobotInfo>>>
{
    private readonly IGroupMemberRepository _members;
    private readonly IRobotRepository _robots;

    public GetGroupRobotsHandler(IGroupMemberRepository members, IRobotRepository robots)
    {
        _members = members;
        _robots = robots;
    }

    public async Task<ApiResponse<List<RobotInfo>>> Handle(GetGroupRobotsQuery query, CancellationToken ct)
    {
        var memberIds = await _members.ListMemberIdsAsync(query.GroupId, ct).ConfigureAwait(false);
        if (memberIds.Count == 0)
            return ApiResponse<List<RobotInfo>>.Ok(new List<RobotInfo>());

        var robots = await _robots.ListByBotUserIdsAsync(memberIds, ct).ConfigureAwait(false);
        return ApiResponse<List<RobotInfo>>.Ok(robots.Select(r => r.ToInfo()).ToList());
    }
}
