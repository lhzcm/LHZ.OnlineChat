using System.Reflection;
using LHZ.OnlineChat.Application.Admins.Queries;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Application.Common.Behaviors;
using LHZ.OnlineChat.Application.Friends.Commands;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace LHZ.OnlineChat.Application;

/// <summary>应用层装配</summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(config =>
        {
            // 用例处理器 + 领域事件订阅方全部在本程序集
            config.RegisterServicesFromAssembly(assembly);

            // 管道顺序（由外到内）：
            //   日志      —— 能记录到异常翻译前的原始情况
            //   异常翻译  —— DomainException → ApiResponse.Fail
            //   唯一约束  —— 唯一索引冲突 → DomainException，交给上一层翻译
            //   事务      —— 必须在异常翻译之内，否则业务驳回会连带提交半截写入
            config.AddOpenBehavior(typeof(LoggingBehavior<,>));
            config.AddOpenBehavior(typeof(DomainExceptionBehavior<,>));
            config.AddOpenBehavior(typeof(UniqueConstraintBehavior<,>));
            config.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        // 领域事件的事务内缓冲（每请求一个，见 DomainEventOutbox 说明）
        services.AddScoped<DomainEventOutbox>();

        // 跨用例复用的应用层协作者（不是领域概念，也不是基础设施）
        services.AddScoped<FriendSettingWriter>();
        services.AddScoped<AdminUserDtoBuilder>();

        return services;
    }
}
