# 编码规范细则（AGENTS.md 的展开）

红线与自检清单在 [AGENTS.md](../AGENTS.md) —— 那份每轮都会加载；本文件是**细则**，按需查对应小节即可，不必整篇读。两者冲突时以 AGENTS.md 的红线为准。

| 节 | 内容 |
|---|---|
| §1 | 分层职责 + 工程/构建约定 |
| §2 | 用例与管道（Command/Handler 模板、Behavior 顺序） |
| §3 | 数据访问与性能 |
| §4 | 实时通信（WebSocket） |
| §5 | 测试规范 |
| §6 | 前端规范 |
| §7 | 注释与文档同步 |
| §8 | 提交规范 |
| §9 | 踩坑案例集（本仓库真实发生过） |

---

## §1 分层职责与工程约定

**Domain（`backend/LHZ.OnlineChat.Domain/`）**
- 实体 = `Entity<TId>` / 聚合根 = `AggregateRoot<TId>`：私有 setter、私有构造 + 静态工厂（`Create` / `Send` / `Request`…），**外部不能直接 new**。
- 值对象 = `ValueObject` 子类：校验与归一化收敛在类型内部，构造即合法（例：`Email`、`WebhookUrl`、`MentionList`）。需要落库的经 FreeSql `TypeHandler` 双向转换。
- 业务规则违反一律 `throw` 或 `DomainException.Ensure(cond, "面向用户的提示")`；`Message` 会原样出现在前端提示里，**要说人话**。
- 派生异常：`EntityNotFoundException`（资源不存在，且**不要泄露存在性**，例：`EnsureOwnedBy` 抛"机器人不存在"而不是"无权"）、`ForbiddenOperationException`。
- **领域层不认识 MediatR**：事件是普通 POCO（`IDomainEvent`），聚合根用 `Raise(...)` 累积，应用层 `DequeueDomainEvents()` 取出后派发。
- 时间：一律 `IClock.UtcNow`，**禁止** `DateTime.Now` / `DateTime.UtcNow`。从库里读回的 `DateTime` 用 `UtcTime.Normalize(...)` 补 `Kind`。

**Application（`backend/LHZ.OnlineChat.Application/`）**
- 一个用例一个 Command/Query + Handler；Handler 用 `internal sealed class`（单元测试经 `InternalsVisibleTo` 直接构造）。
- 用例**显式携带操作者**（`SenderId` / `OperatorId` / `OwnerId` / `RequesterId`），不要从 HttpContext 隐式取 —— 这样 HTTP 与 WS 路径能复用同一个用例，测试也不必伪造请求上下文。表现层用 `ICurrentUser` 填充这些字段。
- 一律 `DomainException` 抛业务错误，**不要在 Handler 里拼 ApiResponse.Fail**（由管道统一转换，见 §2）。
- 跨用例复用的协作对象放 `Application/Common/` 或对应上下文（例：`FriendSettingWriter`），不要复制粘贴两遍规则。

**Infrastructure（`backend/LHZ.OnlineChat.Infrastructure/`）**
- 持久化：`FluentApi` 映射集中在 `Persistence/EntityConfiguration.cs`；实体是纯 POCO，所以**加字段必须同时补映射**，否则列名/忽略列会错。
- 仓储走 `DbSession`（`Select/Insert/Update/Delete`、`InsertWithIdentityAsync`…），事务由 `FreeSqlUnitOfWork` 承载；**不要用 `IFreeSql` 裸写业务查询**绕开会话。
- 出站/入站外部 I/O（邮件、Webhook、Redis、文件、WS）都在这一层；每个端口实现都要有超时与日志。

**Server（`backend/LHZ.OnlineChat.Server/`）**
- Controller 只做：取 `ICurrentUser` → 组装 Command/Query → `ISender.Send` → 返回 `ApiResponse`。
- 配置集中在 `Configuration/AppSettings.cs`：**新增配置项要同时改** `.env.example`、`docker-compose.yml`、`docs/DEPLOY.md`（同步表见 §7）。
- 生产必填/危险配置要进 `EnsureProductionReady()` 自检（缺了或还是占位值就**拒绝启动**），不要只打一条警告。

**工程与构建约定**
- 四层 + 三个测试工程都列在 `backend/LHZ.OnlineChat.slnx`；新增项目要同步登记，并更新 `backend/Directory.Build.props` 的注释。
- `TreatWarningsAsErrors=true`：任何警告都会让构建失败。需要放过时用 `NoWarn` 并**在文件里写明理由**（照 `Directory.Build.props` 的写法）。
- **`Directory.Build.props` 的继承链**：MSBuild 向上找它是"就近取第一个就停"，不会沿路合并 —— 所以 `backend/tests/Directory.Build.props` 必须显式 `Import` 上一级，`backend/Directory.Build.props` 也必须显式向上导入：

  ```xml
  <PropertyGroup>
    <_AboveDirectoryBuildProps>$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))</_AboveDirectoryBuildProps>
  </PropertyGroup>
  <Import Project="$(_AboveDirectoryBuildProps)" Condition="'$(_AboveDirectoryBuildProps)' != ''" />
  ```

  起点必须是**上一级**（写成当前目录会找到自己、变成自导入）；必须判空（父级不存在时跳过）。**新增任何一层 `Directory.Build.props` 都要照做**，否则父级设置会被静默忽略（不报错、不生效）。
- 改完构建配置用 `dotnet msbuild <proj> -getProperty:<Name>` 看生效值，不要靠肉眼。
- **工具链版本**：SDK 由仓库根 `global.json` 钉住 —— 下限 `10.0.100` + `rollForward: latestFeature`，即 10.0.x 内任意 feature band 都放行（`sdk:10.0` 镜像与 CI 的 `10.0.x` 都能解析），.NET 11 及以上会被拒绝。要严格复现就把它提到团队统一版本、改成 `latestPatch`。
- **格式基线**：根 `.editorconfig`（UTF-8 无 BOM、C# 4 空格 / 前端与配置 2 空格、`Dockerfile` 与 `.sh` 强制 LF、Markdown 不裁行尾空格）。换行**故意交给 git**（`core.autocrlf=true`，索引存 LF、按平台检出）—— 编辑器再强制一边只会制造"整文件翻转"的假改动。其中的 C# 风格项一律 `suggestion`，**不改变构建结果**；会让构建失败的红线仍然只在 `Directory.Build.props`。
- **依赖升级**：`.github/dependabot.yml` 覆盖 nuget / 两个 npm / GitHub Actions，每周一 09:00（Asia/Shanghai）向 `dev` 开 PR，minor+patch 合组；**故意忽略自研包 `LHZ.FastJson` 与 `LHZ.WebSocket.AspNetCore`**（升级等价于改 WS 线协议，必须连 README 协议表与前端一起动）。漏洞驱动的 PR 还需在 GitHub 仓库设置里开启 Dependabot alerts + security updates。

---

## §2 用例与管道（照抄既有形状）

```csharp
public sealed class DoSomethingCommand : ICommand<ApiResponse<ThingDto>>   // IQuery<T> 用于读
{
    public int OperatorId { get; set; }      // 操作者显式携带
    public long TargetId { get; set; }
}

internal sealed class DoSomethingHandler : IRequestHandler<DoSomethingCommand, ApiResponse<ThingDto>>
{
    // 只依赖 Application 声明的端口 / 仓储接口（Domain 里定义的仓储接口）
    public async Task<ApiResponse<ThingDto>> Handle(DoSomethingCommand command, CancellationToken ct)
    {
        var target = await _repo.FindByIdAsync(command.TargetId, ct).ConfigureAwait(false)
                     ?? throw new EntityNotFoundException("目标不存在");
        target.EnsureOwnedBy(command.OperatorId);          // 业务规则在聚合根上
        await _repo.UpdateAsync(target, ct).ConfigureAwait(false);
        await _events.DispatchEventsOfAsync(target, ct).ConfigureAwait(false);   // 落库之后再派发
        return ApiResponse<ThingDto>.Ok(target.ToDto());
    }
}
```

管道顺序（`Application/DependencyInjection.cs`，由外到内，**不要插队**）：
`LoggingBehavior`（能记到异常翻译前的原始信息）→ `DomainExceptionBehavior`（`DomainException` → `ApiResponse.Fail`）→ `UniqueConstraintBehavior`（唯一索引冲突 → `DomainException`）→ `TransactionBehavior`（**必须在异常翻译之内**，否则业务驳回会连带提交半截写入）。

- 领域事件在事务内**只入队**（`DomainEventOutbox`），提交后才真正派发。因此：**不要假设订阅方在事务里执行**，也不要把"必须回滚的东西"放进订阅方。
- 新增唯一约束时，除了在 `DatabaseInitializer` 建索引，还要在 `UniqueConstraintBehavior.MessageByConstraint` 里给出面向用户的提示语。
- 事务里**不要做网络/Redis/WS 调用**（会拉长持锁时间）；缓存写入与推送放到提交之后。

---

## §3 数据访问与性能（有真实数据量后最先崩的就是这些）

| 禁止 | 替换做法 |
|---|---|
| 循环里 `await` 单条查询 / 单条 Redis 命令（N+1） | 批量查询（`GetManyAsync` / `ListByIdsAsync` / 窗口函数一次取回），或 `Task.WhenAll` 并发独立查询 |
| 无上限查询（`ToListAsync` 不带分页/上限） | 显式分页或 `LIMIT`；离线补发按群 100 条这类上限要保留 |
| 为改一个布尔而整行 UPDATE | 单列更新（`Update().Set(...).Where(...)`） |
| `page > 1` 还跑 `CountAsync` + `Skip/Take` 深分页 | 游标/keyset（`beforeId` / `beforeSentAt`）；首页之外不做全表 COUNT |
| 同步阻塞式 DB 调用出现在 async 链路（`ExecuteDataTable`） | `...Async` 版本；不要用 `Task.FromResult` 包同步调用 |
| 广播循环里查库/查 Redis/逐条序列化 | 循环**之前**批量取数、**序列化一次**，循环里只写 socket |
| 注册期 I/O（`AddInfrastructure` 里连库建库） | 已知历史包袱，**不要新增**；新初始化逻辑放 `IHostedService` |

- **规则 3.1** 查询排序必须带**次级键**（`SentAt DESC, Id DESC`），否则 `SentAt` 相同的行在分页下会重复/漏行 —— 仓库有专门测试（`MessageQueryOrderingTests`）钉住这点。
- **规则 3.2** 搜索关键词进 `LIKE` 前考虑通配符与 trgm 成本；搜索接口必须有长度上限。
- **规则 3.3** 新查询要在 `DatabaseInitializer` 里补相应索引，并在 PR/提交信息里说明命中哪个索引。
- **规则 3.4** 前端列表/数组凡是无界增长的，都要有上限常量（参考 `chat.ts` 的 `MAX_SESSIONS` / `MAX_MESSAGE_SESSIONS`）。

---

## §4 实时通信（WebSocket）

- 推送一律经 `IRealtimeNotifier` 端口，**不要在业务代码里直接摸 socket**。
- 对外协议是**公开契约**：`WsMessage` 的字段名（camelCase）、事件类型字符串（`friend_request` / `group_removed` / `kicked` …）改名等于破坏客户端。要加字段可以，改名/删字段必须同步 `README.md` 的 WS 协议表 + 前端。
- 入站报文在 `Infrastructure/Realtime/WsInboundDispatcher.cs` 解析后**只做路由**，业务逻辑一行都不留在这里。
- 入站失败不能崩连接：解析失败/处理失败各自 `try/catch` + `ILogger`，**不回错误帧以免探测信息**（例如"你不是群成员"这种事不能回给发送者）。
  - 例外：**发送方自己输入**引发的内容校验失败（超长/为空/类型未定义）可以明确回执（`message_failed`，见 `WsContentGuard`）——
    这类失败不含任何他人信息，不回反而会让消息静默消失（2026-10 长文本被 5000 字上限拦掉、用户只看到"未读"就是这么来的）。
    判定标准：这句话会不会透露"别人/别的群"的存在或关系；会，就静默。
- 心跳路径（`heartbeat`）不要开 DI 作用域、不要查库；在线状态是带 TTL 的 Redis 键，靠心跳续期。
- 多端语义：消息推给用户**全部在线设备**；踢下线要清 Redis 会话 + refresh 反查索引 + WS 连接三件套。

---

## §5 测试规范

**命名（2026-10 起统一）**
- 方法名用**英文标识符**，沿用 `方法_场景_预期` 三段式下划线风格（这正是 `backend/tests/Directory.Build.props` 放开 CA1707 的原因）。
- 中文说明放 `DisplayName`，**逐字符**保留原意：

  ```csharp
  [Fact(DisplayName = "Create_记录拉黑方向")]
  public void Create_RecordsBlockDirection() { ... }

  [Theory(DisplayName = "Create_被拉黑ID非法时抛出")]
  [InlineData(0)] [InlineData(-1)]
  public void Create_InvalidBlockedUserId_Throws(int blockedId) { ... }
  ```

  （xUnit v2 会把参数追加到 DisplayName 之后，所以 Theory 行仍显示为 `中文名(arg: value)`。）
- **不要再写中文标识符**；新增/改名同步加 `DisplayName`。

**组织与风格**
- 一个类内按场景分组，用嵌套类/多个 `class` 分段（参考 `SendMessageTests.cs`）。
- **不使用 mock 框架**：手写内存替身放 `TestDoubles/`（真存储、真分配自增主键），共享装置用 `ApplicationTestContext` 的 `GivenUser` / `GivenFriends` / `GivenGroup` / `GivenRobot` 等。
- 一个测试只断言一个行为；命名即规格，断言失败信息要能自解释。
- 需要参数化就 `[Theory] + [InlineData]`；不要写"一个 Fact 里跑五种场景"的长测试。
- 新增替身/端口实现时要同步 `ApplicationTestContext` 暴露出来。
- 别测实现细节（私有方法、SQL 文本）—— 例外是刻意的契约测试（如 `MessageQueryOrderingTests` 用 `ToSql()` 钉住排序）。

**前端（独立测试工程 `frontend/tests`）**
- **测试不放在应用目录里**：应用只负责"能构建"，vitest / jsdom / @vue/test-utils 不进它的 dependencies，也不进镜像。前端测试是独立 npm 工程，用 `@` 别名（与应用的 vite 别名一致）引用被测源码；`resolve.dedupe` 保证 vue/pinia 只有一份实例（否则 store 与测试各自 `getActivePinia()`，报错很难懂）。
- 只覆盖**可确定性断言**的部分：纯函数、解析/消毒、Pinia store 逻辑、组件**渲染成什么**（文本/图片/Markdown/已撤回分支）。不要去测模板长什么样、样式、`axios` 拦截器的网络时序。
- 安全相关的渲染（`v-html` 前的消毒）必须有**反例用例**：`<script>`、`onerror`/`onclick`、`javascript:`/`data:`/`vbscript:`、`<iframe>`/`<svg>`/`<form>`、Markdown 图片语法 —— 这类用例的价值高于覆盖率数字。
- 改应用依赖版本要同步 `frontend/tests/package.json`：`dependency-parity.test.ts` 会比对两边范围，写歪了直接失败（防止"测试跑的是旧 marked、线上是新 marked"的假绿）。
- 被测源码裸引用的运行时依赖（vue/pinia/axios/marked/dompurify/highlight.js）在 `frontend/tests/tsconfig.json` 的 `paths` 里显式映射到本工程 `node_modules`；`vitest.config.ts` 用 `resolve.dedupe` 做同样的事。**两条都不能删**：CI 只装本工程那一份，删了在干净环境里 `TS2307` / `getActivePinia()`，而本地因应用装过依赖会假绿。
- 本地：`cd frontend/tests && npm run test`（watch 用 `npm run test:watch`，类型检查 `npm run typecheck` —— 用 vue-tsc 才能覆盖 `.vue`）。

---

## §6 前端规范（Vue 3 + TS + Pinia）

- `<script setup lang="ts">` + Composition API；类型检查由 `vue-tsc` 把关，**`noUnusedLocals` / `noUnusedParameters` 已打开**，死代码不许留。
- 网络请求集中在 `api/`，状态在 `stores/`；组件不直接 `axios`。
- **生命周期必须配对**：`ws.onMessage(...)` / `onStatusChange(...)` 返回取消订阅函数，组件卸载（`onUnmounted`）时**必须调用**；`window.addEventListener` 要配 `removeEventListener`。SPA 内部跳转不会刷新页面，漏清理会导致"换账号后回调叠加、串号"。
- 主动断开要能抑制自动重连（参考 `websocket.ts` 的 `manualClose`），重连耗尽要有 UI 出口而不是只打 console。
- **列表项里的浮层状态必须是单例**：消息右键菜单、Toast 这类「同一时刻只该存在一个」的浮层，状态放模块级单例（composables/useMessageMenu.ts、useToast.ts），**不要**放进每个列表项组件 —— 每个气泡各持一份 menu 时会「右键几条就叠几个弹窗」；监听器同理，只装一份（上千条消息时就是上千个 document 监听）。
- **对话页按职责分层，别再堆回 ChatArea**：ChatArea（编排：历史分页、滚动锚点、未读分隔线、多选转发、公告、lightbox）→ ChatHeader（头部/正在输入）→ ChatSearchPanel（会话内搜索，防抖与分页在自己身上）→ MessageComposer（草稿/输入/表情/@/图片/引用/发送/提示）→ composer/{EmojiPanel,MentionPanel}（纯展示，只 emit pick）。发送原语统一走 utils/messageSend.ts，输入区对外只暴露 eply/reedit/setHint/focus/retry，发送成功只 emit('sent') 由父级滚动。
- **消息内容按类型拆成独立组件**：`components/chat/messages/{Text,Image,Markdown,Forward}Message.vue`，样式随组件走；`MessageBubble` 只留行级职责（头像/发送者/时间/已读/操作菜单/多选）。新增内容类型时加一个 `XxxMessage.vue` 并在 `MessageBubble` 里加一支 `v-else-if`，**不要**把渲染逻辑继续堆在 `MessageBubble` 里。文本渲染（转义 + @提及 + 关键词高亮）是纯函数 `utils/messageText.ts`，单独测；`v-html` 内容的后代选择器一律 `:deep()`（注入的节点没有 scoped 属性）。
- 竞态：切会话/搜索这类会并发发请求的地方，用请求序号或"在途标记"防止旧响应覆盖新数据；列表合并避免每次全量 `sort`（大多是新消息追加）。
- **未读语义**：只有「消息属于当前会话 **且** `document.visibilityState === 'visible'` **且** `document.hasFocus()`」才算已读；窗口切后台/最小化/被挡住一律按未读处理（红点 + 提示音），回到前台再补标已读（`ChatLayout` 的 `markCurrentChatRead`）。入站动作统一由 `utils/unread.ts` 的 `decideIncomingAction` 决定，不要在组件里散写 `if`（历史 bug：会话被选中但窗口在后台，消息被静默标已读 —— 用户看不到红点，发送方那边还显示"已读"）。WS **重连**后必须补一次 `fetchSessions` + `loadOfflineMessages`，断线期间的消息不会自己回来。
- 图片：上传前压缩（`utils/image.ts`），头像导出用 WebP；不要在缩放时阻塞主线程太久或漏掉 EXIF 方向。
- **草稿与未读锚点都在 chat store 里**：草稿按 `sessionKey` 存（`drafts`，普通/Markdown 两份 + 模式），未读分隔线存 `unreadAnchors`（打开会话时把"尾部 N 条未读"换算成第一条的消息 ID）。**不要在组件里另起一份本地状态** —— 那正是"切会话草稿串了 / 分隔线不消失"的来源。
- **输入中提示**（`typing`）只做转发、不落库：客户端 3 秒节流发帧，收到方只在**当前会话**显示、4 秒无新帧自动消失；`typing` 帧必须在 `handleWsMessage` 里**提前 return**，绝不能流进 `addMessage`（否则会变成一条空消息）。
- **弹窗键盘支持是全局的**：`Esc` 关闭与 Tab 焦点锁定由 `ChatLayout` 的 `onGlobalKeydown` 统一处理（按 `.modal-overlay` 找当前弹窗），弹窗组件根元素统一带 `role="dialog" aria-modal="true"` —— 新加弹窗照抄这两个属性即可，不用各自实现键盘逻辑。
- **聊天记录（合并转发）是 `messageType=4` 的 JSON 快照**：一次转发 = 一条消息 = 一张卡片。打包/解析/预览都在 `utils/forward.ts`（`buildForwardBundle` / `parseForwardBundle` / `messagePreview`），上限常量必须与服务端 `ForwardBundle`（Application）一致 —— 服务端会拒收超限或结构非法的载荷并回 `message_failed`。卡片是**只读快照**（原消息被撤回也不变），不要改成"引用原消息"；渲染走 `ForwardViewerModal`（Markdown 消毒、图片地址白名单）。会话列表与搜索结果的预览必须走 `messagePreview`，别把 JSON 打到界面上。
- **安全**：`v-html` 之前必须转义（参考 `MessageBubble.vue`）；不要把非令牌的敏感数据放 `localStorage`；退出登录要 `ws.disconnect()` + 清 store（内存里的会话与消息缓存一并清）。
- PWA：`sw.js` 不缓存 `/api/`；`VERSION` 递增才会清旧缓存。

---

## §7 注释与文档同步

- 注释写**为什么**（约束、历史原因、踩过的坑），不写"这行在赋值"。中文，与现有风格一致。
- 公开类型/成员要有中文 `<summary>`；抑制某条分析器规则时，**必须在该处注明理由**（照 `Directory.Build.props` 的写法）。
- 命名：领域语言用英文标识符；面向用户的字符串用中文；不要拼音。
- **改了这些就必须同步文档**（历史上已经漂移过多次）：

| 改了什么 | 必须更新 |
|---|---|
| 对外接口 / 响应形状 / WS 协议 | `README.md`（含 WS 协议表） |
| 环境变量 / 必填配置 / 部署步骤 / 行为变化 | `.env.example`、`docs/DEPLOY.md` |
| 测试数量、限流阈值、功能开关 | `README.md` |
| 架构决策、已知取舍 | `docs/` 下的分析报告（新增一节，别改历史结论） |

---

## §8 提交规范

```
<type>: <中文摘要，一句话说清做了什么>

<正文：为什么这么改；分 ## 小节写关键决策与取舍；列出验证结果；未验证的部分要明说>

BREAKING CHANGE: <仅当行为/配置/契约有破坏性变化时写：谁需要做什么>
```

- `type` 取值沿用历史：`feat` / `fix` / `perf` / `refactor` / `docs` / `chore`；破坏性变更用 `fix!` / `feat!` / `refactor!`。
- 正文要写：**动机 → 关键改动 → 验证证据（命令 + 结果）→ 未验证项**。不要只写"修复 bug"。
- **不要提交**：`.env`、真实密钥、`obj/`、`bin/`、`node_modules/`、`dist/`；不要提交与本次改动无关的顺手重构（单独一个提交）。
- 一次提交只做一件事。Rename、格式化这类纯机械改动要单独提交，便于 review 与回滚。
- 提交信息里引用代码位置用 `文件:行号`，引用的测试名用它的 `DisplayName`（中文那条）。

---

## §9 踩坑案例集（本仓库真实发生过）

| 现象 | 教训 | 规则 |
|---|---|---|
| `.env.example` 的占位密钥被直接上线，机器人令牌可被伪造 | 占位值必须让进程拒绝启动，而不是打条警告 | AGENTS.md §三.2 |
| Webhook 地址填 `169.254.169.254` / 容器内主机名，服务端替攻击者打内网 | 用户填的 URL 是出站请求目标，必须校验两次 | AGENTS.md §三.1 |
| 任意账号能给陌生人发私聊并触发其桌面通知 | 交互类用例要校验关系；操作者来自链路上下文 | AGENTS.md §三.4 |
| 登出后 WS 用旧 token 重连、回调叠加导致跨账号串号 | 订阅要能取消、主动断开要抑制重连、卸载要清理 | §6 |
| 给 `[Theory]` 设 DisplayName 担心丢参数 | xUnit v2 会追加参数，可放心加 | §5 |
| `Directory.Build.props` 改了不生效 | MSBuild 就近取第一个就停，子目录那份必须显式导入 | §1 工程约定 |
| `dotnet build` 报"生成失败，0 个警告 0 个错误" | 先怀疑 SDK/环境（本机曾因 workload 解析器缺失导致 MSB4276），不要盲改代码 | AGENTS.md §一 |
| 改了对外行为，README/DEPLOY 还是旧的 | 行为变更必须同步文档，否则下一轮按错误前提改代码 | §7 |
| 私聊消息 ID 口径在服务端与前端各算了一遍，逐渐漂移 | 口径只允许有一处（服务端 DTO 的 `PublicMessageId`），前端不再兜底计算 | §1（Application DTO 约定） |
| 仪表盘统计同步阻塞、异常只进 `Console` | 用 async API；异常走 `ILogger` | §3 |
| 数据库卷里的旧口令与新 `.env` 不一致，后端 crash loop | `POSTGRES_PASSWORD` 只在空卷时生效；排错步骤见 `docs/DEPLOY.md` 常见问题 | — |
