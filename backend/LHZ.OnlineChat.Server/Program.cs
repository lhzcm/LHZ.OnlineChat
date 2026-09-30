using System.Security.Claims;
using System.Text;
using LHZ.OnlineChat.Application;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Infrastructure;
using LHZ.OnlineChat.Infrastructure.Persistence;
using LHZ.OnlineChat.Server.Authentication;
using LHZ.OnlineChat.Server.Configuration;
using LHZ.OnlineChat.Server.Realtime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ==================== 配置 ====================
var appSettings = new AppSettings();
builder.Configuration.Bind(appSettings);
builder.Services.AddSingleton(appSettings);

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

// ==================== 数据库准备（建表 / 迁移 / 索引 / 初始超管） ====================
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
}

// ==================== 中间件管道 ====================
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRootPath),
    RequestPath = "/uploads"
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// 未处理异常留痕（业务规则驳回由 DomainExceptionBehavior 处理，不会到这里）
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
            context.Request.Method, context.Request.Path, context.Request.QueryString);
        throw;
    }
});

app.MapChatWebSocket();
app.MapControllers();

app.Logger.LogInformation("OnlineChat API 启动中（四层架构：Domain / Application / Infrastructure / Server）");
app.Run();
