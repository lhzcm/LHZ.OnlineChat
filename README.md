# LHZ.OnlineChat

前后端分离的在线聊天系统(类 IM),功能覆盖注册登录、好友、群组、实时聊天、@ 提及、表情、会话聚合、个人信息管理,开箱即用(Docker 一键部署)。

## 🚀 在线试用

**http://chat.onlinemusic.top/chat** — 注册账号即可体验(建议电脑端访问,移动端同样支持)

- 后端:ASP.NET Core (.NET 10) + **DDD 四层架构 + MediatR CQRS** + FreeSql + PostgreSQL + Redis
- 前端:Vue 3 + TypeScript + Vite + Pinia
- 实时通信:自研 LHZ.WebSocket 库(RFC 6455 实现)
- JSON 序列化:自研 [LHZ.FastJson](https://www.nuget.org/packages/LHZ.FastJson)(WS 协议 camelCase 双向兼容)
- 测试:903 个单元测试(xUnit),全量约 1.3 秒

## ✨ 功能总览

**账号体系**
- 注册:昵称(可重复)+ 邮箱(**6 位数字验证码**,SMTP 发送)+ 密码,注册成功自动分配**账号 ID**(int,起始 10000 自增)
- 登录:仅账号 ID + 密码;JWT + RefreshToken(Redis 反查,O(1) 轮换)
- **多端登录**:同一账号可多台设备同时在线(消息全端同步),个人资料 →「登录设备」可查看/踢下线任意设备、一键退出其他所有设备;被踢设备 API 立即 401、WebSocket 收到 `kicked` 通知后自动登出
- **忘记密码**:登录页入口,邮箱验证码(校验邮箱已注册)重置密码,重置后所有登录会话失效
- **修改密码**:登录态验证原密码修改,修改后其他设备全部下线

**个人信息**
- 修改昵称、**头像裁剪上传**(选择图片后弹出裁剪窗口:拖动/缩放调整,512×512 方形导出)、**换绑邮箱**(新邮箱验证码 + 唯一性校验,不能与其他账号重复)
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
- **异步回复/主动推送**:`POST /api/robots/{令牌}/reply`;**Webhook 地址可留空**——纯推送模式:不接收消息回调,仅由第三方主动推送
- **安全**:对外暴露的是**加密 ID 令牌**(AES-256-GCM,由 `Robot__TokenKey` 派生密钥,管理面板一键复制),不泄露内部自增 ID;**签名可选**——配置了 `WebhookSecret` 才强制验签,未配置则仅靠令牌鉴权
- 管理面板:创建/编辑/删除/**测试触发**;机器人有独立账号 ID、禁止登录、🤖 标识,好友/会话/群成员列表可见

**聊天**
- 私聊 + 群聊实时收发、历史分页、未读角标、已读标记(私聊/群已读游标)、离线消息拉取、乐观发送(messageId 去重回显)
- **全局消息搜索** + **会话内搜索**:聊天窗口放大镜入口,按关键词搜索当前会话消息(防抖实时出结果、可加载更多),点击结果自动定位消息并**高亮关键词**(`<mark>`);服务端 pg_trgm(trigram)GIN 索引加速 `LIKE '%关键词%'`,大数据量下不退化
- **群聊 @ 提及**:输入 `@` 或点击 @ 按钮弹出成员选择器(按昵称过滤),消息携带 `mentions`,气泡内 `@昵称` 高亮,被 @ 的消息主色描边
- **表情面板**:5 类 136 个 emoji,光标处插入
- 会话列表(私聊/群聊聚合:最后消息、时间、未读数,私聊显示我的备注)
- 群消息离线补发(已读游标之后,每群上限 100 条)

**界面**
- 现代 IM 风格:渐变气泡、彩色头像、胶囊 Tab、弹窗动画;移动端列表↔聊天切换 + 安全区适配
- **浏览器桌面通知**:页面在后台时新消息弹系统通知(标题=发送者、内容=消息),点击通知直达会话;个人资料可开关
- **全局免打扰时段**:设置起止时间(支持跨午夜),时段内不弹通知、不响提示音,未读角标照常累计
- **PWA**:manifest + 图标 + Service Worker,可安装到桌面/主屏,离线打开应用;`/manifest.webmanifest` 已配正确 MIME

## 🏗️ 技术栈

| 端 | 技术 |
|---|---|
| 后端 | .NET 10 (ASP.NET Core)、**DDD 四层架构 + MediatR CQRS**、FreeSql (PostgreSQL, CodeFirst 自动建表)、StackExchange.Redis、JWT Bearer、BCrypt、MailKit (SMTP)、Swagger |
| 前端 | Vue 3 (Composition API) + TypeScript、Vite 6、Pinia、Vue Router、Axios |
| 实时通信 | LHZ.WebSocket 1.2.0 (自研 RFC 6455) + LHZ.WebSocket.AspNetCore 中间件 |
| 序列化 | LHZ.FastJson 2.0.1-pre(WS 协议 camelCase,`[JsonProperty]` 标注) |
| 测试 | xUnit 2.9,手写内存测试替身(不依赖 mock 框架) |

## 🧱 后端架构(DDD 四层)

依赖方向严格由外向内,用**独立 csproj 在编译期强制**,而非靠自觉:

```
Server ──→ Infrastructure ──→ Application ──→ Domain
(HTTP/WS)    (FreeSql/Redis)    (用例/CQRS)    (领域模型,零依赖)
```

| 层 | 职责 | 不允许出现 |
|---|---|---|
| **Domain** | 聚合根、值对象、领域事件、仓储接口。业务规则的唯一归属地 | 任何外部包(连 ORM / JSON / DI 都不引用) |
| **Application** | 一个用例一个 Command/Query + Handler;声明基础设施端口(接口) | SQL、HttpContext、协议报文 |
| **Infrastructure** | 仓储实现、Redis、WS 推送、JWT、邮件、Webhook;实现 Application 声明的端口 | 业务规则 |
| **Server** | 控制器(仅转发到 MediatR)、鉴权管道、DI 组合根 | 业务逻辑 |

**限界上下文**:Users / Friends / Blacklists / Groups / Messaging / Robots / Admins

几个关键设计点:

- **领域层零 ORM 依赖**:实体是纯 C#(私有 setter + 私有构造),持久化映射由 Infrastructure 的 FreeSql **FluentApi** 完成,`CodeFirst` 自动建表能力不受影响
- **值对象**:`Email`、`PasswordHash`、`GroupAnnouncement`、`MentionList`、`MessageReply`、`WebhookUrl` —— 校验与归一化收敛在类型内部;需要落库的经 FreeSql `TypeHandler` 双向转换
- **领域事件解耦副作用**:`UserPasswordChanged` → 踢全部会话;`UserBanned` → 踢设备;`UserBlocked` → 解好友 + 推通知;`GroupDissolved` → 通知成员 + 清会话设置。改造前这些副作用在各个 Service 里被分别手写,漏一处就是缺陷
- **管道统一错误转换**:领域层只 `throw DomainException`,`DomainExceptionBehavior` 统一转成 `ApiResponse.Fail` —— HTTP 响应形状与改造前完全一致,前端无需改动
- **小聚合**:`Group` 与 `GroupMember`、消息都是独立聚合,按标识引用,避免每次发言把整群成员载入内存

## 📁 目录结构

仓库按**部署单元**分区:`backend/` 与 `frontend/` 各自独立,根目录只放编排与共享配置。

```
LHZ.OnlineChat/
├── docker-compose.yml                  # 编排(Postgres/Redis/后端/前端/管理后台)
├── .env.example                        # 部署配置模板
├── nuget.config
├── README.md
├── docs/
│   └── DEPLOY.md                       # 线上部署手册(HTTPS/备份/运维)
│
├── backend/                            # ===== 后端(DDD 四层)=====
│   ├── LHZ.OnlineChat.slnx             # 解决方案(四层 + 三个测试工程)
│   ├── Directory.Build.props           # 四层共用编译设置
│   │
│   ├── LHZ.OnlineChat.Domain/          # ① 领域层(零外部依赖)
│   │   ├── Common/                     # AggregateRoot / ValueObject / DomainException / IClock
│   │   ├── Users/                      # User 聚合 + Email/PasswordHash 值对象 + 事件
│   │   ├── Friends/                    # Friendship / FriendSetting
│   │   ├── Blacklists/                 # BlacklistEntry
│   │   ├── Groups/                     # Group / GroupMember / GroupAnnouncement + GroupRole
│   │   ├── Messaging/                  # PrivateMessage / GroupMessage / SessionSetting
│   │   │                               #   + MentionList / MessageReply / RecallPolicy
│   │   ├── Robots/                     # Robot + WebhookUrl
│   │   └── Admins/                     # Admin / AdminAuditLog + AuditActions
│   │
│   ├── LHZ.OnlineChat.Application/     # ② 应用层(用例 + 端口)
│   │   ├── Common/                     # ApiResponse / 管道 Behavior / 领域事件派发
│   │   ├── Abstractions/               # 端口:实时推送/会话/缓存/邮件/存储/Webhook/审计
│   │   └── <上下文>/Commands|Queries|EventHandlers/
│   │
│   ├── LHZ.OnlineChat.Infrastructure/  # ③ 基础设施层(端口实现)
│   │   ├── Persistence/                # FluentApi 映射 + 仓储 + TypeHandler + 启动迁移
│   │   ├── Caching/                    # Redis:会话/验证码/在线状态/消息缓存
│   │   ├── Realtime/                   # WS 连接管理 + 协议封包 + 入站分发
│   │   ├── Security/                   # JWT / BCrypt / 机器人令牌 AES-GCM / HMAC
│   │   ├── Messaging/ Storage/ Bots/   # 邮件 / 文件 / Webhook 调度
│   │   └── DependencyInjection.cs
│   │
│   ├── LHZ.OnlineChat.Server/          # ④ 表现层(HTTP + WebSocket)
│   │   ├── Program.cs                  # 组合根:分层装配、JWT、CORS、管道
│   │   ├── Controllers/                # 薄转发到 MediatR(含 Admin/)
│   │   ├── Authentication/             # ICurrentUser 实现 + AdminAuthorize
│   │   ├── Realtime/                   # WS 端点(握手鉴权)
│   │   ├── Configuration/              # appsettings 绑定
│   │   └── Dockerfile                  # 多阶段构建(四层 restore → publish → aspnet 10)
│   │
│   └── tests/                          # 单元测试(见「单元测试」一节)
│       ├── LHZ.OnlineChat.Domain.Tests/
│       ├── LHZ.OnlineChat.Application.Tests/
│       └── LHZ.OnlineChat.Infrastructure.Tests/
│
├── frontend/                           # ===== 前端 =====
│   ├── lhz-onlinechat-web/             # 用户端(PWA)
│   │   ├── Dockerfile + nginx.conf     # 构建 → nginx 托管静态文件 + 反代 API/WS/uploads
│   │   ├── .env.development            # 开发环境 WS 地址
│   │   └── src/
│   │       ├── api/                    # axios 封装(auth/friend/group/message)
│   │       ├── stores/                 # auth / websocket / chat / friend / group (Pinia)
│   │       ├── components/             # Avatar / 聊天区 / 各类弹窗
│   │       ├── constants/ utils/       # emoji 数据 / 头像工具
│   │       ├── views/                  # Login / Register / ForgotPassword / ChatLayout
│   │       └── router/ types/ assets/
│   │
│   └── admin-web/                      # 管理后台(独立构建,经主前端 /admin 反代)
│       ├── Dockerfile + nginx.conf
│       └── src/views/                  # Dashboard / Users / Groups / Admins / 审计日志
│
└── plugins/
    └── dsh-bot-notify/                 # DeepSeek Harness 客户端推送插件(见下)
```

## 🧪 单元测试

```bash
cd backend
dotnet test                                       # 全部 903 个用例，约 1.3 秒
dotnet test tests/LHZ.OnlineChat.Domain.Tests     # 只跑领域层
```

| 测试工程 | 用例数 | 耗时 | 覆盖内容 | 外部依赖 |
|---|---:|---:|---|---|
| `Domain.Tests` | 343 | 94ms | 聚合根行为与不变量、值对象校验与归一化、领域事件、权限/禁言/撤回规则 | 无(纯内存) |
| `Application.Tests` | 420 | 161ms | 全部用例的成功路径与失败分支、领域事件订阅方的副作用、管道异常转换 | 无(内存仓储 + 端口替身) |
| `Infrastructure.Tests` | 140 | 1s | BCrypt、JWT 声明、机器人令牌 AES-GCM、HMAC 验签、实体映射元数据、Redis 键位、本地文件存储 | 无 |

几点约定:

- **不用 mock 框架**,一律手写内存测试替身([TestDoubles/](backend/tests/LHZ.OnlineChat.Application.Tests/TestDoubles/))。内存仓储会真的分配自增主键,因此"忘了回填 Id 就发事件"这类顺序错误测得出来;mock 测不出。
- **领域层零依赖的直接收益**:343 个领域测试不需要数据库、不需要容器,全部跑完 94 毫秒。
- **事件订阅方也在覆盖范围内**:`RecordingEventDispatcher.Subscribe()` 可以挂真实处理器,所以"改密 → 踢全部会话""拉黑 → 解好友 + 推通知"这类链路是被验证过的,而不只是"事件发出来了"。
- **实体映射有专门的测试**:表名/列名一旦与既有 schema 对不上,线上会建出新表或读不到数据,而编译期毫无提示 —— 见 [MappingTests.cs](backend/tests/LHZ.OnlineChat.Infrastructure.Tests/Persistence/MappingTests.cs)。
- **需要真实 PostgreSQL / Redis / SMTP 的部分不在单元测试里糊弄**(那只会测出替身自己的行为),仓储查询与会话存储属于集成测试范畴。

## 🚀 本地运行

### 依赖

- PostgreSQL、Redis(后端启动时自动建库建表)
- .NET 10 SDK、Node.js ≥ 20

### 后端

```bash
dotnet run --project backend/LHZ.OnlineChat.Server
```

- HTTP API:`http://localhost:5000`,Swagger(开发环境):`/swagger`
- WebSocket:`ws://localhost:5000/?access_token=<JWT>`
- 启动自动:创建数据库(若不存在)→ CodeFirst 同步表结构 → 账号 ID 序列迁移(起始 10000)→ pg_trgm 搜索索引 → 初始超管
- 上传的头像保存在 `backend/LHZ.OnlineChat.Server/uploads/`,经 `/uploads/*` 访问
- `LHZ.OnlineChat.Server` 只是启动项目;`dotnet build backend/LHZ.OnlineChat.slnx` 会按依赖顺序构建四层

### 前端

```bash
cd frontend/lhz-onlinechat-web
npm install
npm run dev        # http://localhost:3000，/api 代理到 5000
```

生产构建:`npm run build`(产物 `dist/`)。管理后台在 `frontend/admin-web`,命令相同。

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

**详细手册见 [docs/DEPLOY.md](docs/DEPLOY.md)**(服务器准备 / HTTPS / 备份 / 运维)。核心两步:

```bash
cp .env.example .env        # 必填:POSTGRES_PASSWORD、REDIS_PASSWORD、JWT_SECRET、ROBOT_TOKEN_KEY
docker compose up -d --build
```

> `.env` 必须创建:compose 里的敏感变量都是硬性要求,缺失时会直接报错退出,不会退回内置默认口令。
> PostgreSQL / Redis 的宿主端口只绑定 `127.0.0.1`(默认 55432 / 56379),仅供本机管理;对外只有 `WEB_PORT`(8080)。
> 容器均以非 root 运行:backend 使用 `appuser`(UID 10001),两个前端使用 `nginx-unprivileged` 镜像(UID 101)。

- 前端入口:`http://服务器IP:8080`(配 HTTPS 后反代到 80/443,推荐 Caddy 自动证书)
- 数据持久化:卷 `pgdata` / `redisdata` / `uploaddata`(头像)
- 健康检查:`docker compose ps` 中 postgres / redis / backend / frontend 显示 `healthy`(backend 探针为 `GET /health`)
- 更新:`git pull && docker compose up -d --build`

## 🤖 机器人接入(第三方)

### 1. 主动推送(第三方 → 用户,最常用)

第三方服务(监控告警、业务通知、AI 回复等)随时让机器人给用户/群发消息,不依赖用户先给机器人发消息。

**接口**:`POST {站点地址}/api/robots/{令牌}/reply`(管理面板「我的机器人」里可复制完整调用链接,`令牌` 为 AES-256-GCM 加密 ID,不泄露内部 ID)

**请求体**(JSON):

| 字段 | 类型 | 说明 |
|---|---|---|
| `sessionType` | string | `private` 私聊 / `group` 群聊 |
| `sessionId` | number | 私聊:接收方账号 ID;群聊:群 ID(机器人需已加入该群) |
| `content` | string | 消息内容(≤ 5000 字) |
| `replyTo` | string? | 可选,被引用消息的 messageId |

**鉴权(可选)**:机器人在设置里配置了「签名密钥」后,推送必须携带 `X-Bot-Signature` 请求头,值为对**请求体原始字节**计算的 `HMAC-SHA256(密钥, body)` 十六进制小写;未配置密钥则仅靠令牌鉴权,直接调用即可。

**限制**:私聊推送要求目标用户与机器人是好友关系(机器人创建时自动与创建者互为好友,即默认只能推送给创建者本人)。

**响应**:`200 {"success":true,"message":"已发送"}`;失败返回 `400 {"success":false,"message":"原因"}`。

#### 实例:Node.js

```js
// 未配置签名密钥(仅令牌鉴权)
const PUSH_URL = 'https://chat.onlinemusic.top/api/robots/{令牌}/reply' // 管理面板复制

await fetch(PUSH_URL, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    sessionType: 'private',
    sessionId: 10001,            // 接收方账号 ID(创建者)
    content: '⚠️ 监控告警:服务器 CPU 已超过 90%'
  })
})
```

```js
// 配置了签名密钥(强制验签)
const crypto = require('node:crypto')
const PUSH_URL = 'https://chat.onlinemusic.top/api/robots/{令牌}/reply'
const SECRET = '创建机器人时填写的签名密钥'

const body = JSON.stringify({
  sessionType: 'group',
  sessionId: 3,                  // 群 ID(机器人需已加入该群)
  content: '📢 公告:今晚 22:00 系统维护'
})
const signature = crypto.createHmac('sha256', SECRET).update(body).digest('hex')

await fetch(PUSH_URL, {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'X-Bot-Signature': signature
  },
  body
})
```

#### 实例:curl

```bash
# 未配置签名密钥
curl -X POST 'https://chat.onlinemusic.top/api/robots/{令牌}/reply' \
  -H 'Content-Type: application/json' \
  -d '{"sessionType":"private","sessionId":10001,"content":"你好"}'

# 配置了签名密钥
BODY='{"sessionType":"private","sessionId":10001,"content":"你好"}'
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac '你的签名密钥' | awk '{print $2}')
curl -X POST 'https://chat.onlinemusic.top/api/robots/{令牌}/reply' \
  -H 'Content-Type: application/json' \
  -H "X-Bot-Signature: $SIG" \
  -d "$BODY"
```

#### 实例:Python

```python
import hmac, hashlib, json, requests

url = 'https://chat.onlinemusic.top/api/robots/{令牌}/reply'
body = json.dumps({'sessionType': 'private', 'sessionId': 10001, 'content': '你好'}).encode()

# 配置了签名密钥时
sig = hmac.new(b'你的签名密钥', body, hashlib.sha256).hexdigest()
r = requests.post(url, data=body, headers={'Content-Type': 'application/json', 'X-Bot-Signature': sig})
# 未配置密钥时去掉 X-Bot-Signature 即可
print(r.json())  # {'success': True, 'message': '已发送'}
```

### 2. 接收消息回调(Webhook 模式,可选)

配置了 Webhook 地址的机器人,在收到消息时系统会 POST 事件到你的地址(**Webhook 可留空**——只用主动推送就不需要):

```json
{
  "event": "message",
  "robot": { "userId": 10112, "name": "小助手", "avatar": null, "isBot": true },
  "session": { "type": "private", "id": 10111, "name": "小明" },
  "from": { "userId": 10111, "name": "小明", "avatar": null, "isBot": false },
  "message": { "messageId": "uuid", "content": "在吗", "messageType": 0, "timestamp": 1787065713894 },
  "mentions": [],
  "replyTo": null
}
```

- 请求头携带 `X-Bot-Signature: HMAC-SHA256(签名密钥, rawBody)`(配置了密钥时),用于你校验事件真实性
- **同步回复**:返回 `200 {"content":"回复文本"}`,系统自动以机器人身份回复(10s 超时,失败重试 1 次,自动带回复引用);不返回 `content` 则不回复
- 触发规则:私聊对方是机器人即触发;群聊仅被 `@` 时触发;机器人之间互不触发

### 3. 快速验证

管理面板「我的机器人」→ 该机器人「测试」按钮:模拟一条私聊消息,展示机器人同步回复结果(未配置 Webhook 时提示仅支持主动推送)。

### 4. 官方示例插件:DeepSeek Harness 客户端推送插件(plugins/dsh-bot-notify)

装在 **DeepSeek Harness 客户端里**的插件:监听 Harness 会话事件,把任务的**执行过程与结果**通过机器人主动推送链接(`/api/robots/{令牌}/reply`)通知用户——`🧠 任务开始(含任务内容)` → `🔧 工具调用(可选)` → `✅ 执行结果(turn/step+回复)` → `⚠️ 异常结束`。详见 [plugins/dsh-bot-notify/README.md](plugins/dsh-bot-notify/README.md),安装 3 步:

```bash
dsh plugin --profile web add file:<本仓库>/plugins/dsh-bot-notify
# 编辑 $DSH_HOME/profiles/web/cordis.patch.yml 填入 pushUrl/sessionId(模板已写入)
# 重启 dsh web 生效(日志出现 [dsh-bot-notify] 已启用)
```

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
| `typing` | 双向 | 正在输入;转发给对方全部在线设备 |
| `read_receipt` | 双向 | 已读回执;标记已读并转发给被读方 |
| `message_recalled` | 双向 | 撤回;客户端 `content` 填待撤回的 messageId(仅本人、2 分钟内),服务端向相关方广播 |
| `online_status` | 服务端→客户端 | 好友上下线(`content`: `online`/`offline`) |
| `friend_request` | 服务端→客户端 | 收到新好友申请 |
| `friend_accepted` / `friend_rejected` | 服务端→客户端 | 申请被接受(双向)/ 被拒绝 |
| `group_invited` | 服务端→客户端 | 被邀请加入群组(`from` 为群 ID) |
| `group_dissolved` | 服务端→客户端 | 所在群被解散(`to` 为群 ID),客户端自动退出该会话 |
| `muted` | 服务端→客户端 | 群发言被拒(禁言中),`content` 含禁言截止时间说明 |
| `blocked` | 服务端→客户端 | 被对方拉黑,或私聊消息因对方拉黑而未送达 |
| `kicked` | 服务端→客户端 | 该登录会话被踢下线(设备管理踢出/改密/重置/封禁),随后连接关闭 |

**字段**:`from`(发送者ID)、`to`(接收者ID/群ID)、`content`、`messageId`(客户端生成则保留用于去重,否则用数据库 ID)、`messageType`(0文字/1图片/2文件)、`timestamp`(毫秒)、`senderName`、`senderAvatar`、`mentions`(群聊 @ 的成员 ID 列表)。

**补充机制**
- **消息去重**:历史/离线/群补发接口均返回与 WS 推送一致的 `messageId`(数据库 `ClientMessageId` 列),前端按此去重,不会出现重复消息
- **群离线补发**:`GroupMember.LastReadMessageId` 已读游标,上线推送游标之后的消息(每群 ≤100 条),打开群聊推进游标
- **会话列表**:`GET /api/messages/sessions` 聚合私聊 + 群聊(最后消息/时间/未读数;私聊名优先显示我的备注)

## 🗄️ 数据表

共 12 张表,启动时由 FreeSql CodeFirst 自动同步(映射见 [EntityConfiguration.cs](backend/LHZ.OnlineChat.Infrastructure/Persistence/EntityConfiguration.cs))。

| 表 | 对应聚合 | 说明 |
|---|---|---|
| `User_` | `User` | 用户(Id=账号 ID,起始 10000;Email 唯一;IsBot 机器人;IsBanned/BanReason 封禁) |
| `Friend` | `Friendship` | 好友关系(Status: 0待确认/1已接受/2已屏蔽,一条记录表达双向) |
| `FriendTag` | `FriendSetting` | 好友设置(设置者视角的备注 Remark / 分类 Category) |
| `Blacklist` | `BlacklistEntry` | 黑名单(拉黑者 → 被拉黑者) |
| `Group_` | `Group` | 群组(OwnerId;公告三列 Announcement/At/By) |
| `GroupMember` | `GroupMember` | 群成员(Role: 0群主/1管理员/2成员;LastReadMessageId 已读游标;MutedUntil 禁言) |
| `PrivateMessage` | `PrivateMessage` | 私聊消息(ClientMessageId 去重键;IsRead;IsDeleted 撤回;引用三列) |
| `GroupMessage` | `GroupMessage` | 群聊消息(Mentions 逗号分隔的 @ 列表;IsDeleted;引用三列) |
| `SessionSetting` | `SessionSetting` | 会话设置(用户 × 会话维度的置顶 IsPinned / 免打扰 Muted) |
| `RobotProfile` | `Robot` | 机器人配置(账号=`User_` 中 IsBot=true 的行;WebhookUrl/Secret/超时/加密令牌/推送统计) |
| `Admin` | `Admin` | 管理员(独立于用户体系;Role: 0超管/1运营;Status: 0停用/1启用) |
| `AdminLog` | `AdminAuditLog` | 管理操作审计(Action/TargetType/TargetId/Detail/Ip) |

**索引**:`PrivateMessage.Content` 与 `GroupMessage.Content` 上建有 pg_trgm(trigram)GIN 索引,使 `LIKE '%关键词%'`(含中文)走索引,大数据量下搜索不退化。

## 📜 License

MIT
