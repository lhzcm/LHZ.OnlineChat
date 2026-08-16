# LHZ.OnlineChat

前后端分离的在线聊天系统（类 IM）。后端 ASP.NET Core + FreeSql + PostgreSQL + Redis，前端 Vue 3 + Vite + Pinia，实时通信基于自研 [LHZ.WebSocket](LHZ.WebSocket.README.md) 库。

## 技术栈

| 端 | 技术 |
|---|---|
| 后端 | .NET 10 (ASP.NET Core)、FreeSql (PostgreSQL, CodeFirst 自动建表)、StackExchange.Redis、JWT Bearer、Swagger |
| 前端 | Vue 3 (Composition API) + TypeScript、Vite 6、Pinia、Vue Router、Axios |
| 实时通信 | LHZ.WebSocket 1.0.2（自研 RFC 6455 实现）+ LHZ.WebSocket.AspNetCore 中间件 |

## 功能

- 注册：昵称（可重复）+ 邮箱（6 位数字验证码）+ 密码，注册成功自动分配**账号 ID**（int，起始 10000 自增），登录仅需账号 + 密码
- 好友：按账号 ID 申请 / 接受 / 拒绝 / 删除，实时通知（WS `friend_request`/`friend_accepted`/`friend_rejected`），在线状态实时广播（WS `online_status`）
- 群组：创建 / 加入 / 退出 / 踢人 / 解散 / 成员列表（含在线状态）
- 聊天：私聊 + 群聊（WS 实时收发）、历史消息分页、未读角标、已读标记、离线消息拉取、乐观发送（客户端 messageId 去重回显）
- 会话列表：私聊/群聊聚合（最后消息、时间、未读数），群消息离线补发（已读游标）

## 目录结构

```
LHZ.OnlineChat/
├── LHZ.OnlineChat.slnx              # 解决方案
├── LHZ.OnlineChat.Server/           # 后端 API + WebSocket 服务
│   ├── Program.cs                   # 入口：DI、JWT、FreeSql、Redis、WS 中间件
│   ├── Config/AppSettings.cs        # 配置映射
│   ├── Controllers/                 # Auth / Friends / Groups / Messages
│   ├── Services/                    # 业务服务 + Redis + WS 连接管理/消息分发
│   └── Models/                      # Entities（6 张表）+ DTOs（含 WS 协议）
├── lhz-onlinechat-web/              # 前端
│   └── src/
│       ├── api/                     # axios 封装
│       ├── stores/                  # auth / websocket / chat / friend / group
│       ├── views/                   # Login / Register / ChatLayout
│       └── router/ types/ assets/
└── LHZ.WebSocket.README.md          # 自研 WebSocket 库文档
```

## 运行

### 依赖

- PostgreSQL（默认 `192.168.2.1`，首次启动自动建库建表）
- Redis（默认 `127.0.0.1:6379`）
- .NET 10 SDK、Node.js ≥ 20

### 后端

```bash
dotnet run --project LHZ.OnlineChat.Server
```

- HTTP API: `http://localhost:5000`，Swagger: `/swagger`
- WebSocket: `ws://localhost:5000/?access_token=<JWT>`
- 启动时自动：创建数据库（若不存在）→ FreeSql CodeFirst 同步表结构

### 前端

```bash
cd lhz-onlinechat-web
npm install
npm run dev        # http://localhost:3000，/api 代理到 5000
```

生产构建：`npm run build`（产物在 `dist/`）。

### 配置

`LHZ.OnlineChat.Server/appsettings.json` 中的 `ConnectionStrings:Default`、`Redis:Connection`、`Jwt:Secret`、`Cors:AllowedOrigins`、`Smtp:*` 均可通过环境变量覆盖（ASP.NET Core 默认行为，如 `ConnectionStrings__Default`、`Redis__Connection`、`Smtp__Host`）。前端 WS 地址可用 `VITE_WS_URL` 覆盖（默认 `ws://localhost:5000`）。

### 邮箱验证码（SMTP）

注册需要邮箱验证码。未配置 `Smtp` 时（开发/演示模式），验证码**打印到服务端控制台**，并随 `POST /api/auth/send-code` 响应返回 `devCode` 字段（前端会展示提示）；配置 SMTP 后改为真实发送邮件，`devCode` 不再返回。

```json
"Smtp": { "Host": "smtp.example.com", "Port": 465, "EnableSsl": true, "User": "xxx", "Password": "***", "From": "no-reply@example.com" }
```

### 账号 ID

账号即 `User_.Id`：int 类型，起始 **10000**，每次注册自增 1。服务启动时自动迁移（列类型 bigint→integer、序列起始值 ≥10000、幂等）。

## 生产部署

建议：前端 `npm run build` 后由 Nginx/Caddy 托管静态文件，API 与 WebSocket 通过反向代理转发到后端，**HTTPS 在反向代理层终止（wss）**。

```nginx
# /etc/nginx/sites-available/onlinechat
server {
    listen 443 ssl http2;
    server_name chat.example.com;

    ssl_certificate     /etc/letsencrypt/live/chat.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/chat.example.com/privkey.pem;

    # 前端静态文件（lhz-onlinechat-web/dist）
    root /var/www/onlinechat;
    index index.html;
    location / { try_files $uri $uri/ /index.html; }

    # REST API
    location /api/ {
        proxy_pass http://127.0.0.1:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # WebSocket（wss）
    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_read_timeout 3600s;
    }
}
```

环境变量示例（systemd unit 或 docker compose）：

```bash
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5000
ConnectionStrings__Default=Host=pg-host;Database=OnlineChat;Username=postgres;Password=***
Redis__Connection=redis-host:6379
Jwt__Secret=<强随机密钥，至少32字符>
Cors__AllowedOrigins=https://chat.example.com   # 生产建议收敛来源，不要用 *
```

前端构建时注入：`VITE_WS_URL=wss://chat.example.com npm run build`（WS 走同域反代，`/` 路径由 Nginx 转发）。

## WebSocket 协议

客户端发送 / 服务端广播均为 JSON（`WsMessage`）：

| type | 方向 | 说明 |
|---|---|---|
| `private_message` | 双向 | 私聊消息；服务端转发给接收者并回显给发送者 |
| `group_message` | 双向 | 群聊消息；服务端广播给群内在线成员并回显 |
| `heartbeat` | 客户端→服务端 | 心跳，服务端回复 `{"type":"pong"}` |
| `typing` | 双向 | 正在输入状态（预留） |
| `read_receipt` | 双向 | 已读回执（预留） |
| `online_status` | 服务端→客户端 | 好友上下线通知（`content`: `online`/`offline`） |
| `friend_request` | 服务端→客户端 | 收到新好友申请，前端刷新申请列表 |
| `friend_accepted` | 服务端→客户端 | 好友申请被接受（双向通知，刷新好友列表） |
| `friend_rejected` | 服务端→客户端 | 好友申请被拒绝（通知申请人） |

字段：`from`（发送者ID）、`to`（接收者ID/群ID）、`content`、`messageId`（客户端生成则保留用于去重，否则用数据库ID）、`messageType`（0文字/1图片/2文件）、`timestamp`（毫秒）、`senderName`、`senderAvatar`。

### 补充说明

- **群离线补发**：`GroupMember.LastReadMessageId` 是群已读游标（加入群时初始化为当前最新消息ID）。用户上线时，服务端推送各群游标之后的消息（每群最多 100 条）；打开群聊（`PUT /api/messages/group/{groupId}/read`）推进游标。
- **会话列表**：`GET /api/messages/sessions` 聚合私聊 + 群聊会话（最后消息、最后时间、未读数）。

## 数据表

`User_`（Id=账号，Email 唯一）、`Friend`（0待确认/1已接受/2已屏蔽）、`Group_`、`GroupMember`（0群主/1管理员/2成员）、`PrivateMessage`、`GroupMessage`。

## License

MIT
