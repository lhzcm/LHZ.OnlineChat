# LHZ.OnlineChat

前后端分离的在线聊天系统（类 IM）。后端 ASP.NET Core + FreeSql + PostgreSQL + Redis，前端 Vue 3 + Vite + Pinia，实时通信基于自研 [LHZ.WebSocket](LHZ.WebSocket.README.md) 库。

## 技术栈

| 端 | 技术 |
|---|---|
| 后端 | .NET 10 (ASP.NET Core)、FreeSql (PostgreSQL, CodeFirst 自动建表)、StackExchange.Redis、JWT Bearer、Swagger |
| 前端 | Vue 3 (Composition API) + TypeScript、Vite 6、Pinia、Vue Router、Axios |
| 实时通信 | LHZ.WebSocket 1.0.2（自研 RFC 6455 实现）+ LHZ.WebSocket.AspNetCore 中间件 |

## 功能

- 注册 / 登录 / JWT + RefreshToken（Redis 存储）
- 好友：申请 / 接受 / 拒绝 / 删除，实时通知（WS `friend_request`），在线状态实时广播（WS `online_status`）
- 群组：创建 / 加入 / 退出 / 踢人 / 解散 / 成员列表（含在线状态）
- 聊天：私聊 + 群聊（WS 实时收发）、历史消息分页、未读角标、已读标记、离线消息拉取、乐观发送（客户端 messageId 去重回显）

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

`LHZ.OnlineChat.Server/appsettings.json` 中的 `ConnectionStrings:Default`、`Redis:Connection`、`Jwt:Secret` 均可通过环境变量覆盖（ASP.NET Core 默认行为，如 `ConnectionStrings__Default`、`Redis__Connection`）。前端 WS 地址可用 `VITE_WS_URL` 覆盖（默认 `ws://localhost:5000`）。

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

字段：`from`（发送者ID）、`to`（接收者ID/群ID）、`content`、`messageId`（客户端生成则保留用于去重，否则用数据库ID）、`messageType`（0文字/1图片/2文件）、`timestamp`（毫秒）、`senderName`、`senderAvatar`。

## 数据表

`User_`、`Friend`（0待确认/1已接受/2已屏蔽）、`Group_`、`GroupMember`（0群主/1管理员/2成员）、`PrivateMessage`、`GroupMessage`。

## License

MIT
