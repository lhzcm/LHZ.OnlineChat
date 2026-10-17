# AGENTS.md — 动手前必读（红线 + 自检）

给 AI 编码助手的硬性约束。**这里只放红线与清单，细则见 [docs/CODING_STANDARDS.md](docs/CODING_STANDARDS.md)** —— 按需查对应小节，不必整篇读（索引在末尾）。

规范与既有代码冲突时：先弄清既有代码为什么那样写（多半有注释），在回复里指出冲突并给建议，**不要默默按规范改掉它**。

## 一、改完必须跑（跑不了必须明说）

```bash
dotnet build backend/LHZ.OnlineChat.slnx --nologo --configuration Release   # 必须 0 警告 0 错误
dotnet test  backend/LHZ.OnlineChat.slnx --nologo --configuration Release --no-build
cd frontend/lhz-onlinechat-web && npm run build    # = vue-tsc && vite build
cd frontend/admin-web        && npm run build
cd frontend/tests            && npm run test     # 前端单测(vitest，独立工程)
```

- 跑不了要写清"哪条没跑、为什么、用什么替代验证"。**禁止把"编译通过"说成"测试通过"。**
- 改了后端任一层就必须跑构建 + 测试；只改 `.md` 可免。

## 二、依赖方向（编译期强制，不是自觉）

```
Server ──→ Infrastructure ──→ Application ──→ Domain
```

| 层 | 绝对禁止 |
|---|---|
| **Domain** | 任何 NuGet 包（连 JSON / ORM / DI / MediatR 都不行）、其它三层 |
| **Application** | FreeSql、Redis、ASP.NET / HttpContext、WS 报文、其它两层 |
| **Infrastructure** | 业务规则（"能不能"必须回到 Domain）、直接返回 HTTP 形状 |
| **Server** | 业务逻辑（Controller 只做转发 + 鉴权 + 组装） |

需要外部能力时：**Application 声明端口 → Infrastructure 实现**（`IClock` / `IEmailSender` / `IWebhookDispatcher`…）。构建配置（`Directory.Build.props` 的继承链等）见 [§1](docs/CODING_STANDARDS.md)。

## 三、安全红线

1. **用户填的 URL 都不能信**：服务端主动请求前必须过 SSRF 校验 —— 写入时 `WebhookUrl.EnsureTargetAllowed(url, allowPrivate)`，调用前再按**域名解析结果**判一次。
2. **密钥/口令不允许有"能跑的默认值"**：占位值必须进 `EnsureProductionReady()` 拒绝启动，并同步 `.env.example` / `compose` / `DEPLOY.md`。
3. **验证码/令牌/口令不进日志与响应**（生产环境不回传验证码；查询串按 `RedactQuery` 那套脱敏）。
4. **越权校验写在用例里**，操作者身份只能来自链路上下文（`ICurrentUser` / 入站报文鉴权结果），不能来自请求体。
5. **不做存在性泄露**：无权限时抛"不存在"而不是"无权"。
6. **上传**走 `UploadRules`（扩展名白名单 + 大小上限），文件名由服务端生成。
7. **匿名且昂贵的接口必须有限流**（`RateLimitPolicies`），分区键要服务端可控。
8. **不许为了调试/测试放宽安全边界**（关限流、开内网 Webhook、收窄校验）。

## 四、工作过程约束

1. **先读后写**；不确定 API 是否存在就 grep 或看 `.csproj` / `package.json`，**不臆造类型、方法、包**。
2. **最小 diff**：不顺手重构、不重排 using / 成员、不改无关格式。
3. **不动生成物与配置**：`obj/`、`bin/`、`dist/`、`.vscode/`、`.env`、lock 文件。
4. **一次提交只做一件事**；纯机械改动（改名、格式化）单独提交。
5. **不为让测试变绿而改断言语义**（测试写错了要说明为什么原断言是错的）。
6. **顺手修的既有缺陷要单独标注**；改对外契约/启动行为前先问。
7. **拿不准就说明假设与影响面**，不要猜一个"看起来对"的实现。

## 五、提交前自检

- [ ] 后端 build **0 警告 0 错误**、test 全绿（跑不了则明说）
- [ ] 改动的每个前端应用 `npm run build` 通过；改了前端源码/测试则 `cd frontend/tests && npm run test` 通过
- [ ] 没有跨层引用，Domain 仍零外部包；改过构建配置用 `-getProperty` 验过
- [ ] 新增配置项 → 自检 + `.env.example` + `compose` + `DEPLOY.md` 四处同步
- [ ] 改对外接口 / WS 协议 / 行为 → `README.md`（含 WS 协议表）同步
- [ ] 新增测试：**英文方法名 + 中文 `DisplayName`**，未引入 mock 框架
- [ ] 新增查询/写入：想过 N+1、上限、索引、次级排序键、事务内无 I/O
- [ ] 涉及用户输入：想过校验、越权、限流、日志泄露
- [ ] diff 里没有 `.env`、生成物、无关格式化

## 六、细则索引（[docs/CODING_STANDARDS.md](docs/CODING_STANDARDS.md)）

| 要查什么 | 小节 |
|---|---|
| 每层能写什么；构建配置继承链；时间/异常/值对象约定 | §1 分层职责 + 工程约定 |
| Command/Handler 模板；四个 Behavior 的顺序；领域事件何时派发 | §2 用例与管道 |
| N+1、分页、索引、广播循环、事务边界 | §3 数据访问与性能 |
| WS 协议契约、入站路由、多端语义 | §4 实时通信 |
| 测试命名与组织、替身约定、不测什么 | §5 测试规范 |
| 订阅清理、竞态、图片压缩、PWA | §6 前端规范 |
| 注释写什么；改哪些必须同步哪份文档 | §7 注释与文档同步 |
| 提交信息格式与 `BREAKING CHANGE` | §8 提交规范 |
| 本仓库真实踩过的坑（案例 + 教训） | §9 踩坑案例集 |
