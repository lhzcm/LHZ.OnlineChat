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

## 安装

> 版本要求：按**组合包（bundle）**安装需要 DSH ≥ 0.2（`dsh.bundle` 机制）。插件本身只用 `node:crypto` 与全局 `fetch`，无第三方依赖。

### 1. 准备机器人

1. 登录 OnlineChat → 侧边栏「我的机器人」→ 创建机器人
2. 复制「调用链接」：`POST http(s)://站点/api/robots/{令牌}/reply` → 得到 `pushUrl`
3. （可选）给机器人配置「签名密钥」（WebhookSecret）→ 同时用于 `pushSecret`

### 2. 安装组合包（会自动启用插件）

```bash
# 在 DSH 客户端机器上（DSH 已初始化）
dsh plugin --profile web add file:D:/Study/dotnet/LHZ.OnlineChat/plugins/dsh-bot-notify
# （--profile 换成你的 profile 名；Linux 用 file:/path/to/plugins/dsh-bot-notify）
```

本包声明了 `dsh.bundle`，所以 `dsh` 会把它记进 `$DSH_HOME/profiles/<name>/package.json` 的 `dsh.profile.bundles`，并应用包内 `cordis.patch.yml`——**插件行由组合包自动插入**，不需要再手写 `insert`。

装完可以先用不启动的方式确认这一层进去了：

```bash
dsh --profile web --dump-config | Select-String 'dsh-bot-notify'   # Linux/macOS 换成 | grep dsh-bot-notify
```

应能看到 `# == dsh-bot-notify` 这一层；若安装时提示 `declares no dsh.bundle`，说明拿到的是 0.1.x 的旧包。

### 3. 填配置（覆盖组合包自带的那一行）

编辑 `$DSH_HOME/profiles/<name>/cordis.patch.yml`，用**相同 `id`** 写一行覆盖组合包的行（后应用的层整行覆盖，`config` 是整体替换、不深合并；没写的键用插件默认值）：

```yaml
- insert:
    - id: bot-notify
      name: dsh-bot-notify
      config:
        pushUrl: 'http://localhost:8080/api/robots/你的机器人令牌/reply'
        pushSecret: ''            # 机器人的签名密钥（配置了才带 X-Bot-Signature）
        sessionId: 10001          # 接收方账号 ID（或群 ID）
        sessionType: 'private'    # private=发给创建者本人 / group=发到群
```

其余键（`notifyStart` / `notifyResult` / `notifyError` / `notifyTools` / `toolMinIntervalMs` / `maxText` / `pushTimeoutMs`）不写就取默认值，见下方配置项表。

> 提示：`sessionType: private` 时机器人只会发给它的创建者（好友关系限制），即机器人本人；`sessionType: group` 需要机器人已加入该群（群成员 → 添加机器人），并填群 ID。

### 4. 重启 Harness

```bash
dsh web            # 或重启你的 Harness 进程
```

日志出现 `[dsh-bot-notify] 已启用 → private#10001` 即生效；若出现「尚未配置 pushUrl / sessionId」，说明第 3 步的覆盖行还没生效。

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
| `maxText` | `4000` | 推送内容截断长度（含截断提示，服务端单条上限 5000） |
| `pushTimeoutMs` | `8000` | 单次推送超时；也是进程退出前等待在途推送的上限 |

## 注意事项

- 插件只做**单向通知**（Harness → 机器人），不接收机器人回调；签名同样是单向的，只在插件发出的 HTTP 请求上带 `X-Bot-Signature`。「在聊天里给 Harness 发任务」是反方向的功能，本仓库目前没有对应插件
- 推送失败只打印警告日志，不影响 Harness 本体；日志会隐去 `pushUrl` 里的机器人令牌
- 多会话并行时按会话 `id` 独立跟踪状态，各会话互不干扰；会话销毁后其状态会被释放
- 配置没填（`pushUrl` / `sessionId` 为空）时插件**只告警不抛错**，不会拦住 Harness 启动
