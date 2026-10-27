# LHZ.OnlineChat 优化分析报告（第四轮）

> 基线：`dev` 分支 `b18642b`（语音消息提交之后）。前三份报告（`PROJECT_ANALYSIS.md`、`OPTIMIZATION_REVIEW.md`、
> `OPTIMIZATION_REVIEW_ROUND3.md`）不改，本篇只写三类内容：**本轮已修**、**本轮新发现但未修**、**旧报告里仍未修**。
> 优先级口径：安全与正确性 > 性能 > 工程；性能项按"小规模自部署"估价，不压过正确性问题。

## 一、验证口径

| 项 | 结果 |
|---|---|
| `dotnet build`（Release） | 0 警告 0 错误 |
| `dotnet test`（Release） | 1089 条全过（Domain 397 / Application 491 / Infrastructure 201） |
| `frontend/tests` `npm run test` | 222 条全过（本轮新增 43 条，其中 voice-race 与 store-races 共 15 条已用旧源码验证过会失败） |
| `lhz-onlinechat-web` `npm run build` | 通过（`vue-tsc && vite build`） |
| `admin-web` `npm run build` | **未跑**：本轮没有改动 admin-web |
| 限流行为 | **未运行验证**：Server 层没有集成测试，本机也没起 Postgres / Redis；只验证了编译与注册 |

## 二、本轮已修

### 2.1 图片 / 语音消息只接受站内地址（破坏性）

- 问题：`MessageContentRules.EnsureValid` 只校验"非空 + 长度"，type 1 / 5 的 `content` 可以是任意 URL。
  每个接收者的浏览器都会去请求发送者指定的外链（泄露 IP、已读状态，可当跟踪像素）。合并转发卡片里 `t=1/5` 的明细同样没校验。
- 修复：`Domain/Messaging/MessageValueObjects.cs` 新增 `EnsureLocalMedia`——图片必须是 `/uploads/images/<文件名>`，
  语音必须是 `/uploads/voices/<文件名>`（文件名只含字母数字与 `. _ -`，无子目录 / 查询串 / `..`）；
  `ForwardBundle` 对卡片明细调用同一规则。前端 `utils/media.ts#localMediaSrc` 作为第二道防线，
  不合法的地址显示"[图片不可用] / [语音不可用]"。
- 契约变化：之前能发外链图片的客户端现在会收到 `message_failed`。**只校验新发的消息，库里已有的不迁移**
  （历史聊天图片一直在 `/uploads/images/`，不受影响）。README 已同步。
- 测试口径变化（有意）：`WsContentGuardTests` 原用例把 `https://example.com/a.png` 当作合法图片内容。
  那是入口尚无站内地址约束时的口径，不是规范本身，现改为"外链被拒"。
- 已知耦合：Domain 里的 `/uploads/images/`、`/uploads/voices/` 是协议常量，与 `FileStorageOptions.PublicPrefix`（默认 `/uploads`）
  各存一份；`PublicPrefix` 目前没有任何配置入口覆盖。若将来开放该配置，两处要一起改。

### 2.2 限流补全

| 入口 | 阈值 | 分区 |
|---|---|---|
| `POST /api/auth/register`、`/forgot-password` | 20 次 / 10 分钟 | 来源 IP |
| `POST /api/auth/refresh` | 120 次 / 10 分钟 | 来源 IP |
| `POST /api/uploads/image`、`/uploads/voice`、`/api/auth/avatar` | 20 次 / 分钟 | 登录用户（三者共用计数） |
| `GET /api/messages/search` | 30 次 / 分钟 | 登录用户 |

- 中间件顺序改为 `UseAuthentication → UseRateLimiter → UseAuthorization`：按用户分区要读已验签的身份，
  之前限流在认证之前拿不到。匿名接口按 IP 分区，不受影响。
- 超出范围的一处顺带：头像上传（`/api/auth/avatar`）也套了 `upload` 策略，磁盘消耗形态相同，只是同一个属性。
- 阈值是代码常量，不是配置项，所以没有 `.env.example` / `compose` / `DEPLOY.md` 要同步；README 限流表已更新。
- **审计误报**：子审计说"搜索没有关键词长度上限"，实际 `SearchMessagesQuery.MaxKeywordLength = 50` 已在用例里截断，未改。

### 2.3 前端竞态与生命周期

| 问题 | 修复 | 位置 |
|---|---|---|
| 上传语音 / 图片期间切会话，消息发进新会话 | 目标会话与当前用户在 `await` 之前定下来；已不在原会话时不 emit `sent` | `MessageComposer.vue` |
| 授权弹窗期间重复点击 / 取消 / 卸载，漏掉麦克风流 | `starting` 标记 + 取消 / 卸载后事后拿到的流立即 `stop` | `useVoiceRecorder.ts` |
| `ChatLayout` 在 `await fetchUser()` 之后才注册监听，早卸载会泄漏 | `unmounted` 标记，await 之后检查 | `ChatLayout.vue` |
| 登出后在途的历史 / 会话 / 离线消息响应写进新账号的 store | store 内 `epoch`，`clearAll` 时 +1，响应回来代数不符就丢弃 | `stores/chat.ts` |
| 历史分页失败后 `hasMore=false`，再也翻不了 | 只有成功拿到该页才推进 `page` / `hasMore` | `stores/chat.ts` |
| 群成员列表被慢响应覆盖成上一个群 | 只认最后发起的那次请求 | `stores/group.ts` |
| 会话内搜索被旧关键词 / 上个会话的慢响应覆盖 | 关键词变化、换会话、关闭时 `seq++`，在途请求按代数丢弃 | `ChatSearchPanel.vue` |

测试：`voice-race.test.ts`（13）、`store-races.test.ts`（9）、`media.test.ts`（18）、
`message-bubble.test.ts` 补外链渲染。测试里的假 stream 改成带真实 `getTracks`，"轨道已关闭"才可能失败。
修一个既有夹具：`message-bubble.test.ts` 的图片地址原为 `/uploads/a.png`，不是真实上传路径，改为 `/uploads/images/a.png`。

## 三、本轮新发现、未修

| 级别 | 问题 | 位置 |
|---|---|---|
| P2 | 语音上传只校验扩展名，不检查文件头；`.mp4` 与视频重叠 | `UploadRules.EnsureValidVoice` |
| P2 | 上传没有磁盘总配额，也没有孤儿文件清理（单个上传卷） | `LocalFileStorage`、compose |
| P2 | 内容搜索会命中语音 / 图片消息的 URL 文本 | `SearchMessages` |
| P2 | 录音没有 `recorder.onerror`；60 秒上限靠 `setInterval` 计数，后台标签页会被限频，实际可能超过 60 秒 | `useVoiceRecorder.ts` |
| P2 | 旧 socket 的 `onclose` 可能影响新连接（`disconnect()` 后异步 close，未按当前 `ws` 校验） | `stores/websocket.ts` |
| P2 | `claimVoicePlayback` 的模块级 `current` 可能持有已卸载的 `<audio>`；每条语音都挂一个 `preload="metadata"` | `useVoicePlayback.ts`、`VoiceMessage.vue` |
| P2 | 图片 / 语音发送块与文字发送路径近似重复，应收进 `utils/messageSend.ts`；倒计时在 3 处各写一份 | `MessageComposer.vue`、`Register.vue` 等 |
| P2 | `docs/CODING_STANDARDS.md:174` 两条 bullet 缺换行，渲染错乱 | 文档 |
| P2 | 语音 URL 无鉴权直出（靠 GUID 文件名不可猜），未来要做会话级访问控制需要换方案 | `UseStaticFiles` |
| 测试 | `frontend/tests` 的 `typecheck` 在 `utils/markdown.ts` 报缺类型（测试工程没装 marked / dompurify / highlight.js 的类型解析），非本轮引入，也不在 AGENTS.md 要求的命令里 | 测试工程 |

## 四、旧报告里仍未修（已对着当前代码复核）

**后端 P1**
- `GetSessions`：每群一次 `CountAfterCursorAsync`（9+N 次查询）并多拉 500 行（`GetSessions.cs:70,131-132`）。
- 全部分页仍是 `Count` + `OFFSET`，没有 keyset（`PrivateMessageRepository` / `GroupMessageRepository`，`GetMessageHistory.cs:62`）。
- 已读标记与机器人令牌更新整行；`ManageRobots.cs:242` 在 GET 列表里循环写库。
- 仪表盘约 40 次串行 `await`，全仓没有 `Task.WhenAll`（`GetDashboard.cs`）。
- 撤回缓存逐条 `KeyDelete` + `LeftPush`（`RedisStores.cs:208-213`）。
- `TransactionBehavior` 包所有命令，`SendTypingCommand` 等也开事务；`SendPrivateMessage` 在事务内写缓存与推 WS。
- 缺索引：`User_.CreatedAt`、`Group_.CreatedAt`、各 `IsDeleted`、`AdminLog.Action`。
- 搜索与管理端消息查询在内存里合并分页（`SearchMessages.cs:157,234`）。

**后端 P2**
- 异步路径里的同步 DB 调用（`ExecuteDataTable` 包 `Task.FromResult`，`PrivateMessageRepository.cs:259` 等）。
- `EnsureDatabaseExists` 在 DI 注册阶段执行，冒烟测试做不了；测试项目无 `Mvc.Testing` / Testcontainers。
- 启动期临时 DDL，无 SchemaVersion 表；没有任何指标端点。
- 未处理异常 `rethrow`，生产返回空 500 而不是 `ApiResponse`（`Program.cs`）。
- compose：Redis 无 `maxmemory`，无 `mem_limit`、日志轮转；`frontend-admin` 无 healthcheck。
- 信任代理列表被清空（依赖后端端口不直接对外）；机器人限流分区键是原始令牌，分区数无上限。

**前端**
- P1：消息列表没有上限，每条消息一次拷贝 + 全量 `sort`，`ChatArea` 全量渲染（`chat.ts`、`ChatArea.vue`）。
- P2：`ChatSidebar` 行级无 `v-memo`；两个应用都没有 `manualChunks`；10 个弹窗静态引入；启动流程串行；
  每次切会话都重拉第 1 页；admin 路由守卫不检查 `superOnly`。
- P2：大文件在变大——`ChatSidebar.vue` 845 行、`ChatLayout.vue` 778 行（`handleWsMessage` 约 200 行 if 链）、
  `MessageComposer.vue` 731 行、`MessageBubble.vue` 599 行、`ChatArea.vue` 559 行、`chat.ts` 504 行。

## 五、建议下一步

1. 先把本轮四个提交合入并观察上传 / 搜索限流在真实使用下是否误伤（阈值是常量，改起来只有一处）。
2. 下一批优先做**后端 P1 里的 `GetSessions` 与 keyset 分页**——它们随会话数 / 消息量线性变差，且互相独立。
3. `TransactionBehavior` 的免事务标记与 DI 注册阶段副作用，是后面几乎所有测试 / 性能工作的前置，值得单独排一个提交。
