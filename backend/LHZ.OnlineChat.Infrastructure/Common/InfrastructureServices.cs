using System.Globalization;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Application.Messaging.Commands;
using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LHZ.OnlineChat.Infrastructure.Common;

/// <summary>系统时钟</summary>
internal sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// 宿主环境信息：把启动时的 IsDevelopment 暴露给应用层。
/// 唯一用途是决定「SMTP 发不出去时是否回传验证码」——
/// 该行为在生产环境等于把「忘记密码」变成任意账号接管入口。
/// </summary>
internal sealed class HostEnvironmentInfo : IHostEnvironmentInfo
{
    public HostEnvironmentInfo(bool isDevelopment) => IsDevelopment = isDevelopment;

    public bool IsDevelopment { get; }
}

/// <summary>
/// 领域事件派发器。
/// 领域事件是普通 POCO（领域层不认识 MediatR），这里用反射把它包进
/// DomainEventNotification&lt;T&gt; 后交给 MediatR 发布。
/// </summary>
internal sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IPublisher _publisher;
    private readonly DomainEventOutbox _outbox;
    private readonly ILogger<DomainEventDispatcher> _logger;

    public DomainEventDispatcher(
        IPublisher publisher, DomainEventOutbox outbox, ILogger<DomainEventDispatcher> logger)
    {
        _publisher = publisher;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task DispatchAsync(
        IEnumerable<IDomainEvent> domainEvents, CancellationToken ct = default)
    {
        // 事务进行中：只入队，等 TransactionBehavior 提交后再放行 ——
        // 否则推了 WS 通知而事务随后回滚，通知就发错了
        if (_outbox.IsDeferring)
        {
            _outbox.Enqueue(domainEvents);
            return;
        }

        foreach (var domainEvent in domainEvents)
        {
            var notificationType = typeof(DomainEventNotification<>).MakeGenericType(domainEvent.GetType());
            var notification = (INotification)Activator.CreateInstance(notificationType, domainEvent)!;

            _logger.LogDebug("派发领域事件 {Event}", domainEvent.GetType().Name);
            await _publisher.Publish(notification, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// 禁言提示语格式化：把 UTC 截止时间按中国标准时间渲染给用户看。
/// 时区数据库属于宿主环境，因此这是基础设施职责。
/// </summary>
internal sealed class MuteMessageFormatter : IMuteMessageFormatter
{
    private readonly TimeZoneInfo _displayTimeZone;
    private readonly ILogger<MuteMessageFormatter> _logger;

    public MuteMessageFormatter(ILogger<MuteMessageFormatter> logger)
    {
        _logger = logger;
        _displayTimeZone = ResolveChinaTimeZone();
    }

    public string Format(DateTime mutedUntilUtc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(
            UtcTime.Normalize(mutedUntilUtc), _displayTimeZone);

        return $"你已被禁言至 {local.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)}，期间无法在群里发言";
    }

    /// <summary>
    /// Windows 与 Linux 的时区 ID 不同（"China Standard Time" vs "Asia/Shanghai"），
    /// 容器里只有后者。两个都找不到时退回固定 +8 偏移，不能因此抛异常。
    /// </summary>
    private TimeZoneInfo ResolveChinaTimeZone()
    {
        foreach (var id in new[] { "China Standard Time", "Asia/Shanghai" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // 尝试下一个 ID
            }
        }

        _logger.LogWarning("未找到中国时区数据，回退为固定 UTC+8 偏移");
        return TimeZoneInfo.CreateCustomTimeZone("CST+8", TimeSpan.FromHours(8), "UTC+08:00", "UTC+08:00");
    }
}

/// <summary>
/// 管理操作审计日志写入。
/// 与改造前一致：写日志失败不影响主流程。
/// </summary>
internal sealed class AuditLogger : IAuditLogger
{
    private readonly IAdminAuditLogRepository _logs;
    private readonly IAdminRepository _admins;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(
        IAdminAuditLogRepository logs,
        IAdminRepository admins,
        ICurrentUser currentUser,
        IClock clock,
        ILogger<AuditLogger> logger)
    {
        _logs = logs;
        _admins = admins;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task RecordAsync(
        int adminId,
        string action,
        string targetType,
        string? targetId,
        string? detail,
        CancellationToken ct = default)
    {
        try
        {
            var admin = await _admins.FindByIdAsync(adminId, ct).ConfigureAwait(false);

            var log = AdminAuditLog.Record(
                adminId,
                admin?.Username ?? "?",
                action,
                targetType,
                targetId,
                detail,
                _currentUser.ClientIp,
                _clock.UtcNow);

            await _logs.AddAsync(log, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "审计日志写入失败：{Action} {TargetType} {TargetId}", action, targetType, targetId);
        }
    }
}
