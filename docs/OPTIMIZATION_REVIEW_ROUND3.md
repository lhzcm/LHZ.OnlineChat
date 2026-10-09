# LHZ.OnlineChat 优化分析报告（第三轮）

分析时间：2026-10-09 ｜ 基线：`dev` @ `581e46d`（工作树干净）
性质：分析和修复，**报告主体是只读分析**

> **修复状态（同日晚追加）**：本文 §一 的 5 个 P0 已全部修复，见文末「§六 修复记录」——
> 含改动位置、验证方式，以及**尚未验证**的部分（本机沙箱内无法还原 NuGet 包，
> Application / Infrastructure / Server 三层编译不了）。
> §二 / §三 的 P1、P2 项未动。

**验证方式说明**：本报告的每条结论都来自阅读当前代码，均给 `文件:行号`。
未能执行 `dotnet build` / `dotnet test`：本机沙箱内 dotnet CLI 在 workload 校验阶段即静默退出
（`dotnet restore` 无任何输出、exit 1），与代码无关。因此**测试数量、构建是否 0 警告这两项沿用仓库 CI 与文档的口径，未在本轮复测**。

---

## 零、结论速览

上一轮报告（`docs/OPTIMIZATION_REVIEW.md` → `349eec6`）的「第一批」5 项已经落地：

| 上一轮第一批 | 状态 | 证据 |
|---|---|---|
| 图片/头像压缩上传 | ✅ | `utils/image.ts:10-16`（长边 1600 / q0.82）、`AvatarCropModal.vue:102`（WebP）、`ProfileModal.vue:100`（不再写死 avatar.png） |
| `/vite.svg` 404 与 favicon | ✅ | 两个 `index.html:7` 指向 `/icons/icon-192.png`，全仓无 `vite.svg` 引用 |
| 文档同步 5 处 | ✅ | 测试数（348/447/151=946）、限流参数、Swagger 环境与代码逐项吻合 |
| CI + `TreatWarningsAsErrors` + 前端 unused 检查 | ✅ | `.github/workflows/ci.yml`、`Directory.Build.props`、两个 `tsconfig.json` |
| WS 连接补发查询收敛 | ✅ | `PresenceCommands.cs:85-99` + `GroupMessageRepository.cs:124-146`，常见路径 1 次查询 |

**但「第二批、第三批」基本未动**（免事务标记、消息渲染窗口、仪表盘并发、keyset 分页、聚合未读、冒烟测试、指标端点）。
本轮另外查出 **5 个 P0**，其中 2 个是「照着 `.env.example` + 部署文档操作就会中招」的占位密钥问题。

### 本轮 Top 8（按「影响 ÷ 改动成本」排序）

| # | 问题 | 一句话影响 | 档 |
|---|---|---|---|
| 1 | `Robot__TokenKey` 占位值不在生产自检内 | 可算出密钥、伪造任意机器人令牌，以其身份发消息 | **P0** |
| 2 | `ADMIN_INITIAL_PASSWORD` 占位值不在生产自检内 | 后台超管 `admin/change-me-admin-password` 直接可用 | **P0** |
| 3 | 私聊没有好友校验 | 任意登录用户可向任意账号发消息并触发对方桌面通知 | **P0** |
| 4 | Webhook 地址不限内网（SSRF） | 服务端被诱导 POST 内网/云元数据地址，`/test` 还能回显部分响应 | **P0** |
| 5 | 前端 WS 订阅与重连生命周期 | 登出后旧连接仍带旧 token 重连、回调不注销 → 换账号后串号 | **P0** |
| 6 | 每个 `ICommand` 都开数据库事务 | 打字指示器等纯推送命令也付 BEGIN/COMMIT 并占用连接 | P1 |
| 7 | 单会话消息无上限 + 每条消息全量排序 | 长会话 O(n² log n) 累积、整表重建 | P1 |
| 8 | 仪表盘 / 会话列表 / 撤回的多次往返 | ~41 次串行 RTT、9+N 次查询、最多 51 次 Redis 往返 | P1 |

---

## 一、P0：建议优先修（都在小改动范围内）

### P0-1　`Robot__TokenKey` 的占位值不会被自检拦住，机器人令牌可被伪造

**证据链**

- `.env.example:33` 的默认值就是公开字符串：`ROBOT_TOKEN_KEY=change-me-to-a-random-robot-token-key`；
- `docker-compose.yml:66` 只做**非空**校验（`${ROBOT_TOKEN_KEY:?...}`），占位值照样通过；
- `AppSettings.cs:104-108` 只判「非空」，**没有**像 JWT 那样比对仓库默认值集合（`AppSettings.cs:69-74, 96-99`）；
- 令牌实现是确定性派生：`SecurityServices.cs:131` 用 `SHA256(TokenKey)` 当 AES-GCM 密钥，`:135-151` 的令牌布局（nonce12+cipher8+tag16）完全公开；
- 消费端只认令牌：`RobotWebhookCommands.cs:62-105` 解出 `robotId` → 直接以机器人身份发消息（好友/群校验只限制**目标**，不限制调用者）。

**影响**：部署者若沿用示例值，任何读过本仓库的人都能算出密钥、为**任意 `robotId`** 生成合法令牌，
再 `POST /api/robots/{token}/reply` 以该机器人身份给它的好友与所在群发消息。
又因为 `Program.cs:142-151` 是**按令牌字符串**分区限流，攻击者每换一个伪造令牌就是一个新分区，120 次/分钟的限制形同虚设，且分区表不淘汰（持续占内存）。

**修法**：把 `Robot__TokenKey` 纳入 `KnownDevelopmentSecrets` 同款校验（Reject 占位值），
并在 `.env.example` / `docs/DEPLOY.md` 的必填清单里点名。

### P0-2　初始超级管理员口令的占位值同样不被自检拦住

**证据**：`.env.example:50-51`（`admin` / `change-me-admin-password`）→ `docker-compose.yml:68-69` 无 `:?` 强校验
→ `DatabaseInitializer.cs:334-344` 只要求长度 ≥ 6 → `AppSettings.cs:88-118` 的 `EnsureProductionReady` **不含** `Admin.InitialPassword`
→ `docs/DEPLOY.md:56-63, 270` 的必填项与上线清单也都没提它。

**影响**：按文档部署即得到最高权限超管（可重置任意用户密码、检索删除全部消息）。
`AppSettings.cs:83-86` 的注释解释了为什么这类问题要「启动即失败」，这里恰好漏了一项。

**修法**：`EnsureProductionReady` 增加 `Admin.InitialPassword` 校验（存在且不在默认值集合、长度与复杂度下限），
并同步 DEPLOY 必填表。（可顺带把 `POSTGRES_PASSWORD` / `REDIS_PASSWORD` 的占位值也纳入 compose 侧校验——
它们目前只绑 `127.0.0.1`，风险低于上面两项，属加固。）

### P0-3　私聊消息没有好友关系校验

**证据**

- `SendPrivateMessage.cs:97-108`：整条链路只做「接收者是否拉黑发送者」，**没有** `AreFriendsAsync`；
  对比机器人推送路径 `RobotWebhookCommands.cs:82-89` 明确做了防骚扰校验，`FriendRepositories.cs:22` 也已有现成方法可复用；
- 消息落库后 `RealtimeNotifier`（`SendPrivateMessage.cs:139`）推给**双方全部在线设备**（含桌面通知）；
- 会话列表按消息聚合（`GetSessions.cs:69-79`），所以陌生人会凭空出现在对方会话列表里；
- 而历史接口要求好友（`GetMessageHistory` 的好友校验），**对方点进去读不到**——对他来说这是「一条读不了的通知」。

**影响**：任意登录用户把 WS `private_message` 的 `to` 改成任意账号 ID，即可骚扰/钓鱼任意用户（附带你站点的桌面通知）。
`ReadStateCommands.cs:136-155` 的 typing / 已读回执转发同样不校验关系，可用来试探某账号是否在线。

**修法**：在 `SendPrivateMessageHandler` 注入 `IFriendshipRepository`，非好友且非机器人会话直接拒绝（走已有的 `SendMessageResult.Rejected` + WS 提示）。

### P0-4　Webhook 地址不限内网（SSRF）

**证据**：`Robot.cs:9-30` 的 `WebhookUrl` 只要求「http/https 开头的绝对地址」，
`DependencyInjection.cs:140-144` 的注释还明确写了「允许 http:// 内网地址」；服务端在
`WebhookDispatcher.cs:50-61` 主动 POST 该地址。重定向已关闭（`DependencyInjection.cs:143`）是好的，
但**没有**私网/环回/链路本地地址拦截，也没有 DNS rebinding 缓解；`WebhookDispatcher.cs:70` 读取响应体无大小上限。

**影响**：任意用户建一个机器人指向 `http://169.254.169.254/...`（云元数据）或 `http://postgres:5432/` 等内网服务，
用自己的好友关系触发回调即可探测内网；`RobotWebhookCommands.cs:170-177` 的 `/test` 会把响应 JSON 的 `content` 回显给创建者，形成**有限回读**。

**修法**（按成本递增，任一都比现状好）：
① 解析后拒绝私网/环回/链路本地/`fd00::/8` 等地址；② 仅在显式开关下允许内网地址（自托管用户需要时手动打开）；
③ 加响应体读取上限（如 64KB）。

### P0-5　前端 WS 订阅与重连的生命周期缺陷（登出后串号）

**证据**

- `ChatLayout.vue:217-223` 调 `ws.onMessage(...)` / `ws.onStatusChange(...)` 但**丢弃返回值**，组件也没有 `onUnmounted`；
  `websocket.ts:160-175` 的注释写明「返回取消订阅函数（组件卸载时调用）」，即约定存在但未被使用；
  `ChatLayout.vue:203` 的 `window.addEventListener` 同样不移除。
- `websocket.ts:65-70` 的 `onclose` 无条件 `scheduleReconnect(token)`；而 `disconnect()`（`:77-88`）只是 `ws.close()`，
  **关闭动作本身会触发 onclose**，于是「登出」路径（`ChatLayout.vue:501-505`）也会在 1 秒后**用旧 token 重连**。
- `auth.ts:72-84` 的登出只清本地存储，不调服务端吊销（AuthController 也没有 logout 端点），
  所以旧会话在 7 天有效期内仍能通过鉴权 → 旧连接能真正建起来。
- `websocket.ts:149-158` 重连 10 次后只是 `console.log` 并永久停连，UI 上 `ChatArea.vue:441` 仍显示「正在重连」，用户无从恢复。

**影响**：同一浏览器里「登出 → 用另一个账号登录」后：① 上一个账号的回调仍在数组中，
每条消息被处理 N 次（未读角标、提示音、桌面通知、已读回执各 ×N）；② 旧账号的 WS 可能以旧 token 活着，
把上一个账号的消息写进当前 store → **跨账号数据串号**（共享设备上尤其严重）。

**修法**（都很小）：`onUnmounted` 里调用保存下来的两个 unsubscribe 并 `removeEventListener`；
`disconnect()` 增加 `manualClose` 标志让 `onclose` 跳过重连；重连耗尽时通知 UI 提供「重新连接」入口；
登出时调一次服务端吊销（顺带补上 P2 第 12 条）。

---

## 二、P1：上一轮遗留的结构性优化（逐条复核现状）

### 2.1 上一轮报告条目的当前状态

| 上一轮条目 | 现状 | 当前证据 | 一句话做法 |
|---|---|---|---|
| WS 连接补发查询 | ✅ 已修复 | `PresenceCommands.cs:85-99`、`GroupMessageRepository.cs:124-146` | — |
| 撤回缓存的子串误删 | ✅ 已修复 | `RedisStores.cs:225-231`（改 `TryParse` 后按字段比对） | — |
| 仪表盘异常不入日志 | ✅ 已修复 | `PrivateMessageRepository.cs:266-272`（`ILogger.LogWarning`） | — |
| `HourlyAggregate` 同步阻塞 | ⚠️ 部分 | `PrivateMessageRepository.cs:259` 仍是 `ExecuteDataTable`，`:221` 用 `Task.FromResult` 包装；`GroupMessageRepository.cs:305` 同样 | 改 `ExecuteDataTableAsync`，去掉 `Task.FromResult` |
| `TransactionBehavior` 无差别开事务 | ❌ 仍存在 | `TransactionBehavior.cs:39-50` 对每个 `ICommand` 都 `ExecuteAsync`；全仓无免事务标记；`SendTypingCommand` 等只调 `IRealtimeNotifier` | 加 `ITransactionFree` 空接口或静态泛型缓存，在 `:39` 处放行 |
| 消息列表无上限 + 每条全量排序 | ❌ 仍存在 | `chat.ts:105-114`（每条新消息 `list.map`+`Set`+全量 `sort`，`:136` 再 `find` 一次）；`:13-16` 只有会话/元数据上限，`loadMoreHistory:363` 无上限；`ChatArea.vue:616` 全量 `v-for` 无虚拟滚动/`v-memo` | 渲染窗口取最近 ~200 条 + 追加改为插入（省掉 `sort`），有条件再上虚拟滚动 |
| 仪表盘 ~41 次串行往返 | ❌ 仍存在 | `GetDashboard.cs:67-76`（10 个 await 串行）、`:107-133`（7 天 × 3 次）；Application 层 grep 无任何 `Task.WhenAll` | 独立查询 `Task.WhenAll`；7 天趋势压成 3 条 `GROUP BY date_trunc('day')` |
| 会话列表 9+N 次查询 + 白拉 500 行 | ❌ 仍存在 | `GetSessions.cs:130-132` 每群一次 `CountAfterCursorAsync`；`:69-71` 的 `ListRecentOfUserAsync(userId, 500)` | 未读数一条 `GROUP BY GroupMember.LastReadMessageId` 算完；最后一条照抄 `LatestOfGroupsAsync` 的 `DISTINCT ON` |
| 撤回 51 次 Redis 往返 | ❌ 仍存在 | `RedisStores.cs:208-213`（`KeyDelete` + 逐条 `await ListLeftPushAsync`，最多 49 条）；`:169-170` 的 `AppendAsync` 仍 LPUSH+LTRIM 两次 | 过滤后用 Lua 脚本或一次 pipeline 重建；`AppendAsync` 合并为一段脚本 |
| Count + OFFSET 分页 | ❌ 仍存在 | `PrivateMessageRepository.cs:27-38` 等 `CountAsync` + `Skip/Take`；`GetMessageHistory.cs:62-64` 缓存命中路径仍白付一次 `Count` | 接口加可选 `beforeId`/`beforeSentAt` 走 keyset；`page>1` 跳过 Count |
| 整行 UPDATE 改一个布尔 | ❌ 仍存在 | `ReadStateCommands.cs:26-30`、`:116-120`（find + `SetSource`）；`RobotTriggerHandlers.cs:282-283` 计数器同理 | 改单列 `Set(...)` 更新（仓库里已有单列写法可照抄） |
| `MembersModal` 的 O(n²) | ❌ 仍存在 | `MembersModal.vue:51,73` 的 `canSetAdmin`/`canKick` 各自 `members.find()`，`:115` 在 `v-for` 中调用（`:128,131`） | `myRole` + 管理员 id 集合提到 `computed` |
| `ChatSidebar` 全量重渲染 | ⚠️ 部分 | 无 `v-memo`/行级子组件，行内仍从响应式 Map 取预览/时间/未读 | 抽出行组件或 `v-memo` |
| `manualChunks` / 弹窗异步化 | ❌ 仍存在 | 两个 `vite.config.ts` 无 `build.rollupOptions`；`ChatLayout.vue:79-90` 静态导入 10 个弹窗 | 拆 `vendor`/`axios`；弹窗改 `defineAsyncComponent` |
| 白发请求（启动 RTT、切会话重拉、无条件已读、切标签重拉） | ❌ 仍存在 | 启动 3 段串行 RTT（`ChatLayout.vue:200→226→231`）；`:507-511` 切标签重拉；`ChatArea.vue:220` 每次切会话重拉第 1 页 | 见 §2.3 |
| DI 注册期连库（冒烟测试前提） | ❌ 仍存在 | `DependencyInjection.cs:60` 在 `AddPersistence` 里 `EnsureDatabaseExists`；测试工程无 `Mvc.Testing`/`Testcontainers` | 挪到 `IHostedService.StartAsync`（`Program.cs:230-233` 已在 `Run` 前显式调用，时序等价） |
| 启动期 ad-hoc DDL 无版本 | ❌ 仍存在 | `DatabaseInitializer.cs:72-84` 六步硬编码；索引/超管失败只 `LogWarning`（`:304-325, 350-353`） | 加 `SchemaVersion` 表 + 步骤序号，把「漏看警告」变成「启动报缺哪一步」 |
| `/metrics` 与 JSON 日志 | ❌ 仍存在 | 全仓无 Prometheus/OpenTelemetry/Meter；`appsettings.json` 只有 `LogLevel` | 最小做法：把连接数/在线数做成文本端点；生产用 JSON console |
| CI | ⚠️ 部分 | `ci.yml:32,36,44-64` 覆盖构建+测试+两个前端；但无 `docker build`/`compose config`、无 lint、无覆盖率、无 `permissions:` | 加 docker 构建 job（Dockerfile/nginx.conf 目前零覆盖） |

### 2.2 本轮新增的 P1

| 问题 | 证据 | 影响 / 做法 |
|---|---|---|
| 缺索引 | `User_.CreatedAt`（`UserRepository.cs:94-101`，仪表盘 7 天各扫一次全表）、`Group_.CreatedAt`（`GroupRepository.cs:53-54`）、消息 `IsDeleted`（`GroupMessageRepository.cs:309-315` 等谓词都带它）、`AdminLog.Action`（`RobotAndAdminRepositories.cs:122-123`） | 已核对 `DatabaseInitializer.cs:253-299`，这几列无覆盖；补索引即可 |
| 深分页在内存拼接 | `SearchMessages.cs:157` 取 `page*pageSize` 行后在内存 `OrderBy/Skip/Take`（`:232-237`）；管理端 `AdminMessageCommands.cs:132-206` 同模式 | 第 100 页 = 两表各拉 3000 行；改用 DB 侧分页/游标 |
| 每次消息把 Redis 与 WS 推送关进数据库事务 | `SendGroupMessage.cs:118-123`、`SendPrivateMessage.cs:134-139`（`DomainEventOutbox` 只推迟**领域事件**，缓存写入与推送仍在事务内） | 事务持有时间被 Redis RTT 拉长；把 cache/notifier 移到提交后 |
| 管理写操作多一次查询 | `InfrastructureServices.cs:153` 为拿 `Username` 而 `FindByIdAsync(adminId)`；调用方已持有该实体（`AdminGroupCommands.cs:65`、`AdminMessageCommands.cs:68,85`） | 给 `RecordAsync` 加带 username 的重载 |
| 限流覆盖不足 | 仅有 `SendCode`、`RobotReply` 两条策略（`Program.cs:130-151`）；`AuthController.cs:29/43/48`（注册/刷新/找回）、`MessagesController.cs:54-70`（搜索，pg_trgm 重查询）、`:113-119`（上传，无配额）、`RobotsController.cs:21-26`（建机器人写三张表）均无限流 | 至少给上传、搜索、注册/找回加策略；把机器人分区键改为「令牌哈希」或 IP，避免无限分区 |
| `GET` 请求在循环里写库 | `ManageRobots.cs:216-230` 逐个机器人补令牌并 `UpdateAsync` | 一次批量更新 |
| 代理头信任面过宽 | `Program.cs:115-120` 清空 `KnownProxies/KnownIPNetworks`，靠「后端端口不对外」这一约定兜底 | 当前部署不可绕（`ForwardLimit=1` 取最右一跳），但一旦直连后端，IP 维度限流即可用伪造 `X-Forwarded-For` 绕过；建议显式写信任代理 |
| 管理端路由守卫不校验角色 | `admin-web/src/router/index.ts:47,53` 声明 `superOnly`，但 `:60-69` 的 `beforeEach` 只看 token | 非超管可直接打开 `/admin/admins`、`/admin/logs`（后端 403 兜底，但属 UI 越权） |

### 2.3 前端行为缺陷（P1）

| 问题 | 证据 | 影响 |
|---|---|---|
| 历史分页失败后「加载更早」永久失效 | `chat.ts:292` `let hasMore=false`，`:293-356` 无 `catch`，`finally` 无条件写 `meta.hasMore=false` | 一次网络抖动后上滑加载失效，需重开会话 |
| 群成员列表无归属 | `group.ts:28` 的 `members` 不带 `groupId` | 快切群时旧响应覆盖新群；`MembersModal.vue:51,73` 据此显示不该有的按钮（后端会拒） |
| 会话内搜索无请求序号 | `ChatArea.vue:71-101` | 慢响应覆盖新关键词的结果 |
| 未读角标虚高 | `ChatLayout.vue:225` 先 `connect` 再 `:231` 拉离线；`chat.ts:400` 未去重、按 `list.length` 计未读 | 离线拉取与 WS 在途消息重复计入 |
| 搜索/上传等无输入长度约束 | `MessagesController.cs:54-70`、`:113-119` | 超长关键词可放大 trgm 查询成本 |

---

## 三、P2：工程、运维与打磨

**可观测性与部署**

1. 无 `/metrics`、无结构化日志（见 §2.1 末两行）。
2. `docker-compose.yml` 全文无 `deploy:`/`mem_limit`/`logging:`/`ulimits:`；redis 启动参数（`:29`）无 `--maxmemory` 与淘汰策略 → 缓存无界增长可拖垮整机。
3. 镜像全部来自第三方镜像站 `docker.m.daocloud.io`（`:4,:23` 与三个 Dockerfile），未按 digest 钉死（虽是 `10.0`/`16-alpine`/`7-alpine` 而非 `latest`，仍会随上游漂移）。
4. `frontend-admin` 无 healthcheck（`DEPLOY.md:117` 已说明）。
5. 备份脚本硬编码 `/var/lib/docker/volumes/onlinechat_pgdata` 与容器名 `onlinechat-postgres-1`（`DEPLOY.md:250,255`），克隆目录名一变就失效；`:247` 写「全量备份」却未含 `redisdata`。
6. `ASPNETCORE_URLS=http://+:5000`（Dockerfile）与 `Program.cs:209` 的 `app.Urls.Add("http://0.0.0.0:5000")` 重复声明同一端口，易误导。
7. 机器人令牌在 URL 路径里，而 `nginx.conf:66-72` 的 `/api/` 用默认访问日志（含 `$request`）→ 令牌落 nginx 日志；后端脱敏只覆盖查询串（`Program.cs:294-310`），只有 `/ws` 做了脱敏格式。建议 `/api/robots/` 单独配脱敏日志格式。
8. 未处理异常直接 rethrow（`Program.cs:238-251`），生产环境返回空 500 而非统一 `ApiResponse` 形状。

**测试盲区**（三个测试工程共 946 个用例，但分布不均）

9. 测试工程均**不引用 Server** → 表现层零测试：Controllers、`AdminAuthorize`、限流中间件、JwtBearer 事件、`/health`、WS 握手全无覆盖；这也是 §2.1「DI 注册期连库」的直接后果（无法用 `WebApplicationFactory`）。
10. 无 Realtime/WS 测试（`WsConnectionManager`、`WsInboundDispatcher`、`ChatConnectionHandler`、`RealtimeNotifier`）；Redis 实现（`RedisStores`、`SessionStore`、`LoginThrottle`）只用替身测过；`WebhookDispatcher`、Docker/nginx 配置无测试。

**前端与仓库卫生**

11. `sw.js:4` 的 `VERSION` 需手改，不递增则 `activate`（`:17-23`）不清旧哈希资源，发版后旧资源持续累积。
12. `catch (e: any)` 11 处；`websocket.ts` 内 `console.log/error` 十余处（生产环境建议静音）；`adminToken`/`adminInfo` 明文存 `localStorage`。
13. 根目录残留 `lhz-onlinechat-web/`、`admin-web/`、`LHZ.OnlineChat.Server/` 三个目录，内容只剩 `node_modules`/`dist`/`bin`/`obj`（均未被 git 跟踪、已被 `.gitignore` 忽略）→ 建议本地删除；`frontend/package-lock.json` 是空壳且无对应 `package.json`。
14. `plugins/dsh-bot-notify`：164 行、零依赖、失败只 `console.warn`（`lib/index.js:88`），但 `fetch`（`:84`）无超时，`beforeExit`（`:66`）会 await 未完成的推送。
15. 文档小漂移：`README.md:105` 把 `nuget.config` 画在仓库根（实际在 `backend/`）；`.env.example:37` 注释称验证码「随接口返回」，实际仅开发环境回传（`SendVerificationCode.cs:93`）。
16. 两个自研 NuGet 依赖（`LHZ.FastJson 2.0.1-pre`、`LHZ.WebSocket.AspNetCore 1.2.0`）版本已精确钉死，但 `backend/nuget.config` 用 `<clear/>` 只留 nuget.org → CI/内网构建必须有公网连通性或预热包缓存。

---

## 四、已核实**无需再动**（避免下一轮重复劳动）

- **越权修复是彻底的**：全部 7 个 Admin 控制器/动作都挂了 `AdminAuthorize`（含 `SuperOnly`）；
  群操作（邀请/踢人/设管理员/公告/入群方式/解散/加机器人）、群成员名单、群历史、全局/群内搜索、私聊历史
  都做了成员或好友校验；撤回仅本人且 2 分钟内、标记已读仅接收方、好友申请仅被申请方可处理。
- **验证码链路**：CSPRNG、恒定时间比较、5 次失败作废、非开发环境不回传；登录限流账号+IP 双维度，管理员独立命名空间。
- **令牌链路**：refresh 轮换 + 旧令牌反查删除 + 二次校验；踢下线清 refresh 键/反查索引/元数据/WS 连接；无 `sid` 的旧令牌被拒；生产拒绝仓库默认 JWT 密钥。
- **唯一性与事务**：10 个唯一索引 + 23505 翻译；无原始 SQL 拼接（`Ado` 调用只用到硬编码表名）。
- **上传**：扩展名白名单 + GUID 文件名 + `nosniff`，无法落 `.html/.svg` 造成存储型 XSS。
- **前端**：消息在 `v-html` 前已转义；SW 不缓存 `/api/`；广播路径在循环外只序列化一次（不要「优化」它）；
  「每个认证请求一次 Redis `KeyExists`」是会话即时失效的前提，不要加本地缓存。
- **已完成的性能项**：WS 补发收敛、图片/头像压缩、favicon、CI 骨架。

---

## 五、建议执行顺序

**第一批（P0，改动都很小，1 天内可完成）**
1. `Robot__TokenKey` / `Admin.InitialPassword` 纳入生产自检（+ DEPLOY 必填表）。
2. 私聊补好友校验（复用 `AreFriendsAsync`）。
3. Webhook 地址禁私网 + 响应体大小上限。
4. 前端 WS：保存 unsubscribe + `onUnmounted` 清理、`disconnect` 抑制重连、重连耗尽给 UI 出口。

**第二批（结构性，收益确定）**
5. DI 注册期的建库挪到 `IHostedService` → 补 `WebApplicationFactory` 冒烟测试（第一条断言「DI 图可解析」）。
6. `TransactionBehavior` 支持免事务标记，先放行 `SendTypingCommand` 等纯推送命令。
7. 消息列表渲染窗口 + 去掉全量 `sort`；`MembersModal` 提 `computed`。
8. 限流补齐（上传/搜索/注册/找回），机器人分区键改为令牌哈希。
9. 补索引（`User_.CreatedAt`、`Group_.CreatedAt`、消息 `IsDeleted`、`AdminLog.Action`）。
10. 顺手修：`HourlyAggregate` 改异步、`chat.ts` 的 `hasMore` 失败路径、`group.ts` 成员归属、管理端路由角色守卫。

**第三批（等有真实数据再做，先加度量）**
11. `/metrics` + 生产 JSON 日志 + redis `maxmemory` + 容器内存上限。
12. 仪表盘并发化、`GetSessions` 聚合、撤回 Lua、keyset 分页、单列 UPDATE、管理写操作去掉多余查询。
13. 前端 `manualChunks` / 弹窗异步化 / `sw.js` 版本自动化 / CI 增补 docker 构建与 lint。

---

## 六、修复记录（5 个 P0）

| # | 问题 | 改动 | 验证 |
|---|---|---|---|
| P0-1 | `Robot__TokenKey` 占位值未自检 | `AppSettings.cs`：新增 `KnownPlaceholderValues` + `ContainsPlaceholder`，`Robot:TokenKey` 命中占位值即拒绝启动 | 语法校验 ✅；行为未运行（Server 层编译不了） |
| P0-2 | `ADMIN_INITIAL_PASSWORD` 占位值未自检 | 同上；并覆盖连接串里的 Postgres / Redis 口令占位值。`.env.example`、`docker-compose.yml`、`README.md`、`docs/DEPLOY.md`（必填表 + 上线清单 + 升级行为变化第 4 条）同步 | 同上 |
| P0-3 | 私聊缺好友校验 | `SendPrivateMessage.cs` 注入 `IFriendshipRepository`，黑名单之后加好友校验，非好友只回执发送者 | 语法校验 ✅；新增 `非好友发送被拒_不落库不广播`；原「唯一索引」用例补了前置好友关系 |
| P0-4 | Webhook 地址不限内网（SSRF） | ① 领域层 `WebhookUrl.EnsureTargetAllowed` + `IsBlockedAddress`（环回/私网/链路本地/CGNAT/ULA/组播 + 单标签主机名 + 内部后缀；`Parse` 保持宽松不动读取路径）② 写时校验：`CreateRobotHandler` / `UpdateRobotHandler` ③ 调用前二次校验（域名解析结果）：`WebhookDispatcher.DescribeBlockedTargetAsync`，并给响应体加 64KB 上限 ④ 开关 `Robot:AllowPrivateWebhookTargets`（默认 false）贯穿 `AppSettings` → `RobotOptions` → `IWebhookTargetPolicy` | **行为已实跑**：59 条断言全过（含 9.255.255.255 / 172.15 / 172.32 / 100.63 / 100.128 等边界，以及 `Parse` 宽松性回归）；Domain + Domain.Tests 编译 0 警告 ✅ |
| P0-5 | 前端 WS 订阅与重连生命周期 | `websocket.ts` 新增 `manualClose`（主动断开不再重连）、`reconnectExhausted` 状态并在耗尽时置位；`ChatLayout.vue` 保存 unsubscribe、`onUnmounted` 清理、window 监听改具名函数并移除、新增「重新连接」横幅；`ChatArea.vue` 断线文案区分「重连中」与「已断开」 | `vue-tsc --noEmit` exit 0 ✅（`noUnusedLocals/Parameters` 已开） |

**未做的事（有意）**：`SendTypingCommand` / `SendReadReceiptCommand` 仍不校验好友关系。
理由是收益极低而成本明确：这两个帧对攻击者没有可观测回执（不构成探测通道），
且陌生人现在已无法建立会话，而 typing 是按键频率的高频帧，加一次好友查询等于给热路径加一次数据库往返。

**编译验证（事后补齐）**：包缓存被重新还原后，四层都已实际编译通过：

| 层 | 方式 | 结果 |
|---|---|---|
| Domain / Domain.Tests | `dotnet build`（正常流程） | 0 警告 0 错误 |
| Application | `dotnet build`（正常流程） | 0 警告 0 错误 |
| Infrastructure | 探针（见下） | 0 警告 0 错误 |
| Server | 探针（见下） | 0 警告 0 错误 |

之所以 Infrastructure / Server 要走探针：**本机 SDK 的 workload 解析器损坏** ——
`C:\Program Files\dotnet\sdk\10.0.301\Sdks\Microsoft.NET.SDK.WorkloadAutoImportPropsLocator\Sdk`
不存在，导致任何 `ProjectReference` 的嵌套求值（`_GetProjectReferenceTargetFrameworkProperties`
→ 被引用项目的 `GetTargetFrameworks`）以 `MSB4276` 失败，表现为「生成失败，0 个警告 0 个错误」。
探针的做法是：直接 `Compile Include` 真实源码目录 + 用已编译的 Application/Domain DLL 与
`project.assets.json` 里解析出的引用代替 `ProjectReference`，因此绕开了嵌套求值，
编译的仍是同一份源码与同一组引用。
**结论：本轮改动在四层都能编译。** 完整 `dotnet test`（946 个用例）仍未在本机跑过（VSTest 需要打开
子进程句柄，被沙箱拒绝），请在 CI 或本机 `dotnet test` 复核。
