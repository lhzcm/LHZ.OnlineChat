using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Application.Messaging.Commands;
using LHZ.OnlineChat.Application.Robots.EventHandlers;
using LHZ.OnlineChat.Domain.Admins;
using LHZ.OnlineChat.Domain.Blacklists;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Friends;
using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Robots;
using LHZ.OnlineChat.Domain.Users;
using LHZ.OnlineChat.Infrastructure.Bots;
using LHZ.OnlineChat.Infrastructure.Caching;
using LHZ.OnlineChat.Infrastructure.Common;
using LHZ.OnlineChat.Infrastructure.Messaging;
using LHZ.OnlineChat.Infrastructure.Persistence;
using LHZ.OnlineChat.Infrastructure.Persistence.Repositories;
using LHZ.OnlineChat.Infrastructure.Realtime;
using LHZ.OnlineChat.Infrastructure.Security;
using LHZ.OnlineChat.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LHZ.OnlineChat.Infrastructure;

/// <summary>基础设施层装配：把应用层声明的每个端口绑定到具体实现</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Webhook 调用的整体超时上限（单次调用另有 Robot.TimeoutMs 控制）</summary>
    private static readonly TimeSpan BotHttpClientTimeout = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, InfrastructureOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        AddPersistence(services, options);
        AddCaching(services, options);
        AddRealtime(services);
        AddSecurity(services, options);
        AddIntegrations(services, options);

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddSingleton<IMuteMessageFormatter, MuteMessageFormatter>();
        services.AddScoped<IAuditLogger, AuditLogger>();

        // 环境信息是单例：它由启动时的配置决定，与请求无关
        services.AddSingleton<IHostEnvironmentInfo>(
            new HostEnvironmentInfo(options.IsDevelopment));

        return services;
    }

    private static void AddPersistence(IServiceCollection services, InfrastructureOptions options)
    {
        // 目标库不存在时先建库（必须在构建 IFreeSql 之前）
        DatabaseInitializer.EnsureDatabaseExists(options.ConnectionString);

        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.PostgreSQL, options.ConnectionString)
            .UseAutoSyncStructure(true)
            // SQL 日志仅开发环境输出：生产高并发下 Console 写入会阻塞请求线程（线程池饥饿）
            .UseMonitorCommand(cmd =>
            {
                if (options.IsDevelopment) Console.WriteLine($"[SQL] {cmd.CommandText}");
            })
            .Build();

        // 实体映射（FluentApi）必须在任何查询之前完成
        EntityConfiguration.Apply(fsql);

        services.AddSingleton(fsql);
        services.AddSingleton(options.AdminBootstrap);
        services.AddScoped<DatabaseInitializer>();

        // 每请求一个会话：持有环境事务，仓储全部经它发起查询
        services.AddScoped<DbSession>();
        services.AddScoped<IUnitOfWork, FreeSqlUnitOfWork>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IFriendshipRepository, FriendshipRepository>();
        services.AddScoped<IFriendSettingRepository, FriendSettingRepository>();
        services.AddScoped<IBlacklistRepository, BlacklistRepository>();
        services.AddScoped<IGroupRepository, GroupRepository>();
        services.AddScoped<IGroupMemberRepository, GroupMemberRepository>();
        services.AddScoped<IPrivateMessageRepository, PrivateMessageRepository>();
        services.AddScoped<IGroupMessageRepository, GroupMessageRepository>();
        services.AddScoped<ISessionSettingRepository, SessionSettingRepository>();
        services.AddScoped<IRobotRepository, RobotRepository>();
        services.AddScoped<IAdminRepository, AdminRepository>();
        services.AddScoped<IAdminAuditLogRepository, AdminAuditLogRepository>();
    }

    private static void AddCaching(IServiceCollection services, InfrastructureOptions options)
    {
        services.AddSingleton(new RedisConnection(options.RedisConnectionString));
        services.AddSingleton<IVerificationCodeStore, VerificationCodeStore>();
        services.AddSingleton<IPresenceStore, PresenceStore>();
        services.AddSingleton<IRecentMessageCache, RecentMessageCache>();
        services.AddSingleton<ISessionStore, SessionStore>();
        services.AddSingleton<ISessionTerminator, SessionTerminator>();
        services.AddSingleton<IAdminSessionStore, AdminSessionStore>();
        services.AddSingleton<ILoginThrottle, LoginThrottle>();
    }

    private static void AddRealtime(IServiceCollection services)
    {
        // 连接表是进程级状态，必须单例
        services.AddSingleton<WsConnectionManager>();
        services.AddSingleton<IConnectionRegistry>(sp => sp.GetRequiredService<WsConnectionManager>());
        services.AddSingleton<IRealtimeNotifier, RealtimeNotifier>();
        services.AddSingleton<WsInboundDispatcher>();
        // 表现层完成握手后由它接管连接生命周期
        services.AddSingleton<IChatConnectionHandler, ChatConnectionHandler>();
    }

    private static void AddSecurity(IServiceCollection services, InfrastructureOptions options)
    {
        services.AddSingleton(options.Jwt);
        services.AddSingleton(options.Robot);
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
        services.AddSingleton<IRobotTokenCipher, RobotTokenCipher>();
        services.AddSingleton<IWebhookSigner, HmacWebhookSigner>();
    }

    private static void AddIntegrations(IServiceCollection services, InfrastructureOptions options)
    {
        services.AddSingleton(options.Smtp);
        services.AddSingleton<IEmailSender, MailKitEmailSender>();

        services.AddSingleton(options.FileStorage);
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        services
            .AddHttpClient(WebhookDispatcher.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                // 允许 http:// 内网地址（本地自建 Webhook 调试）；生产建议 HTTPS
                AllowAutoRedirect = false
            })
            .ConfigureHttpClient(client => client.Timeout = BotHttpClientTimeout);

        services.AddScoped<IWebhookDispatcher, WebhookDispatcher>();
        // Webhook 出站目标策略（是否允许内网地址）：与 RobotOptions 同源，默认关闭
        services.AddSingleton<IWebhookTargetPolicy, RobotWebhookTargetPolicy>();
        services.AddSingleton<IRobotConversationService, RobotConversationService>();
    }
}

/// <summary>基础设施配置汇总（由宿主从 appsettings / 环境变量绑定后传入）</summary>
public sealed class InfrastructureOptions
{
    public required string ConnectionString { get; init; }

    public required string RedisConnectionString { get; init; }

    public required JwtOptions Jwt { get; init; }

    public required SmtpOptions Smtp { get; init; }

    public required RobotOptions Robot { get; init; }

    public required FileStorageOptions FileStorage { get; init; }

    public required AdminBootstrapOptions AdminBootstrap { get; init; }

    /// <summary>开发环境才输出 SQL 日志</summary>
    public bool IsDevelopment { get; init; }
}
