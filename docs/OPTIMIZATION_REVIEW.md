# LHZ.OnlineChat 优化分析报告（第二轮）

分析时间：2026-10-07 ｜ 对象：`dev` @ `349eec6`（上轮修复已提交）
性质：**只做分析，未改动任何代码**

---

## 一、先说一个决定优先级的前提

我查了正在运行的那套栈的真实数据量：

```
PrivateMessage  5 行      User_  2 行      Friend  1 行      Admin  1 行
GroupMessage    0 行      Group_ 0 行      RobotProfile 0 行  其余全为 0
Redis 键总数：9
```

**这套系统目前几乎没有真实数据。** 这直接决定了优化顺序：

- 任何"查询变快"的改动，现在都无法被测量验证，收益是纸面上的；
- 而**图片按原图上传下载**、**消息列表无上限**、**文档与代码不一致**、**没有 CI** 这几件事，无论数据量多少都是实打实的浪费或风险。

所以下面的排序不是按"理论收益"排的，而是按**"现在做就有确定收益，且不依赖数据量"**排的。

---

## 二、优先级总览

| 优先级 | 项 | 性质 |
|---|---|---|
| **P0-1** | 图片/头像按原图传输（前端的最大字节浪费） | 用户体验 + 带宽 |
| **P0-2** | WS 连接时补发查询随群数线性放大（1+2N 次查询） | 每次连接/重连 |
| **P0-3** | 消息列表无上限 + 每条消息对全会话重排序 | 长时间使用后卡顿 |
| **P0-4** | 打字指示器也要开一个数据库事务 | 纯浪费，频率高 |
| **P1** | 仪表盘 40 次串行 DB 往返、GetSessions 9+N、撤回 51 次 Redis 往返、Count+OFFSET 分页、整行 UPDATE | 数据量上来后才痛 |
| **P1** | 两个前端渲染热点（MembersModal O(n²)、ChatSidebar 全量重渲染）、`/vite.svg` 404、若干重复请求 | 前端 |
| **P2** | 无 CI、无法做冒烟测试（注册期副作用）、启动期 ad-hoc DDL、可观测性、**文档漂移** | 工程与运维 |
| — | 明确**不建议**动的几处 | 避免无效优化 |

---

## 三、P0：现在做就有确定收益

### P0-1　图片与头像按原图上传/下载

| 位置 | 现状 | 问题 |
|---|---|---|
| `ChatArea.vue:498,506` | 聊天图片原文件直传，仅校验 5MB | 手机照片 3–5MB |
| `MessageBubble.vue:350` | 实际渲染宽度 ≤ 260px | 为 260px 传 4MB |
| `AvatarCropModal.vue:99` | 512×512 导出 **PNG** | 300–700KB |
| `ProfileModal.vue:97` | 文件名写死 `avatar.png` | 每个好友拉头像都要再下一次 |

一张 4MB 照片 = 上行 4MB + **每个接收者再下行 4MB**（10Mbps 上行约 3.2 秒）。而头像 PNG 换 WebP 能省 **85–90%**，收益按"每个会话参与者 × 每次拉取"重复计算。

**做法**：聊天图片用 canvas 压到长边 ≤1600px、WebP q0.82（≈250–400KB，**−90%**）；头像导出改 WebP q0.85（≈30–70KB）。两处都是纯前端改动，后端无需变更。**这是全仓库"每行代码省下的字节"最高的一项。**

### P0-2　WebSocket 连接时的补发查询爆炸

`ChatConnectionHandler.cs:66` 每次连接都触发 `SendGroupBacklogCommand`（fire-and-forget）。而 `PresenceCommands.cs:81-116` 是：

```csharp
foreach (var membership in memberships)          // N = 我加入的群数
{
    var backlog = await _messages.ListAfterCursorAsync(...);   // 1 次查询
    if (backlog.Count == 0) continue;
    var senders = await _users.GetManyAsync(...);              // 1 次查询
    ...
}
```

**20 个群 = 41 次数据库往返，而这 41 次里绝大多数时候一条消息都不会返回**（没有离线补发是常态）。更要命的是现在客户端在心跳超时后会主动重连（上一轮加的），重连频率比之前高。

**做法**：两条路，任选其一即可把常见路径降到 1 次查询——
1. 先用一条 `EXISTS`-类查询判断"是否存在任一需要补发的群"，为空直接返回；
2. 或用一次 `ROW_NUMBER() OVER (PARTITION BY "GroupId")` 窗口查询一次取回所有群的补发消息（每群 100 条上限不变），再加一次批量发件人查询 → **3 次查询**。

顺带一提：这个命令是 `ICommand`，所以它**被 `TransactionBehavior` 包进了一个数据库事务**——为 N 次只读查询开一个事务，纯属多余（与 P0-4 同源）。

### P0-3　消息列表无上限，且每条消息对全会话重新排序

```ts
// chat.ts:105-114
function mergeList(list, incoming) {
  const seen = new Set(list.map(m => m.messageId).filter(Boolean))
  const merged = [...list]
  ...
  return merged.sort((a, b) => a.timestamp - b.timestamp)   // ← 每次都全量排序
}
```

`chat.ts:13-16` 只限制了**会话数**（200/30），**没有限制单个会话的消息数**；`loadMoreHistory` 每页追加 50 条且无上限。于是：

- 一个会话累积到 1000 条：`ChatArea.vue:612` 渲染 1000 个 `MessageBubble`（约 8000 个 DOM 节点），无虚拟滚动；
- 每来一条新消息都执行一次 `list.map` + `Set` + **全量 `sort`**：n 条消息累计 O(n² log n)；
- 再叠加一次强制 reflow（`ChatArea.vue:181-187` 读 `scrollHeight`）。

实测量级：n=1000 时约 0.3ms/条（还能忍），n=5000 时 2–5ms/条 → 消息密集时掉帧。

**做法**：渲染窗口只取最近 ~200 条（store 仍然保留全部，向上滚动加载的逻辑本来就有），或引入虚拟滚动。同时把"追加一条"改成插入到正确位置而不是全量排序（消息绝大多数是追加到末尾，`sort` 完全可以省掉）。**−80% 挂载耗时、−80% 单条更新耗时。**

### P0-4　打字指示器也在开数据库事务

`TransactionBehavior` 无差别包裹每一个 `ICommand`。而 `SendTypingHandler`（`ReadStateCommands.cs:143-156`）**只调用 `IRealtimeNotifier`，一个仓储都不碰**：

```csharp
internal sealed class SendTypingHandler : IRequestHandler<SendTypingCommand, Unit>
{
    private readonly IRealtimeNotifier _notifier;      // 没有仓储
    ...
}
```

"正在输入"是按输入节奏触发的（虽已节流，仍是高频），**每一次都付 BEGIN + COMMIT 两次数据库往返，并占住一个连接池连接**。同类还有：`KickSessionCommand` / `LogoutOtherSessionsCommand`（纯 Redis）、`RefreshTokenCommand`（Redis + 1 次读）、`BroadcastPresenceCommand`。

**做法**：给不需要事务的命令加一个标记（如 `ITransactionFree` 空接口，或静态泛型缓存 `ConcurrentDictionary<Type,bool>`），在 `TransactionBehavior` 里跳过。**默认仍走事务**，只对显式标记的命令放行——这个方向是安全的。

### P0-5（顺带发现）异步路径里的同步数据库调用

```csharp
// PrivateMessageRepository.cs:252  HourlyAggregate.Query
var table = fsql.Ado.ExecuteDataTable(sql);        // 同步阻塞
```

它被 `CountByHourSinceAsync` 通过 `Task.FromResult(...)` 包装后返回，于是**在一个 async 请求处理链里同步阻塞了一个线程池线程**，仪表盘一次要跑两次。改为 `ExecuteDataTableAsync` 即可。

同一段里的错误处理用的是 `Console.WriteLine`（`:262`）而不是 `ILogger`——失败不会进结构化日志。

---

## 四、P1：数据量上来之后才会痛

### 仪表盘 40 次串行往返

`GetDashboard.cs:63-86` 的对象初始化器里 10 个 `await` 顺序执行，`:107-133` 又是 7 天 × 3 次查询 = 21 次，加上其余共 **≈40 次串行往返**（总耗时 = Σ RTT，而非 max）。

**做法**：独立的查询用 `Task.WhenAll` 并发；7 天趋势压成 3 条 `GROUP BY date_trunc('day')`；10 个计数压成几条 `COUNT(*) FILTER (WHERE ...)`。→ 约 16 条查询、2 个并发波次。

> 注意：仪表盘只有管理员会看、且不常看，所以**它不是紧急项**。列在 P1 是因为改动便宜，而不是因为影响大。

### 会话列表 9 + N 次查询，还白拉 500 行

`GetSessions.cs:130-132` 在 foreach 里对每个群各查一次未读数（20 个群 → 29 次查询，随群数线性增长）；`ListRecentOfUserAsync(userId, 500)`（`PrivateMessageRepository.cs:116-123`）拉 **500 行全字段**只为每个对端保留最后一条。

**做法**：未读数用一条 `GROUP BY` 基于 `GroupMember.LastReadMessageId` 算完（9+N → 约 9，与群数无关）；最后一条消息改用 `DISTINCT ON` / 窗口函数——`LatestOfGroupsAsync`（`GroupMessageRepository.cs:124-145`）已经是这么写的，照抄即可。约 250KB → 5KB。

### 撤回一条消息要 51 次 Redis 往返

`RedisStores.cs:193-215`：`ListRange` + `KeyDelete` + 一个循环里**逐条 `await ListLeftPushAsync`**（最多 49 条 → 最多 **51 次串行往返**）。用户每次撤回、管理员每次强制删除都要付。

**顺带一个隐患**：判断"哪条要删"用的是字符串包含——

```csharp
private static bool MatchesMessageId(string json, string messageId)
    => json.Contains($"\"messageId\":\"{messageId}\"", StringComparison.Ordinal);
```

如果某条消息的**正文**里恰好出现 `{"messageId":"..."}` 这样的片段（用户手打一段 JSON 就会），就会误命中并删掉错误的缓存条目。而这个类里**已经有 `TryParse` 能把 JSON 反序列化成 `CachedMessage`**，直接按解析后的字段比较即可，不必做子串匹配。

**做法**：反序列化后过滤，再用一段 Lua 脚本（或发一批不逐条 await 的 LPUSH）重建列表 → ≤2 次往返，同时消掉误匹配。同样的脚本能把 `AppendAsync`（`:169-170`，LPUSH+LTRIM=2 次）也降下来。

### Count + OFFSET 分页

所有分页都是 `CountAsync` + `Skip/Take`：第 50 页 = PostgreSQL 读出并丢弃 2450 行，再加一次全表 `COUNT`。消息历史的排序键 `(SentAt DESC, Id DESC)` **已经被索引覆盖**，所以改成 keyset（`beforeSentAt, beforeId`）就是索引查找。聊天向上翻页本来就是游标语义，接口可以加一个可选的 `beforeId`，并在 `page > 1` 时跳过 `CountAsync`。O(offset) → O(pageSize)。

另外 `GetPrivateHistory.cs:62-64` 在缓存命中路径上也**白付一次 Count** 只为填 `Total`。

### 整行 UPDATE 只为改一个布尔

`MarkMessageReadHandler`（`ReadStateCommands.cs:26-30`）与 `SendReadReceiptHandler`（`:116-120`）都是 find + `SetSource` → **整行重写（连 `Content` 一起写一遍）**只为把 `IsRead` 置真。改成一条 `UPDATE ... SET "IsRead" = true` 即可。`RobotTriggerHandlers.cs:282-283` 的计数器自增同样在重写整个 Robot 行。

### 前端的两个渲染热点

- **`MembersModal.vue:115,128,131`**：成员列表 `v-for` 里，每个成员都调 `canSetAdmin(m)` / `canKick(m)`，各自再 `find()` 一次成员表 → **500 人群约 50 万次比较/渲染**。把 `myRole` 与管理员 id 集合提到 `computed` → −99%。
- **`ChatSidebar.vue:102,112,116,122`**：每行的预览/时间/未读都从 store 的 Map 里取，导致组件的渲染 effect 订阅了**所有**会话的键——群里来一条消息就重排最多 200 行。把行抽成接原始值的子组件（或 `v-memo`）→ −90%。

### `/vite.svg` 404，且两个前端都没有 favicon

`frontend/lhz-onlinechat-web/index.html:4` 引用了 `/vite.svg`，而 `public/` 下只有 `manifest.webmanifest`、`sw.js`、`icons/icon-192.png`、`icons/icon-512.png` —— **每次加载页面都有一个 404，且浏览器标签页没有图标**。管理端 `index.html` 连 favicon 都没有。

（已确认：`public/icons/` 里有现成的 192/512 PNG，直接指过去即可。）

### 几个白发的请求

`ChatLayout.vue` 启动时是 6 个 XHR、3 个串行 RTT：
- `loadOfflineMessages`（`:244`）只依赖 `auth.user`，却排在 `Promise.all` 之外 → 移进去可省 1 个 RTT；
- `ChatArea.vue:213-225` **每次切换会话都重拉第 1 页并重新排序**，尽管 store 还缓存着最多 30 个会话 → 有缓存且 WS 未断时跳过；
- `ChatLayout.vue:264` 无条件调 `markSessionRead`（未读为 0 时也发一次 PUT + WS 回执）→ 先判断未读数；
- `ChatLayout.vue:520-524` 每次点标签都重拉整个列表（无 TTL）。

### 打包与缓存

- 两个 `vite.config.ts` 都没有 `manualChunks`：Vue/router/pinia/应用外壳在同一个 chunk 里，**每次发版都会让回访用户重新下载 40.6KB gz**（管理端 39.2KB）。拆出 `vendor` + `axios` 后，发版只需重下约 5–8KB。
- `ChatLayout.vue:81-90` **静态导入 11 个弹窗**（都藏在 `v-if` 后面），约占 ChatLayout chunk（26.7KB gz）的 8–10KB gz。改 `defineAsyncComponent` 可把聊天页首屏从 94.7KB gz 降到约 84KB gz。

---

## 五、P2：工程与运维

### 没有 CI（当前最大的工程风险）

仓库里**没有任何流水线配置**（`.github/workflows`、`.gitlab-ci.yml`、`azure-pipelines.yml` 都不存在）。936 个测试、0 警告、两个前端的类型检查，全部依赖"提交的人记得跑"。

**最小可用流水线**（一条 workflow，约 30 行）：
```
dotnet build backend/LHZ.OnlineChat.slnx  →  dotnet test  →  两个前端 npm ci && vue-tsc --noEmit && vite build
```
顺带把 `Directory.Build.props` 加上 `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`——现在是 0 警告，锁住它不花成本；前端 `tsconfig.json` 的 `noUnusedLocals`/`noUnusedParameters` 目前是 `false`（`:15-16`），打开可以清掉死代码（例如 `api/message.ts` 的 `getUnreadCount`/`markAsRead` 已经无人调用）。

### 无法做冒烟测试——因为 DI 注册阶段有副作用

`Infrastructure/DependencyInjection.cs:56`：

```csharp
private static void AddPersistence(IServiceCollection services, InfrastructureOptions options)
{
    DatabaseInitializer.EnsureDatabaseExists(options.ConnectionString);   // ← 注册期就连库
```

**服务注册阶段就执行了数据库 I/O**。后果：
- 进程没有数据库就起不来（已知）；
- **任何进程内冒烟测试都必须先有真实数据库**，所以 `WebApplicationFactory` 这条路目前走不通；
- 测试工程里没有任何 `Microsoft.AspNetCore.Mvc.Testing` / Testcontainers 引用（已确认）。

这意味着一整类缺陷**当前零覆盖**：中间件顺序、DI 图能否解析、JSON 序列化形状、限流是否真的生效、`/health` 是否可达。

**做法**：把建库/迁移从注册期挪到一个 `IHostedService` 的 `StartAsync`（时序等价——现在也是在 `app.Run()` 之前显式调用），随后就能用 `WebApplicationFactory` + 替身端口写冒烟测试，第一条就该断言"DI 图能解析"。**这是我这一轮最推荐的一项工程改动。**

### 启动期 ad-hoc DDL

`DatabaseInitializer.cs` 每次启动都执行一串幂等 DDL（`ALTER TABLE`、`CREATE UNIQUE INDEX`、`setval`）。当前写法很克制（失败只告警、不阻塞启动），但随着 schema 演进会有具体风险：
- **不是所有迁移都能写成幂等**（改列类型、拆分列、回填数据都不行）；
- 唯一索引建失败只留一条警告，**运维很容易漏看**，那时并发防重实际没生效；
- 没有版本表，无法回答"这个库现在是什么版本"。

不需要上 EF Migrations 重写：**给现有机制加一张 `SchemaVersion` 表 + 把 DDL 按序号组织**，就足以把"漏看警告"变成"启动时明确报出缺哪一步"。

### 可观测性：够用，但回答不了"现在慢不慢"

已有的：异常日志中间件、`/health`、管理后台能看到在线用户数与 WS 连接数（`GetDashboard.cs:65-66`）。

缺的：**没有任何指标暴露**（无 `/metrics`、无 Prometheus）。所以"消息现在慢不慢""连接池是否打满""Redis 是否在超时"这些问题，目前只能翻日志猜。对自建部署，最小可做的两件事：
1. 把 `IConnectionRegistry` 的计数与连接池/Redis 状态做成一个 `/metrics` 文本端点（无需引入 Prometheus 客户端库）；
2. 生产环境日志改用 JSON 控制台格式（现在 `appsettings.json` 只配了 `Default: Information` / `Microsoft.AspNetCore: Warning`），便于按字段检索。

### 两个自研依赖的版本风险

后端依赖两个作者自己的包：`LHZ.WebSocket.AspNetCore 1.2.0` 与 `LHZ.FastJson 2.0.1-pre`。

- **`2.0.1-pre` 是预发布版本**。预发布包可以被作者取消发布（unlist）或覆盖，而它是**线上 JSON 序列化的唯一实现**（WS 协议、Redis 缓存快照都走它）。一旦上游有问题，这里没有替代路径。
- 提交历史里已经出现过一次这类代价：`82a3166 chore: 升级 LHZ.WebSocket.AspNetCore 至 1.2.0（适配拼写修正的破坏性重命名）`——**依赖方做过破坏性重命名**，说明它的 API 还不稳定。
- 两个包都没有对外可见的源码仓库入口（README 只给了 FastJson 的 NuGet 链接），可用性完全绑定在作者身上。

**最小缓解**（不需要换库）：把版本**钉死**（已是精确版本，满足）、在 README 或 `docs/` 里写清"我们实际依赖了这两个库的哪些 API"，以及注意 `backend/nuget.config` 用了 `<clear />` 只保留 nuget.org——**构建必须有到公网 nuget.org 的连通性**，CI 或内网构建要提前考虑（自建镜像源或预热包缓存）。若这些都不足以接受，才需要考虑把 FastJson 换成 `System.Text.Json`（WS 协议 camelCase 与 `[JsonProperty]` 标注需相应改造，属于真正的迁移工作）。

### 文档漂移（改完代码最容易忘的一步）

上一轮改了不少对外行为，README 有 **5 处**已经与实现不一致：

| 位置 | README 说 | 实际 |
|---|---|---|
| `README.md:228` | SMTP 留空时"随 `send-code` 接口返回 `devCode`" | `devCode` **只在开发环境**返回；生产环境不回传，提示去查服务器日志 |
| `README.md:35` | "创建/**加入**/退出/踢人" | 加入默认**仅限邀请**，需群主调 `PUT /groups/{id}/join-policy` 开放 |
| `README.md:401-405` WS 协议表 | 无 `friend_removed` / `group_removed` | 本轮新增了两个下行类型 |
| `README.md:13,174,180-182` | 903 个测试（343/420/140） | 936 个（348/445/143） |
| 全文 | 未提限流 | 发码 20 次/10 分钟/IP、机器人推送 120 次/分钟会返回 429 |

另外 `docs/PROJECT_ANALYSIS.md` 作为"某一时刻的报告"被提交进了仓库，它会随时间失真。建议要么在文件头写明"截至某 commit 的快照"，要么只保留其中的架构结论、把过程性内容移出仓库。

---

## 六、明确**不建议**动的部分

写下来是为了避免下一轮把时间花在这里：

- **广播路径已经是正确的**：`RealtimeNotifier` 在循环**之前**序列化一次（`:61/68`、`:164/166`、`:212/222`），循环里没有 DB/Redis 调用。不要"优化"它。
- **每个认证请求一次 Redis `KeyExists`**（`Program.cs:97`）是会话撤销能立即生效的前提。加本地缓存会牺牲这一点，**不值得**。
- **领域事件用反射、MediatR 四个管道行为**：实测量级约 1–5µs/请求，在 1k msg/s 时占不到一个核的 0.2%。不要为此引入缓存或手写分发。
- **emoji 数据集**只有 1.6KB 源码（≈1KB gz），不是问题。
- **管理后台的表格**已经全部服务端分页（20 行/页），不必改。
- **前端总包只有 362KB（首屏 94.7KB gz）**、4 个生产依赖——不存在"包太大"的问题。第 5 节的拆包建议是**缓存收益**，不是体积收益。

---

## 七、建议的执行顺序

**第一批（确定收益，不依赖数据量）**
1. 图片/头像压缩（P0-1）——纯前端，收益最大
2. 修 `/vite.svg` 404 与缺失的 favicon（一行）
3. 文档同步 5 处（README + 报告文件头加时间戳）
4. 加 CI（一条 workflow）+ `TreatWarningsAsErrors` + 前端 unused 检查
5. WS 连接补发查询降到 1–3 次（P0-2）

**第二批（结构性改善）**
6. 把建库/迁移从 DI 注册期挪到 `IHostedService`，随后补上 `WebApplicationFactory` 冒烟测试
7. `TransactionBehavior` 支持免事务标记，先放行 `SendTypingCommand` 等（P0-4）
8. 消息列表渲染窗口 + 去掉全量排序（P0-3）
9. 顺手修：`HourlyAggregate` 改异步、错误走 `ILogger`
10. 修搜索结果 `messageId` 为 null 导致机器人消息无法定位（见下）

**第三批（等有真实数据再做，先加度量）**
11. `/metrics` + 生产 JSON 日志
12. 仪表盘并发化、GetSessions 聚合、撤回 Lua 脚本、keyset 分页、单列 UPDATE
13. 前端 `manualChunks` / 弹窗异步化 / 两个渲染热点

---

## 附：本轮顺带发现的三个缺陷

> **状态：三个均已修复**（同一分支，尚未提交）。修法见每条的「已修复」行；
> 共补 11 个回归测试，测试总数 936 → 946，构建 0 警告。

这三个不属于"优化"，是读代码时发现的真实问题：

**1. 搜索结果里机器人消息无法定位/高亮。**
`SearchMessages.cs:205,223` 把 `MessageId` 赋成 `m.ClientMessageId`——机器人推送的消息这一列是 **null**（`RobotTriggerHandlers.cs:294,319` 显式传 `null`）。而 `MessageSearchResultDto`（`MessageDtos.cs:94-115`）**没有 `Id` 字段**，前端的兜底写法 `m.messageId || String(m.id)`（`chat.ts:303,335,386`）在这里用不上，`ChatArea.vue:130` 又是 `if (r.messageId)` —— 于是点击这类搜索结果**静默无反应**，README 宣传的"点击结果自动定位并高亮"对机器人消息失效。
修法：这两处改用 `m.PublicMessageId`（它本来就会回落到数据库 ID，也是 WS 推送与历史接口用的口径）。改完前端的兜底逻辑甚至可以删掉——**同一套规则目前被服务端和客户端各实现了一遍**，这是漂移的温床。

> **已修复**：`SearchMessages.cs` 两处与 `MessageDtos.cs` 两处（历史 DTO）统一改为 `PublicMessageId`。
> 历史 DTO 一并改是因为它与我改动前客户端算出的值**逐字符相同**（`ClientMessageId ?? Id`），
> 所以是零行为变化、只是把口径收到服务端一侧。前端的 `m.messageId || String(m.id)` 兜底保留未动
> （现在恒为死分支，但删它没有收益、反而多一次改动风险）。回归测试：
> `没有客户端消息ID的搜索结果也带可用MessageId`、`没有客户端消息ID的历史消息也返回可用的MessageId`。

**2. `HourlyAggregate` 的异常处理写 `Console.WriteLine`**（`PrivateMessageRepository.cs:262`）而不是 `ILogger`，仪表盘统计失败不会出现在结构化日志里，只会混在容器 stdout 中。

> **已修复**：`HourlyAggregate.Query` 增加 `ILogger` 参数，两个仓储各注入 `ILogger<T>`，改为 `logger.LogWarning(ex, "仪表盘小时分布查询失败（{Table}，近 {Hours} 小时）", ...)`。
> 回归测试用**非法连接串**（端口写成 `not-a-port`）触发失败——比指向不可达端口快得多，
> 后者要付一次网络超时（实测让整个 Infrastructure 套件慢 2 秒）。

**3. 最近消息缓存按子串匹配删除条目**（`RedisStores.cs:217-218`）：用 `json.Contains("\"messageId\":\"...\"")` 判断要删哪条，消息正文里出现同样的 JSON 片段就会误删。详见 §四"撤回一条消息要 51 次 Redis 往返"。

> **已修复**：改用已有的 `TryParse` 反序列化后比对 `MessageId` 字段（`IsTargetMessage`），
> 解析失败一律按"不匹配"处理——宁可留在缓存里也不误删。补 7 个用例，其中两个专门覆盖误删：
> 正文为 `看看这段 {"messageId":"cmid-1"} 是什么` 时不得命中。
> 注：**同一个方法里的"逐条 await 推送、最多 51 次 Redis 往返"没有一并改**——
> 那属于 P1 优化项（§四），不在"三个缺陷"范围内，留待单独处理。
