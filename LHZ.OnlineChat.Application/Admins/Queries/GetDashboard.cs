using System.Globalization;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Robots;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Admins.Queries;

/// <summary>仪表盘概览（统计卡片 + 趋势 + TOP 排行）</summary>
public sealed class GetDashboardQuery : IQuery<ApiResponse<DashboardOverviewDto>>
{
    /// <summary>趋势天数</summary>
    public const int TrendDays = 7;

    /// <summary>小时分布的小时数</summary>
    public const int TrendHours = 24;

    /// <summary>排行榜取前 N</summary>
    public const int TopSize = 10;
}

internal sealed class GetDashboardHandler : IRequestHandler<GetDashboardQuery, ApiResponse<DashboardOverviewDto>>
{
    /// <summary>TOP 排行的候选池：两表各取 2 倍再合并，避免边界漏掉</summary>
    private const int TopCandidatePool = GetDashboardQuery.TopSize * 2;

    private readonly IUserRepository _users;
    private readonly IGroupRepository _groups;
    private readonly IRobotRepository _robots;
    private readonly IPrivateMessageRepository _privateMessages;
    private readonly IGroupMessageRepository _groupMessages;
    private readonly IConnectionRegistry _connections;
    private readonly IClock _clock;

    public GetDashboardHandler(
        IUserRepository users,
        IGroupRepository groups,
        IRobotRepository robots,
        IPrivateMessageRepository privateMessages,
        IGroupMessageRepository groupMessages,
        IConnectionRegistry connections,
        IClock clock)
    {
        _users = users;
        _groups = groups;
        _robots = robots;
        _privateMessages = privateMessages;
        _groupMessages = groupMessages;
        _connections = connections;
        _clock = clock;
    }

    public async Task<ApiResponse<DashboardOverviewDto>> Handle(
        GetDashboardQuery query, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var today = now.Date;

        var dto = new DashboardOverviewDto
        {
            OnlineUsers = _connections.OnlineUserCount,
            WsConnections = _connections.ConnectionCount,
            TotalUsers = await _users.CountAsync(ct).ConfigureAwait(false),
            BannedUsers = await _users.CountBannedAsync(ct).ConfigureAwait(false),
            TotalGroups = await _groups.CountAsync(ct).ConfigureAwait(false),
            TotalRobots = await _robots.CountAsync(ct).ConfigureAwait(false),
            PrivateMessageTotal = await _privateMessages.CountAsync(ct).ConfigureAwait(false),
            GroupMessageTotal = await _groupMessages.CountAsync(ct).ConfigureAwait(false),
            TodayPrivateMessages = await _privateMessages.CountSentSinceAsync(today, ct).ConfigureAwait(false),
            TodayGroupMessages = await _groupMessages.CountSentSinceAsync(today, ct).ConfigureAwait(false),
            TodayRegistrations = await _users.CountRegisteredSinceAsync(today, ct).ConfigureAwait(false),
            TodayNewGroups = await _groups.CountCreatedSinceAsync(today, ct).ConfigureAwait(false)
        };

        dto.TotalMessages = dto.PrivateMessageTotal + dto.GroupMessageTotal;
        dto.TodayMessages = dto.TodayPrivateMessages + dto.TodayGroupMessages;

        dto.TodayActiveUsers = await CountTodayActiveUsersAsync(today, ct).ConfigureAwait(false);
        await FillTrendsAsync(dto, today, ct).ConfigureAwait(false);
        await FillHourTrendAsync(dto, now, ct).ConfigureAwait(false);
        await FillTopUsersAsync(dto, ct).ConfigureAwait(false);
        await FillTopGroupsAsync(dto, ct).ConfigureAwait(false);

        return ApiResponse<DashboardOverviewDto>.Ok(dto);
    }

    /// <summary>今日活跃 = 今日发过消息的去重用户数（私聊 ∪ 群聊）</summary>
    private async Task<int> CountTodayActiveUsersAsync(DateTime today, CancellationToken ct)
    {
        var fromPrivate = await _privateMessages
            .ListDistinctSendersSinceAsync(today, ct)
            .ConfigureAwait(false);
        var fromGroup = await _groupMessages
            .ListDistinctSendersSinceAsync(today, ct)
            .ConfigureAwait(false);

        return fromPrivate.Concat(fromGroup).Distinct().Count();
    }

    /// <summary>近 7 日注册 / 消息趋势</summary>
    private async Task FillTrendsAsync(DashboardOverviewDto dto, DateTime today, CancellationToken ct)
    {
        for (var offset = GetDashboardQuery.TrendDays - 1; offset >= 0; offset--)
        {
            var dayStart = today.AddDays(-offset);
            var dayEnd = dayStart.AddDays(1);
            var label = dayStart.ToString("MM-dd", CultureInfo.InvariantCulture);

            dto.RegisterTrend.Add(new TrendPointDto
            {
                Date = label,
                Count = await _users
                    .CountRegisteredBetweenAsync(dayStart, dayEnd, ct)
                    .ConfigureAwait(false)
            });

            var privateCount = await _privateMessages
                .CountSentBetweenAsync(dayStart, dayEnd, ct)
                .ConfigureAwait(false);
            var groupCount = await _groupMessages
                .CountSentBetweenAsync(dayStart, dayEnd, ct)
                .ConfigureAwait(false);

            dto.MessageTrend.Add(new TrendPointDto
            {
                Date = label,
                Count = privateCount + groupCount
            });
        }
    }

    /// <summary>
    /// 近 24 小时按小时分布。
    /// 原实现这里有 bug：先按 dayAgo 算出小时刻度，紧接着又用 DateTime.UtcNow 覆盖，
    /// 且两行都用 AddHours(-i)，导致 i=0 时落在「当前小时」、i=23 时落在 23 小时前 ——
    /// 顺序其实是倒的（图表从右向左）。这里按时间正序生成刻度。
    /// </summary>
    private async Task FillHourTrendAsync(DashboardOverviewDto dto, DateTime now, CancellationToken ct)
    {
        var byHourPrivate = await _privateMessages
            .CountByHourSinceAsync(GetDashboardQuery.TrendHours, ct)
            .ConfigureAwait(false);
        var byHourGroup = await _groupMessages
            .CountByHourSinceAsync(GetDashboardQuery.TrendHours, ct)
            .ConfigureAwait(false);

        var currentHour = DateTime.SpecifyKind(
            new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0), DateTimeKind.Utc);

        for (var offset = GetDashboardQuery.TrendHours - 1; offset >= 0; offset--)
        {
            var hour = currentHour.AddHours(-offset);
            dto.MessageHourTrend.Add(new HourPointDto
            {
                Hour = hour.ToString("HH:00", CultureInfo.InvariantCulture),
                Count = byHourPrivate.GetValueOrDefault(hour) + byHourGroup.GetValueOrDefault(hour)
            });
        }
    }

    /// <summary>最活跃用户 TOP10（私聊 + 群聊发送量）</summary>
    private async Task FillTopUsersAsync(DashboardOverviewDto dto, CancellationToken ct)
    {
        var fromPrivate = await _privateMessages
            .TopSendersAsync(TopCandidatePool, ct)
            .ConfigureAwait(false);
        var fromGroup = await _groupMessages
            .TopSendersAsync(TopCandidatePool, ct)
            .ConfigureAwait(false);

        var totals = new Dictionary<int, long>();
        foreach (var (userId, count) in fromPrivate)
            totals[userId] = totals.GetValueOrDefault(userId) + count;
        foreach (var (userId, count) in fromGroup)
            totals[userId] = totals.GetValueOrDefault(userId) + count;

        var topIds = totals
            .OrderByDescending(pair => pair.Value)
            .Take(GetDashboardQuery.TopSize)
            .Select(pair => pair.Key)
            .ToList();

        if (topIds.Count == 0) return;

        var users = await _users.GetManyAsync(topIds, ct).ConfigureAwait(false);
        dto.TopUsers = topIds.Select(id => new TopUserDto
        {
            UserId = id,
            Nickname = users.GetValueOrDefault(id)?.Nickname ?? $"用户{id}",
            Avatar = users.GetValueOrDefault(id)?.Avatar,
            Count = totals.GetValueOrDefault(id)
        }).ToList();
    }

    /// <summary>最活跃群 TOP10</summary>
    private async Task FillTopGroupsAsync(DashboardOverviewDto dto, CancellationToken ct)
    {
        var top = await _groupMessages
            .TopGroupsAsync(GetDashboardQuery.TopSize, ct)
            .ConfigureAwait(false);
        if (top.Count == 0) return;

        var groupIds = top.Keys.ToList();
        var groups = await _groups.GetManyAsync(groupIds, ct).ConfigureAwait(false);

        dto.TopGroups = top
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new TopGroupDto
            {
                GroupId = pair.Key,
                Name = groups.GetValueOrDefault(pair.Key)?.Name ?? $"群{pair.Key}",
                Count = pair.Value
            })
            .ToList();
    }
}
