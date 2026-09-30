using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LHZ.OnlineChat.Application.Common.Behaviors;

/// <summary>
/// 把数据库唯一约束冲突翻译成与应用层查重分支一致的提示语。
///
/// 每个写入位置都已经「先查重再写入」，但并发下那道检查有窗口期，真正兜底的是唯一索引。
/// 索引报错时若不翻译，用户会看到 500 —— 而这其实是一个正常的业务驳回，
/// 只是抢跑的那个请求慢了一步。翻译成 DomainException 后由 DomainExceptionBehavior
/// 落成 ApiResponse.Fail，前端表现与串行执行时完全一样。
///
/// 管道位置：在 DomainExceptionBehavior 之内、TransactionBehavior 之外
/// （冲突是从事务提交/回滚里抛出来的）。
/// </summary>
internal sealed class UniqueConstraintBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>索引名 → 用户可读提示。索引名见 DatabaseInitializer.EnsureUniqueConstraints</summary>
    private static readonly Dictionary<string, string> MessageByConstraint = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ux_user_email"] = "该邮箱已注册",
        ["ux_groupmember_group_user"] = "你已经是该群组成员",
        ["ux_friend_user_friend"] = "已经是好友或已发送过申请",
        ["ux_blacklist_user_blocked"] = "该用户已在黑名单中",
        ["ux_admin_username"] = "该管理员账号已存在",
        ["ux_robotprofile_user"] = "该账号已绑定机器人配置",
    };

    /// <summary>未登记的约束（如好友设置/会话设置这类幂等写入）统一提示重试</summary>
    private const string FallbackMessage = "操作冲突，请重试";

    private readonly ILogger<UniqueConstraintBehavior<TRequest, TResponse>> _logger;

    public UniqueConstraintBehavior(ILogger<UniqueConstraintBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        catch (UniqueConstraintViolationException ex)
        {
            // 记 Information 而非 Error：这是并发竞态被兜底拦下，属于预期内的稀有路径，
            // 但比普通业务驳回值得留痕（能反映真实并发冲突频率）
            _logger.LogInformation(
                "{Request} 触发唯一约束 {Constraint}，按业务驳回处理",
                typeof(TRequest).Name, ex.ConstraintHint);

            throw new DomainException(Describe(ex.ConstraintHint), ex);
        }
    }

    private static string Describe(string constraintHint)
    {
        if (MessageByConstraint.TryGetValue(constraintHint, out var exact)) return exact;

        // Postgres 在部分错误里给的是完整报文而非索引名，退化为包含匹配
        foreach (var (constraint, message) in MessageByConstraint)
        {
            if (constraintHint.Contains(constraint, StringComparison.OrdinalIgnoreCase)) return message;
        }

        return FallbackMessage;
    }
}
