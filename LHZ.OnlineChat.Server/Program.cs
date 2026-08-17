using System.Text;
using LHZ.OnlineChat.Server.Config;
using LHZ.OnlineChat.Server.Services;
using LHZ.WebSocket.AspNetCore;
using LHZ.WebSocket.Core;
using LHZ.WebSocket.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ==================== 配置绑定 ====================
var appSettings = new AppSettings();
builder.Configuration.Bind(appSettings);
builder.Services.AddSingleton(appSettings);
Console.WriteLine($"[CFG] Smtp.Host='{appSettings.Smtp.Host}', Port={appSettings.Smtp.Port}, EnableSsl={appSettings.Smtp.EnableSsl}, User='{appSettings.Smtp.User}', From='{appSettings.Smtp.From}'");

// ==================== 确保数据库存在 ====================
EnsureDatabaseExists(appSettings.ConnectionStrings.Default);

// ==================== FreeSql ORM ====================
var fsql = new FreeSql.FreeSqlBuilder()
    .UseConnectionString(FreeSql.DataType.PostgreSQL, appSettings.ConnectionStrings.Default)
    .UseAutoSyncStructure(true)  // 自动同步表结构（CodeFirst）
    .UseMonitorCommand(cmd => Console.WriteLine($"[SQL] {cmd.CommandText}"))
    .Build();

// 初始化数据库表结构
fsql.CodeFirst.SyncStructure(
    typeof(LHZ.OnlineChat.Server.Models.Entities.User),
    typeof(LHZ.OnlineChat.Server.Models.Entities.Friend),
    typeof(LHZ.OnlineChat.Server.Models.Entities.FriendTag),
    typeof(LHZ.OnlineChat.Server.Models.Entities.Group_),
    typeof(LHZ.OnlineChat.Server.Models.Entities.GroupMember),
    typeof(LHZ.OnlineChat.Server.Models.Entities.PrivateMessage),
    typeof(LHZ.OnlineChat.Server.Models.Entities.GroupMessage)
);

// // 账号 ID 迁移：用户 ID 列改为 int，序列起始值 ≥ 10000（账号从 10000 开始自增）
// EnsureAccountIdSchema(fsql);

builder.Services.AddSingleton(fsql);

// ==================== Redis ====================
var redisService = new RedisService(appSettings.Redis.Connection);
builder.Services.AddSingleton(redisService);

// ==================== 邮件服务 ====================
builder.Services.AddSingleton<EmailService>();

// ==================== JWT 认证 ====================
var jwtSecret = Encoding.UTF8.GetBytes(appSettings.Jwt.Secret);
builder.Services.AddAuthentication(options =>
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
        IssuerSigningKey = new SymmetricSecurityKey(jwtSecret),
        ClockSkew = TimeSpan.Zero
    };
    // 支持从查询字符串中读取 Token（WebSocket 连接使用）
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();

// ==================== 业务服务 ====================
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<FriendService>();
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<MessageService>();

// WebSocket 服务（单例）
builder.Services.AddSingleton<WsConnectionManager>();
builder.Services.AddSingleton<WsMessageHandler>();
builder.Services.AddHttpContextAccessor();

// ==================== Controllers ====================
builder.Services.AddControllers();

// ==================== Swagger ====================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "OnlineChat API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token",
        Name = "Authorization",
        In = Microsoft.OpenApi.ParameterLocation.Header,
        Type = Microsoft.OpenApi.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    options.AddSecurityRequirement(doc => new Microsoft.OpenApi.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer", doc, null!),
            new List<string>()
        }
    });
});

// ==================== CORS ====================
builder.Services.AddCors(options =>
{
    var origins = appSettings.Cors.AllowedOrigins;
    options.AddDefaultPolicy(policy =>
    {
        if (string.IsNullOrWhiteSpace(origins) || origins == "*")
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            policy.WithOrigins(origins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

var app = builder.Build();

app.Urls.Add("http://0.0.0.0:5000"); // 0.0.0.0:本地与 Docker 容器内均可访问

// ==================== 上传文件静态服务 ====================
var uploadsDir = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(uploadsDir);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsDir),
    RequestPath = "/uploads"
});

// ==================== 中间件管道 ====================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// ==================== WebSocket 中间件 ====================
app.UseWebSocket(context =>
{
    var accessor = app.Services.GetRequiredService<IHttpContextAccessor>();
    var httpContext = accessor.HttpContext;

    // 验证用户已通过 JWT 认证
    if (httpContext?.User?.Identity?.IsAuthenticated != true)
    {
        Console.WriteLine("[WS] 连接被拒绝：未认证");
        context.Dispose();
        return;
    }

    var userIdClaim = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
    {
        Console.WriteLine("[WS] 连接被拒绝：无法解析用户ID");
        context.Dispose();
        return;
    }

    // 完成 WebSocket 握手
    var client = context.HttpUpgrade();
    if (client == null)
    {
        Console.WriteLine("[WS] 握手失败");
        return;
    }

    var connectionManager = app.Services.GetRequiredService<WsConnectionManager>();
    var messageHandler = app.Services.GetRequiredService<WsMessageHandler>();

    // 绑定消息接收事件
    client.OnMessageReceived += (IWebSocketClient sender, string message) =>
    {
        _ = messageHandler.HandleMessageAsync(sender, userId, message);
    };

    // 收到关闭帧：关闭连接，统一由 OnClientClose 完成清理
    client.OnCloseRecived += (IWebSocketClient sender, CloseMessage msg) =>
    {
        Console.WriteLine($"[WS] 收到关闭帧: {userId} — {msg.CloseCode}");
        sender.Close();
    };

    // 连接断开统一清理（带身份校验，旧连接不会误删新连接）
    client.OnClientClose += (IWebSocketClient sender) =>
    {
        Console.WriteLine($"[WS] 连接断开: {userId}");
        _ = Task.Run(async () =>
        {
            try
            {
                // 仅当该连接仍登记在册时清理并广播下线，避免重复触发
                if (await connectionManager.RemoveConnectionAsync(userId, sender))
                {
                    await messageHandler.NotifyFriendsStatusAsync(userId, online: false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WS] 下线清理失败: {userId} — {ex.Message}");
            }
        });
    };

    // 先登记新连接（覆盖旧映射），再关闭旧连接：
    // 旧连接的关闭回调因身份不匹配而不会触发清理/离线广播，避免上下线抖动
    _ = connectionManager.AddConnectionAsync(userId, client);

    var oldClient = connectionManager.GetConnection(userId);
    if (oldClient != null && oldClient != client && oldClient.Status == LHZ.WebSocket.Enums.ClientStatus.Opend)
    {
        Console.WriteLine($"[WS] 用户 {userId} 重复登录，关闭旧连接");
        oldClient.Close();
    }

    // 广播上线状态给在线好友
    _ = messageHandler.NotifyFriendsStatusAsync(userId, online: true);

    // 补发群离线消息（已读游标之后）
    _ = messageHandler.SendGroupOfflineMessagesAsync(userId);

    Console.WriteLine($"[WS] 用户 {userId} WebSocket 握手完成");
});

app.MapControllers();
// ==================== 启动 ASP.NET Core ====================
Console.WriteLine("🚀 ASP.NET Core Web API starting...");
app.Run();

// ==================== 确保数据库存在 ====================
static void EnsureDatabaseExists(string connectionString)
{
    // 从连接字符串中提取数据库名
    var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
    var database = builder.Database;
    
    // 连接到默认 postgres 数据库来创建目标库
    builder.Database = "postgres";
    using var conn = new Npgsql.NpgsqlConnection(builder.ConnectionString);
    conn.Open();
    
    // 检查数据库是否存在
    using var cmd = new Npgsql.NpgsqlCommand(
        $"SELECT 1 FROM pg_database WHERE datname = '{database}'", conn);
    var exists = cmd.ExecuteScalar() != null;
    
    if (!exists)
    {
        using var createCmd = new Npgsql.NpgsqlCommand(
            $"CREATE DATABASE \"{database}\"", conn);
        createCmd.ExecuteNonQuery();
        Console.WriteLine($"✅ 数据库 \"{database}\" 已创建");
    }
}

// // ==================== 账号 ID 迁移（幂等）====================
// // 用户 ID 列统一为 int；User_ 序列起始值设为 ≥10000，新账号从 10000 开始自增
// static void EnsureAccountIdSchema(IFreeSql fsql)
// {
//     var sql = """
//         DO $$
//         BEGIN
//           -- 用户 ID 相关列从 bigint 降为 integer（数据均在 int 范围内）
//           IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='User_' AND column_name='Id' AND data_type='bigint') THEN
//             ALTER TABLE "User_" ALTER COLUMN "Id" TYPE integer;
//           END IF;
//           IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='User_' AND column_name='Username') THEN
//             ALTER TABLE "User_" DROP COLUMN "Username";
//           END IF;
//           IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='Friend' AND column_name='UserId' AND data_type='bigint') THEN
//             ALTER TABLE "Friend" ALTER COLUMN "UserId" TYPE integer, ALTER COLUMN "FriendId" TYPE integer;
//           END IF;
//           IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='GroupMember' AND column_name='UserId' AND data_type='bigint') THEN
//             ALTER TABLE "GroupMember" ALTER COLUMN "UserId" TYPE integer;
//           END IF;
//           IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='Group_' AND column_name='OwnerId' AND data_type='bigint') THEN
//             ALTER TABLE "Group_" ALTER COLUMN "OwnerId" TYPE integer;
//           END IF;
//           IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='PrivateMessage' AND column_name='SenderId' AND data_type='bigint') THEN
//             ALTER TABLE "PrivateMessage" ALTER COLUMN "SenderId" TYPE integer, ALTER COLUMN "ReceiverId" TYPE integer;
//           END IF;
//           IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='GroupMessage' AND column_name='SenderId' AND data_type='bigint') THEN
//             ALTER TABLE "GroupMessage" ALTER COLUMN "SenderId" TYPE integer;
//           END IF;
//         END $$;
//         SELECT setval(pg_get_serial_sequence('"User_"', 'Id'),
//                       GREATEST((SELECT COALESCE(MAX("Id"), 0) FROM "User_"), 9999));
//         """;
//     fsql.Ado.ExecuteNonQuery(sql);
//     Console.WriteLine("✅ 账号 ID 迁移完成（int 类型，起始 10000 自增）");
// }
