# LHZ.OnlineChat

前后端分离的在线聊天系统(类 IM),功能覆盖注册登录、好友、群组、实时聊天、@ 提及、表情、会话聚合、个人信息管理,开箱即用(Docker 一键部署)。

## 🚀 在线试用

**http://chat.onlinemusic.top/chat** — 注册账号即可体验(建议电脑端访问,移动端同样支持)

- 后端:ASP.NET Core (.NET 10) + FreeSql + PostgreSQL + Redis
- 前端:Vue 3 + TypeScript + Vite + Pinia
- 实时通信:自研 [LHZ.WebSocket](LHZ.WebSocket.README.md) 库(RFC 6455 实现)
- JSON 序列化:自研 [LHZ.FastJson](https://www.nuget.org/packages/LHZ.FastJson)(WS 协议 camelCase 双向兼容)

## ✨ 功能总览

**账号体系**
- 注册:昵称(可重复)+ 邮箱(**6 位数字验证码**,SMTP 发送)+ 密码,注册成功自动分配**账号 ID**(int,起始 10000 自增)
- 登录:仅账号 ID + 密码;JWT + RefreshToken(Redis 反查,O(1) 轮换)

**个人信息**
- 修改昵称、上传头像(图片校验、`/uploads` 静态服务)、**换绑邮箱**(新邮箱验证码 + 唯一性校验,不能与其他账号重复)
- 全站头像支持真实图片(Avatar 组件,无头像时渐变首字母)

**好友**
- 按账号 ID 申请/接受/拒绝/删除,实时通知(WS:`friend_request`/`friend_accepted`/`friend_rejected`)
- **好友备注**(备注名优先显示,设置者视角独立,双方互不可见)
- **分类标签**(家人/朋友/同事/同学/客户/其他或自定义,好友列表按分类分组、未分组置底)
- 在线状态实时广播(WS `online_status`)

**群组**
- 创建/加入/退出/踢人(权限分级:群主 0/管理员 1/成员 2)/解散/成员列表(含在线状态)
- **群主/管理员邀请好友入群**(仅限自己的好友、排除已在群成员、批量邀请,被邀请者实时收到 `group_invited`)

**🤖 机器人(Webhook)**
- **私人机器人助理**:创建后自动成为好友,私聊即触发;**群机器人**:群主/管理员把机器人拉进群,**被 @ 时触发**
- 收到消息 → 系统 POST 事件(JSON,`X-Bot-Signature: HMAC-SHA256(secret, rawBody)` 签名)到你的 Webhook 地址
- **同步回复**:回调返回 `200 {"content":"回复文本"}` 即自动以机器人身份回复(10s 超时,失败重试 1 次,自动带回复引用)
- **异步回复**:`POST /api/robots/{id}/reply`(同样 HMAC 验签),适合 AI 思考久/定时消息场景
- 管理面板:创建/编辑/删除/**测试触发**;机器人有独立账号 ID、禁止登录、🤖 标识,好友/会话/群成员列表可见

**聊天**
- 私聊 + 群聊实时收发、历史分页、未读角标、已读标记(私聊/群已读游标)、离线消息拉取、乐观发送(messageId 去重回显)
- **群聊 @ 提及**:输入 `@` 或点击 @ 按钮弹出成员选择器(按昵称过滤),消息携带 `mentions`,气泡内 `@昵称` 高亮,被 @ 的消息主色描边
- **表情面板**:5 类 136 个 emoji,光标处插入
- 会话列表(私聊/群聊聚合:最后消息、时间、未读数,私聊显示我的备注)
- 群消息离线补发(已读游标之后,每群上限 100 条)

**界面**
- 现代 IM 风格:渐变气泡、彩色头像、胶囊 Tab、弹窗动画;移动端列表↔聊天切换 + 安全区适配

## 🏗️ 技术栈

| 端 | 技术 |
|---|---|
| 后端 | .NET 10 (ASP.NET Core)、FreeSql (PostgreSQL, CodeFirst 自动建表)、StackExchange.Redis、JWT Bearer、BCrypt、MailKit (SMTP)、Swagger |
| 前端 | Vue 3 (Composition API) + TypeScript、Vite 6、Pinia、Vue Router、Axios |
| 实时通信 | LHZ.WebSocket (自研 RFC 6455) + LHZ.WebSocket.AspNetCore 中间件 |
| 序列化 | LHZ.FastJson 2.0.1-pre(WS 协议 camelCase,`[JsonProperty]` 标注) |

## 📁 目录结构

```
LHZ.OnlineChat/
├── docker-compose.yml            # Docker 编排(Postgres/Redis/后端/前端 nginx)
├── .env.example                  # 部署配置模板
├── DEPLOY.md                     # 线上部署手册(HTTPS/备份/运维)
├── README.md
├── LHZ.WebSocket.README.md       # 自研 WebSocket 库文档
├── LHZ.OnlineChat.Server/        # 后端 API + WebSocket
│   ├── Program.cs                # 入口:DI、JWT、FreeSql、Redis、WS 中间件、uploads 静态服务
│   ├── Dockerfile                # 多阶段构建(restore → publish → aspnet 10)
│   ├── Config/                   # AppSettings(连接串/Redis/JWT/CORS/SMTP)
│   ├── Controllers/              # Auth / Friends / Groups / Messages
│   ├── Services/                 # 业务服务 + Email + Redis + WS 连接管理/消息分发
│   └── Models/
│       ├── Entities/             # User / Friend / FriendTag / Group_ / GroupMember / PrivateMessage / GroupMessage
│       └── DTOs/                 # 请求/响应 + WS 协议(WsMessage)
└── lhz-onlinechat-web/           # 前端
    ├── Dockerfile + nginx.conf   # 构建 → nginx 托管静态文件 + 反代 API/WS/uploads
    ├── .env.development          # 开发环境 WS 地址
    └── src/
        ├── api/                  # axios 封装(auth/friend/group/message)
        ├── stores/               # auth / websocket / chat / friend / group (Pinia)
        ├── components/           # Avatar 组件(真实头像/渐变首字母)
        ├── constants/            # emoji 数据
        ├── utils/                # 头像工具
        ├── views/                # Login / Register / ChatLayout
        └── router/ types/ assets/
```

## 🚀 本地运行

### 依赖

- PostgreSQL、Redis(后端启动时自动建库建表)
- .NET 10 SDK、Node.js ≥ 20

### 后端

```bash
dotnet run --project LHZ.OnlineChat.Server
```

- HTTP API:`http://localhost:5000`,Swagger(开发环境):`/swagger`
- WebSocket:`ws://localhost:5000/?access_token=<JWT>`
- 启动自动:创建数据库(若不存在)→ CodeFirst 同步表结构 → 账号 ID 序列迁移(起始 10000)
- 上传的头像保存在 `LHZ.OnlineChat.Server/uploads/`,经 `/uploads/*` 访问

### 前端

```bash
cd lhz-onlinechat-web
npm install
npm run dev        # http://localhost:3000，/api 代理到 5000
```

生产构建:`npm run build`(产物 `dist/`)。

### 配置(appsettings.json / 环境变量)

| 配置 | 说明 |
|---|---|
| `ConnectionStrings:Default` | PostgreSQL 连接串 |
| `Redis:Connection` | Redis 连接串 |
| `Jwt:Secret/Issuer/Audience/ExpireMinutes` | JWT 配置(Secret 至少 32 字符) |
| `Smtp:Host/Port/User/Password/From` | 邮件验证码;**留空为开发模式**:验证码打印到后端控制台并随 `send-code` 接口返回 `devCode` |
| `Cors:AllowedOrigins` | 允许来源,逗号分隔;`*` 允许全部 |

均可通过环境变量覆盖(如 `ConnectionStrings__Default`、`Smtp__Host`)。前端 WS 地址:开发用 `.env.development` 的 `VITE_WS_URL=ws://localhost:5000`;生产留空自动使用当前站点同域 `/ws`(https 下自动 wss)。

## 🐳 生产部署

**详细手册见 [DEPLOY.md](DEPLOY.md)**(服务器准备 / HTTPS / 备份 / 运维)。核心三步:

```bash
cp .env.example .env        # 修改 POSTGRES_PASSWORD、JWT_SECRET、SMTP 等
docker compose up -d --build
```

- 前端入口:`http://服务器IP:8080`(配 HTTPS 后反代到 80/443,推荐 Caddy 自动证书)
- 数据持久化:卷 `pgdata` / `redisdata` / `uploaddata`(头像)
- 更新:`git pull && docker compose up -d --build`

## 📡 WebSocket 协议

客户端发送 / 服务端广播均为 JSON(`WsMessage`,字段 camelCase,经 LHZ.FastJson 序列化):

```json
{ "type": "private_message", "from": "10000", "to": "10001",
  "content": "你好", "timestamp": 1786000000000, "messageId": "uuid",
  "messageType": 0, "senderName": "小明", "senderAvatar": null, "mentions": [] }
```

| type | 方向 | 说明 |
|---|---|---|
| `private_message` | 双向 | 私聊;转发接收者 + 回显发送者(保留客户端 messageId 去重) |
| `group_message` | 双向 | 群聊;广播群内在线成员 + 回显;`mentions` 携带被 @ 的成员 ID |
| `heartbeat` | 客户端→服务端 | 心跳,服务端回复 `{"type":"pong"}` |
| `typing` | 双向 | 正在输入(预留) |
| `read_receipt` | 双向 | 已读回执(预留) |
| `online_status` | 服务端→客户端 | 好友上下线(`content`: `online`/`offline`) |
| `friend_request` | 服务端→客户端 | 收到新好友申请 |
| `friend_accepted` / `friend_rejected` | 服务端→客户端 | 申请被接受(双向)/ 被拒绝 |
| `group_invited` | 服务端→客户端 | 被邀请加入群组(`from` 为群 ID) |

**字段**:`from`(发送者ID)、`to`(接收者ID/群ID)、`content`、`messageId`(客户端生成则保留用于去重,否则用数据库 ID)、`messageType`(0文字/1图片/2文件)、`timestamp`(毫秒)、`senderName`、`senderAvatar`、`mentions`(群聊 @ 的成员 ID 列表)。

**补充机制**
- **消息去重**:历史/离线/群补发接口均返回与 WS 推送一致的 `messageId`(数据库 `ClientMessageId` 列),前端按此去重,不会出现重复消息
- **群离线补发**:`GroupMember.LastReadMessageId` 已读游标,上线推送游标之后的消息(每群 ≤100 条),打开群聊推进游标
- **会话列表**:`GET /api/messages/sessions` 聚合私聊 + 群聊(最后消息/时间/未读数;私聊名优先显示我的备注)

## 🗄️ 数据表

| 表 | 说明 |
|---|---|
| `User_` | 用户(Id=账号,Email 唯一,Avatar/昵称) |
| `Friend` | 好友关系(Status: 0待确认/1已接受/2已屏蔽) |
| `FriendTag` | 好友设置(设置者视角的备注 Remark / 分类 Category) |
| `Group_` | 群组(OwnerId,公告 Announcement) |
| `GroupMember` | 群成员(Role: 0群主/1管理员/2成员,LastReadMessageId 已读游标) |
| `PrivateMessage` / `GroupMessage` | 私聊/群聊消息(ClientMessageId 客户端 ID,Mentions 提及) |
| `RobotProfile` | 机器人配置(机器人账号=User 表 IsBot=true 的行,WebhookUrl/Secret/超时) |

## 📜 License

MIT
