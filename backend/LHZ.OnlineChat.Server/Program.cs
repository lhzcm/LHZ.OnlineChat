using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using LHZ.OnlineChat.Application;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Infrastructure;
using LHZ.OnlineChat.Infrastructure.Persistence;
using LHZ.OnlineChat.Server.Authentication;
using LHZ.OnlineChat.Server.Configuration;
using LHZ.OnlineChat.Server.Realtime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ==================== 配置 ====================
var appSettings = new AppSettings();
builder.Configuration.Bind(appSettings);
builder.Services.AddSingleton(appSettings);

// 生产环境配置自检：密钥仍是仓库默认值 / 关键项缺失就拒绝启动，
// 而不是带着公开的默认密钥静默跑起来（见 EnsureProductionReady 的说明）
if (!builder.Environment.IsDevelopment()) appSettings.EnsureProductionReady();

var uploadsRootPath = Path.Combine(builder.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(uploadsRootPath);

// ==================== 分层装配 ====================
// 依赖方向：Server → Infrastructure → Application → Domain
builder.Services.AddApplication();
builder.Services.AddInfrastructure(
    appSettings.ToInfrastructureOptions(uploadsRootPath, builder.Environment.IsDevelopment()));

// 表现层提供「当前调用者」的实现
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// ==================== JWT 认证 ====================
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = appSettings.Jwt.Issuer,
            ValidAudience = appSettings.Jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(appSettings.Jwt.Secret)),
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            // WebSocket 无法自定义请求头，令牌从查询字符串读取
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken)) context.Token = accessToken;
                return Task.CompletedTask;
            },

            // 会话有效性校验：被踢下线 / 改密 / 封禁后会话即删除，所有 API 立即 401。
            // 必须用异步 API —— 同步查询在高并发下会因 Redis 命令堆积超时，表现为空 500。
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                var sessionId = principal?.FindFirst("sid")?.Value;

                // 无 sid 的令牌只可能是本次改造之前签发的旧管理员令牌，
                // 那批令牌无法吊销，一律拒绝，让持有者重新登录换成带 sid 的
                if (string.IsNullOrEmpty(sessionId))
                {
                    context.Fail("会话已失效，请重新登录");
                    return;
                }

                var services = context.HttpContext.RequestServices;

                // 管理员会话与用户会话键位独立，按 role 分流校验
                var isAdmin = principal!.FindFirst("role")?.Value == "admin"
                              || principal.FindFirst(ClaimTypes.Role)?.Value == "admin";

                var valid = isAdmin
                    ? await services.GetRequiredService<IAdminSessionStore>().IsValidAsync(sessionId)
                    : await services.GetRequiredService<ISessionStore>().IsSessionValidAsync(sessionId);

                if (!valid) context.Fail("会话已失效，请重新登录");
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();

// ==================== 反向代理头 ====================
// nginx 反代之后 RemoteIpAddress 是 nginx 容器的地址：不限流则所有用户共用一个
// 限流分区（一个人刷爆就全员 429），审计日志与登录限流也会记错来源。
// 解析 X-Forwarded-For 才能拿到真实客户端 IP。
//
// 代价是「客户端可以伪造该头」，所以前提是后端端口不直接对外：
// docker-compose 里 backend 没有 ports 映射，只能经 nginx 反代访问。
// 若将来直接暴露后端端口，必须把下面的清空改成显式的信任代理列表。
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ==================== 接口限流 ====================
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // 匿名发码：按来源 IP 分区，拦住「换邮箱批量轰炸」。
    // 阈值取得比真人需求宽松（正常用户几分钟内最多点一两次），
    // 同时给公司/校园 NAT 出口留余量。
    options.AddPolicy(RateLimitPolicies.SendCode, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitPolicies.ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));

    // 机器人推送：按令牌分区（同一出口下的多个第三方服务互不影响），
    // 每分钟 120 条对通知类推送足够，也能挡住拿令牌刷爆聊天记录。
    options.AddPolicy(RateLimitPolicies.RobotReply, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Request.RouteValues["robotToken"]?.ToString()
                ?? RateLimitPolicies.ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // 凭邀请链接申请/加入群组：一分钟 30 次足够正常人（点错、重试、分享给朋友），
    // 又能挡住脚本拿着一条公开链接批量提交申请。按 IP 分区的原因见 RateLimitPolicies.GroupJoin。
    options.AddPolicy(RateLimitPolicies.GroupJoin, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitPolicies.ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // 注册 / 忘记密码：都要先过 SendCode，这里是第二道闸，阈值同量级。
    options.AddPolicy(RateLimitPolicies.AuthAnonymous, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitPolicies.ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));

    // 刷新令牌：真人在同一出口（公司 / 校园 NAT）下会成片触发，所以比注册宽得多。
    options.AddPolicy(RateLimitPolicies.TokenRefresh, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitPolicies.ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));

    // 上传：每次最多落盘 10MB，按用户分区。每分钟 20 次对真人（连发几张图）足够，
    // 也把「单账号一分钟写 200MB」的磁盘消耗上限压下来。
    options.AddPolicy(RateLimitPolicies.Upload, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitPolicies.UserKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // 文件上传：单件 50MB，10 次/分钟对真人足够，也把「单账号一分钟写 500MB」压下来。
    options.AddPolicy(RateLimitPolicies.FileUpload, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitPolicies.UserKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // 搜索：输入框防抖后每次击键停顿都可能触发一次，30 次 / 分钟留出余量。
    options.AddPolicy(RateLimitPolicies.Search, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitPolicies.UserKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // 被限流时也回 ApiResponse 形状，前端能按统一契约提示，而不是解析空响应体
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";

        await context.HttpContext.Response.WriteAsJsonAsync(
            ApiResponse.Fail("请求过于频繁，请稍后再试"), ct);
    };
});

// ==================== 健康检查 ====================
// 供 docker-compose 的 healthcheck 使用（容器内 curl /health）。
// 只反映进程存活，不查数据库 —— 依赖不可用时让探针失败会造成容器反复重启，
// 反而放大故障。依赖状态属于监控范畴，不属于存活探针。
builder.Services.AddHealthChecks();

// ==================== Swagger ====================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "OnlineChat API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        Description = "JWT 认证头，格式为 'Bearer {token}'",
        Name = "Authorization",
        In = Microsoft.OpenApi.ParameterLocation.Header,
        Type = Microsoft.OpenApi.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    options.AddSecurityRequirement(doc => new Microsoft.OpenApi.OpenApiSecurityRequirement
    {
        { new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer", doc, null!), new List<string>() }
    });
});

// ==================== CORS ====================
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = appSettings.Cors.AllowedOrigins;
    if (string.IsNullOrWhiteSpace(origins) || origins == "*")
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    }
    else
    {
        policy
            .WithOrigins(origins.Split(
                ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .AllowAnyMethod()
            .AllowAnyHeader();
    }
}));

var app = builder.Build();

app.Urls.Add("http://0.0.0.0:5000"); // 本机与容器内均可访问

// CORS 全开在开发环境是便利，在生产环境是多余的暴露面（同域部署根本不需要跨域）
if (!app.Environment.IsDevelopment() && appSettings.AllowsAnyOrigin)
{
    app.Logger.LogWarning(
        "Cors:AllowedOrigins 为 *（允许任意来源）。同域部署时建议改成站点域名；"
        + "当前依赖 JWT 放在 Authorization 头（而非 Cookie）来限制跨站利用");
}

// 未配置 SMTP 是受支持的运行模式（验证码只落服务器日志），但生产环境必须显式提醒：
// 这条路径下没有邮件发出，注册/忘记密码/换绑邮箱全靠管理员从日志取码 ——
// 不提示的话，用户看到的是「验证码已生成」，而收件箱里永远等不到邮件
if (!app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(appSettings.Smtp.Host))
{
    app.Logger.LogWarning(
        "未配置 SMTP（Smtp:Host 为空）：验证码只输出到服务器日志，不会发送邮件。"
        + "注册 / 忘记密码 / 换绑邮箱需要管理员从日志取码；面向公众部署请配置 Smtp:Host");
}

// 放行内网 Webhook 目标是有意关闭的默认项：打开后任意用户都能让服务端去 POST
// 内网地址（云元数据 169.254.169.254、内网管理端口等）。生产环境必须留下痕迹。
if (!app.Environment.IsDevelopment() && appSettings.Robot.AllowPrivateWebhookTargets)
{
    app.Logger.LogWarning(
        "Robot:AllowPrivateWebhookTargets 已开启：机器人回调允许指向内网/本机地址，"
        + "注册用户可通过 Webhook 让服务端访问内网（SSRF）。仅在自建 Webhook 确需内网地址时开启");
}

// ==================== 数据库准备（建表 / 迁移 / 索引 / 初始超管） ====================
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
}

// ==================== 中间件管道 ====================
// 异常留痕必须在最前面：放在鉴权之后就记不到鉴权/限流阶段抛出的异常。
// 业务规则驳回由 DomainExceptionBehavior 处理，不会到这里。
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(
            ex, "未处理异常 {Method} {Path}{Query}",
            context.Request.Method, context.Request.Path, RedactQuery(context.Request.Query));
        throw;
    }
});

// 必须早于限流与鉴权：它们都要读真实客户端 IP
app.UseForwardedHeaders();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRootPath),
    RequestPath = "/uploads",
    // 语音消息：默认映射没覆盖 m4a/webm 的音频 Content-Type，Safari 会拒绝播放，这里显式补齐
    ContentTypeProvider = new UploadContentTypeProvider(),
    OnPrepareResponse = ctx =>
    {
        // 上传目录是用户内容：禁止浏览器按内容嗅探类型（配合扩展名白名单才是完整防护），
        // 并声明长期缓存 —— 文件名带 GUID，内容不会变
        ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=604800, immutable";

        // 文件消息一律下载而不是内联打开：上传目录与站点同源，html/svg 之类内联执行
        // 等于一个存储型 XSS。下载名由前端的 <a download> 指定，这里不需要 filename。
        if (ctx.Context.Request.Path.StartsWithSegments("/uploads/files"))
        {
            ctx.Context.Response.Headers["Content-Disposition"] = "attachment";
        }
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseCors();
// 限流在认证之后：按用户分区的策略（上传 / 搜索）要读已验签的身份；
// 匿名接口按 IP 分区，不受这个顺序影响
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapChatWebSocket();
app.MapControllers();

app.Logger.LogInformation("OnlineChat API 启动中（四层架构：Domain / Application / Infrastructure / Server）");
app.Run();

/// <summary>
/// 记录请求上下文，但对可能携带凭据的查询参数脱敏。
///
/// WebSocket 握手把 JWT 放在 ?access_token= 里（浏览器无法给 WS 设置请求头），
/// 原样写进日志等于把可用令牌落到磁盘上；验证码、签名等参数同理。
/// </summary>
static string RedactQuery(IQueryCollection query)
{
    if (query.Count == 0) return string.Empty;

    var parts = query.Select(kvp => IsSensitive(kvp.Key)
        ? $"{kvp.Key}=***"
        : $"{kvp.Key}={kvp.Value}");

    return "?" + string.Join('&', parts);

    static bool IsSensitive(string key)
        => key.Contains("token", StringComparison.OrdinalIgnoreCase)
           || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
           || key.Contains("password", StringComparison.OrdinalIgnoreCase)
           || key.Contains("signature", StringComparison.OrdinalIgnoreCase)
           || key.Equals("ticket", StringComparison.OrdinalIgnoreCase)
           || key.Equals("code", StringComparison.OrdinalIgnoreCase);
}