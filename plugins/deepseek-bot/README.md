# 🤖 DeepSeek 机器人插件

把 LHZ.OnlineChat 的机器人变成 **DeepSeek AI 对话助手**：用户在私聊或群聊 @ 机器人 → 系统把消息回调给本插件 → 插件调用 DeepSeek API → 通过机器人推送接口把**过程通知**（「🤔 正在思考…」）和**最终结果**回复给用户。

- 零依赖：纯 Node.js 18+（http / crypto / fetch），无需 `npm install`
- 可选签名验签（回调 + 推送双向 HMAC-SHA256）
- 可选对话记忆（每会话最近 N 轮，进程内存）
- 消息去重（防系统重试导致重复回复）
- 已 Docker 化：`docker-compose.yml` 内置 `deepseek-bot` 服务，一条命令拉起

## 快速接入（方式一：Docker Compose，推荐）

### 1. 配置主仓库 `.env`

```bash
cp .env.example .env
# 编辑 .env 增加：
#   DEEPSEEK_API_KEY=sk-xxxx          # DeepSeek Key（留空为模拟模式）
#   BOT_ROBOT_TOKEN=<机器人令牌>       # 见第 2 步
#   BOT_SECRET=<签名密钥>              # 可选，与机器人配置的 WebhookSecret 一致
```

### 2. 创建机器人

1. 登录 OnlineChat → 侧边栏「我的机器人」→ 创建机器人
2. **Webhook 地址填：`http://deepseek-bot:9311/hook`**（compose 内服务名，backend 容器可直接访问）
3. （可选）「签名密钥」填一个随机字符串，与 `.env` 的 `BOT_SECRET` 保持一致
4. 创建后点「复制调用链接」，里面的令牌填入 `.env` 的 `BOT_ROBOT_TOKEN`

### 3. 启动

```bash
docker compose up -d --build deepseek-bot
docker compose logs -f deepseek-bot   # 查看插件日志
```

### 4. 测试

- **私聊**：直接给机器人发消息 → 先回「🤔 已收到，正在思考…」→ 随后回复 DeepSeek 结果
- **群聊**：群成员 → 添加机器人，群里 **@机器人** 说话 → 机器人回复到群里

## 快速接入（方式二：宿主机直接运行）

```bash
cd plugins/deepseek-bot
cp .env.example .env
# 编辑 .env：
#   DEEPSEEK_API_KEY=sk-xxxx          # DeepSeek Key（留空为模拟模式）
#   BOT_ORIGIN=http://localhost:8080  # 你的站点地址（生产改成 https://chat.onlinemusic.top）
#   BOT_ROBOT_TOKEN=<机器人令牌>
#   BOT_SECRET=<签名密钥>              # 可选
node bot.mjs
```

- 机器人 WebhookUrl 填 `http://host.docker.internal:9311/hook`（插件跑在宿主机，容器内访问宿主）
- 看到 `✅ 插件已启动` 即成功

## 执行过程与结果通知

| 阶段 | 用户看到的 |
|---|---|
| 收到消息 | `🤔 已收到，正在思考…`（可配 `BOT_THINKING_NOTICE=false` 关闭） |
| DeepSeek 完成 | 机器人把结果回复到会话（自动引用原消息） |
| 调用失败 | `⚠️ DeepSeek 调用失败：<原因>` |

## 配置项

| 变量 | 默认 | 说明 |
|---|---|---|
| `DEEPSEEK_API_KEY` | 空 | DeepSeek Key；留空 = 模拟模式 |
| `DEEPSEEK_BASE_URL` | `https://api.deepseek.com` | API 地址 |
| `DEEPSEEK_MODEL` | `deepseek-chat` | 模型名 |
| `BOT_ORIGIN` | `http://localhost:8080` | 站点地址（推送用） |
| `BOT_ROBOT_TOKEN` | 空 | 机器人推送令牌（必填） |
| `BOT_ROBOT_TOKENS` | 空 | 多机器人映射 `账号ID:令牌,...`，未匹配回退 `BOT_ROBOT_TOKEN` |
| `BOT_SECRET` | 空 | 签名密钥（回调验签 + 推送签名；与机器人配置的 WebhookSecret 一致） |
| `BOT_THINKING_NOTICE` | `true` | 是否先推送「正在思考…」 |
| `BOT_MEMORY` | `true` | 对话记忆开关 |
| `BOT_MEMORY_ROUNDS` | `10` | 每会话记忆轮数 |
| `BOT_TIMEOUT_MS` | `60000` | DeepSeek 调用超时 |
| `BOT_PORT` | `9311` | 监听端口 |

## 工作原理

```
用户 → 私聊/群聊 @机器人
        │ (WS 消息)
        ▼
OnlineChat 服务端 → POST /hook (X-Bot-Signature 验签) → 插件
        │                                             │ 立即 200（异步）
        │                                             ▼
        │                                    调 DeepSeek chat/completions
        │                                             │
        └──────────── POST /api/robots/{令牌}/reply ◄─┘
             （过程通知 + 结果，自动引用原消息）
```

回调事件格式与推送接口详见主仓库 `README.md`「机器人接入」章节。
