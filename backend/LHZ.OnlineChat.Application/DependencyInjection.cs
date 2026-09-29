using System.Reflection;
using LHZ.OnlineChat.Application.Admins.Queries;
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

            // 管道顺序：日志在外（能记录到异常翻译前的原始情况），异常翻译在内
            config.AddOpenBehavior(typeof(LoggingBehavior<,>));
            config.AddOpenBehavior(typeof(DomainExceptionBehavior<,>));
        });

        // 跨用例复用的应用层协作者（不是领域概念，也不是基础设施）
        services.AddScoped<FriendSettingWriter>();
        services.AddScoped<AdminUserDtoBuilder>();

        return services;
    }
}
