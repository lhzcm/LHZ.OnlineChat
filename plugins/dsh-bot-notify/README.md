# 🧠 dsh-bot-notify — DeepSeek Harness → OnlineChat 机器人推送插件

安装在 **DeepSeek Harness 客户端**里的插件：监听 Harness 会话事件，把任务的**执行过程**与**执行结果**通过 OnlineChat 机器人主动推送接口通知用户。

```
用户在 Harness 发起任务
        │
        ▼
dsh-bot-notify（本插件，监听 session/event）
        │  🧠 收到任务，开始执行…（任务内容）
        │  🔧 正在调用工具 xxx（可选）
        │  ✅ 执行结果（turn/step + 回复文本）
        │  ⚠️ 执行结束：<原因>（异常时）
        ▼
POST {pushUrl}  ← 机器人主动推送链接（/api/robots/{令牌}/reply）
        │
        ▼
用户私聊/群里收到机器人通知
```

## 安装（3 步）

### 1. 准备机器人

1. 登录 OnlineChat → 侧边栏「我的机器人」→ 创建机器人
2. 复制「调用链接」：`POST http(s)://站点/api/robots/{令牌}/reply` → 得到 `pushUrl`
3. （可选）给机器人配置「签名密钥」（WebhookSecret）→ 同时用于 `pushSecret`

### 2. 把插件装进 Harness profile

```bash
# 在 DSH 客户端机器上（DSH 已初始化）
dsh plugin --profile web add file:D:/Study/dotnet/LHZ.OnlineChat/plugins/dsh-bot-notify
# （--profile 换成你的 profile 名；Linux 用 file:/path/to/plugins/dsh-bot-notify）
```

### 3. 在 profile 的 cordis.patch.yml 注册插件并配置

编辑 `$DSH_HOME/profiles/<name>/cordis.patch.yml`，追加：

```yaml
- insert:
    - id: bot-notify
      name: dsh-bot-notify
      config:
        pushUrl: 'http://localhost:8080/api/robots/你的机器人令牌/reply'
        pushSecret: ''            # 机器人的签名密钥（配置了才双向签名）
        sessionType: 'private'    # private=发给创建者本人 / group=发到群
        sessionId: 10001          # 接收方账号 ID（或群 ID）
        notifyStart: true         # 🧠 任务开始通知
        notifyResult: true        # ✅ 执行结果通知
        notifyError: true         # ⚠️ 异常结束通知
        notifyTools: false        # 🔧 工具调用过程（开=true，配合 toolMinIntervalMs 防刷屏）
        toolMinIntervalMs: 5000   # 工具过程通知最小间隔
        maxText: 4000             # 推送内容截断长度（服务端单条上限 5000）
```

> 提示：`sessionType: private` 时机器人只会发给它的创建者（好友关系限制），即机器人本人；`sessionType: group` 需要机器人已加入该群（群成员 → 添加机器人），并填群 ID。

### 4. 重启 Harness

```bash
dsh web            # 或重启你的 Harness 进程
```

日志出现 `[dsh-bot-notify] 已启用 → private#10001` 即生效。

## 通知格式

| 事件 | 机器人推送内容 |
|---|---|
| 任务开始 | `🧠 DeepSeek Harness 收到任务，开始执行…` + 任务内容 |
| 工具调用（可选） | `🔧 正在调用工具：<工具名>` |
| 执行结果 | `✅ DeepSeek Harness 执行结果（turn 1 · step 2）` + 回复文本 |
| 异常结束 | `⚠️ DeepSeek Harness 执行结束：<原因>`（含错误信息） |

## 配置项

| 字段 | 默认 | 说明 |
|---|---|---|
| `pushUrl` | 空（必填） | 机器人主动推送链接 `/api/robots/{令牌}/reply` |
| `pushSecret` | 空 | 机器人的签名密钥；配置后推送带 `X-Bot-Signature` |
| `sessionType` | `private` | 推送目标会话类型 `private`/`group` |
| `sessionId` | 0（必填） | 接收方账号 ID 或群 ID |
| `notifyStart` | `true` | 任务开始通知 |
| `notifyResult` | `true` | 执行结果通知（每条 assistant 消息） |
| `notifyError` | `true` | 异常结束通知 |
| `notifyTools` | `false` | 工具调用过程通知 |
| `toolMinIntervalMs` | `5000` | 工具通知最小间隔（防刷屏） |
| `maxText` | `4000` | 推送内容截断长度 |

## 注意事项

- 插件只做**单向通知**（Harness → 机器人），不接收机器人回调；需要"在聊天里给 Harness 发任务"请搭配 OnlineChat 仓库的 `plugins/dsh-bot`（机器人 → Harness 方向）
- 推送失败只打印警告日志，不影响 Harness 本体
- 多会话并行时按会话独立跟踪状态，各会话互不干扰
