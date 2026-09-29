using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Robots.Commands;
using LHZ.OnlineChat.Application.Robots.EventHandlers;
using LHZ.OnlineChat.Domain.Robots;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LHZ.OnlineChat.Infrastructure.Bots;

/// <summary>
/// 机器人会话服务：后台投递 Webhook，拿到同步回复后以机器人身份发一条真实消息。
///
/// 为什么必须「后台 + 独立作用域」：Webhook 默认 10 秒超时，
/// 若在消息处理链路里同步等待，发消息的用户会被卡住；
/// 而触发它的请求作用域早已结束，所以要自己开一个新的 DI 作用域。
/// </summary>
internal sealed class RobotConversationService : IRobotConversationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RobotConversationService> _logger;

    public RobotConversationService(
        IServiceScopeFactory scopeFactory, ILogger<RobotConversationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void DispatchInBackground(long robotId, WebhookEvent payload, RobotReplyTarget replyTarget)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await RunAsync(robotId, payload, replyTarget).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 后台任务异常必须自己吞掉，否则会变成未观察的 Task 异常
                _logger.LogError(ex, "机器人 {RobotId} 的 Webhook 调度失败", robotId);
            }
        });
    }

    private async Task RunAsync(long robotId, WebhookEvent payload, RobotReplyTarget replyTarget)
    {
        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;

        var robots = provider.GetRequiredService<IRobotRepository>();
        var dispatcher = provider.GetRequiredService<IWebhookDispatcher>();

        var robot = await robots.FindByIdAsync(robotId).ConfigureAwait(false);
        if (robot is null) return;

        var result = await dispatcher.DispatchAsync(robot, payload).ConfigureAwait(false);

        if (!result.Success)
        {
            // 回调失败埋点（管理后台统计）
            robot.RecordCallbackFailure();
            await robots.UpdateAsync(robot).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrEmpty(result.Reply)) return;

        // 有同步回复：以机器人身份发出（复用统一的消息通道）
        var sender = provider.GetRequiredService<ISender>();
        await sender.Send(new SendRobotMessageCommand
        {
            RobotId = robot.Id,
            SessionType = replyTarget.SessionType,
            SessionId = replyTarget.SessionId,
            Content = result.Reply,
            QuotedMessageId = replyTarget.QuotedMessageId,
            QuotedContent = replyTarget.QuotedContent,
            QuotedSenderName = replyTarget.QuotedSenderName
        }).ConfigureAwait(false);
    }
}
