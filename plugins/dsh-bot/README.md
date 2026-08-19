# 🧠 DeepSeek Harness 机器人插件

把 LHZ.OnlineChat 的机器人变成 **DeepSeek Harness 任务入口**：用户在私聊或群聊 @ 机器人发送任务 → 系统把消息回调给本插件 → 插件调用本机 DeepSeek Harness（`dsh --profile headless` 单任务模式）执行 → 通过机器人推送接口把**过程通知**（「🧠 任务已提交，执行中…」）和**执行结果**通知用户。

- 零依赖：纯 Node.js 18+（http / crypto / child_process / fetch），无需 `npm install`
- 串行任务队列：同时只跑一个任务，排队的任务会收到「⏳ 已排队」通知
- 可选签名验签（回调 + 推送双向 HMAC-SHA256）
- 消息去重（防系统重试导致重复执行）
- 任务超时保护（默认 10 分钟，超时自动终止并通知）

## 前提

本机已安装并配置好 **DeepSeek Harness**（`dsh` 命令可用 / 或开发环境有仓库 checkout），并且：

- 生产安装：`dsh` 在 PATH 中，`DSH_HOME` 已初始化（`dsh --profile headless "hello"` 能直接跑通）
- 开发环境（本仓库 checkout）：用 `pnpm dsh --profile headless "..."` 在仓库根目录跑通一次

> 插件与 DSH 必须在同一台机器上（headless 是本地进程）。Windows 开发机可直接跑本插件；Linux 服务器同理。

## 快速接入

### 1. 创建机器人

1. 登录 OnlineChat → 侧边栏「我的机器人」→ 创建机器人
2. **Webhook 地址填：`http://host.docker.internal:9312/hook`**（OnlineChat 跑在 Docker 容器、插件跑在宿主机时）
   - 插件如果跑在容器内/同网络，则填对应地址
3. （可选）「签名密钥」填随机字符串，与插件 `BOT_SECRET` 保持一致
4. 复制调用链接，取令牌

### 2. 配置并启动插件

```bash
cd plugins/dsh-bot
cp .env.example .env
# 编辑 .env：
#   BOT_ORIGIN=http://localhost:8080     # 你的站点地址
#   BOT_ROBOT_TOKEN=<机器人令牌>
#   DSH_CMD=pnpm                         # 开发环境（deepseek-harness checkout）
#   DSH_ARGS=dsh --profile headless
#   DSH_CWD=D:/Study/deepseek-harness    # 开发环境必填（仓库根目录）；生产 dsh 在 PATH 可不配
#   DSH_WORKDIR=D:/Study/dsh-bot-workspace  # 任务工作区（agent 可写目录，务必隔离）
node bot.mjs
```

看到 `✅ 插件已启动` 即成功。

### 3. 测试

- **私聊**：给机器人发 `用一句话总结 TCP 三次握手` → 先收到「🧠 任务已提交…」，Harness 执行完成后收到「✅ 执行完成」+ 结果
- **群聊**：把机器人拉进群（群成员 → 添加机器人），群里 **@机器人** 发送任务
- 连续发多个任务：后面的任务会收到「⏳ 已排队（第 N 个）」，按顺序执行

## 执行过程与结果通知

| 阶段 | 用户看到的 |
|---|---|
| 任务提交 | `🧠 任务已提交给 DeepSeek Harness，执行中…` + 任务预览（可配 `BOT_START_NOTICE=false` 关闭） |
| 排队中 | `⏳ 前一个任务还在执行中，你的任务已排队（第 N 个）` |
| 执行完成 | `✅ DeepSeek Harness 执行完成（耗时）` + Harness 最终回答（自动引用原消息） |
| 执行失败/超时 | `⚠️ DeepSeek Harness 执行失败（原因/错误输出尾部）` |

## 配置项

| 变量 | 默认 | 说明 |
|---|---|---|
| `BOT_ORIGIN` | `http://localhost:8080` | 站点地址（推送用） |
| `BOT_ROBOT_TOKEN` | 空 | 机器人推送令牌（必填） |
| `BOT_ROBOT_TOKENS` | 空 | 多机器人映射 `账号ID:令牌,...` |
| `BOT_SECRET` | 空 | 签名密钥（回调验签 + 推送签名） |
| `DSH_CMD` | `dsh` | dsh 命令 |
| `DSH_ARGS` | `--profile headless` | 固定参数（空格分隔） |
| `DSH_CWD` | 工作目录 | dsh 命令执行目录（开发环境必填仓库根目录） |
| `DSH_WORKDIR` | `workspace/` | 任务工作区（agent 可写） |
| `DSH_TIMEOUT_MS` | `600000` | 单任务超时（毫秒） |
| `BOT_START_NOTICE` | `true` | 任务提交时推送开始通知 |
| `BOT_RESULT_MAX` | `4000` | 结果推送长度上限（服务端单条 5000 字） |
| `BOT_PORT` | `9312` | 监听端口 |

## 工作原理

```
用户 → 私聊 / 群聊 @机器人 发送任务
        │ (WS 消息)
        ▼
OnlineChat 服务端 → POST /hook (X-Bot-Signature 验签) → 本插件
        │                                            │ 立即 200（异步）
        │                                            ▼
        │                                dsh --profile headless "<任务>"
        │                                （本机 Harness 执行，串行队列）
        │                                            │
        └─────────── POST /api/robots/{令牌}/reply ◄─┘
            （🧠 已提交 → ⏳ 排队 → ✅/⚠️ 结果）
```

## 安全提示

- `DSH_WORKDIR` 是 agent 的可写工作区：**务必指向专用目录**，不要指向生产数据目录（headless agent 可在该目录内自由读写）
- 生产环境建议配置 `BOT_SECRET`（双向验签）并限制机器人只对自己/可信用户开放
- 群聊机器人可被群内任何人 @ 触发，任务会消耗 Harness 的模型调用额度
