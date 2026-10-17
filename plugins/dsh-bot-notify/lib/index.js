/**
 * dsh-bot-notify — DeepSeek Harness 机器人推送通知插件
 *
 * 监听 Harness 会话事件，把任务的「执行过程」与「执行结果」通过
 * OnlineChat 机器人主动推送接口（POST /api/robots/{令牌}/reply）通知用户。
 *
 * 事件 → 推送：
 *   turn/start + user/message → 🧠 收到任务，开始执行…（任务内容）
 *   tool/call                → 🔧 正在调用工具 xxx（可配置，带最小间隔防刷屏）
 *   assistant/message        → ✅ 执行结果（turn/step + 回复文本）
 *   turn/end（非 completed）  → ⚠️ 执行结束：<原因>
 *
 * 安装（DSH 组合包）：dsh plugin --profile <name> add file:<本目录绝对路径>
 *   —— 本包在 package.json 里声明了 dsh.bundle，dsh 会把它记进 profile 的
 *      dsh.profile.bundles 并应用本包的 cordis.patch.yml，插件随之启用。
 * 配置：在 $DSH_HOME/profiles/<name>/cordis.patch.yml 里按 id: bot-notify 覆盖
 *   本包自带的那一行（键与默认值见 cordis.patch.yml 的注释模板与 README）。
 */
import { createHmac } from 'node:crypto'

export const name = 'dsh-bot-notify'

const DEFAULTS = {
  // 机器人主动推送链接（机器人管理面板「复制调用链接」）必填
  pushUrl: '',
  // 机器人的签名密钥（WebhookSecret）；配置了才带 X-Bot-Signature
  pushSecret: '',
  // 推送目标会话：private=接收方账号 ID / group=群 ID（机器人需在该群）
  sessionType: 'private',
  sessionId: 0,
  // 消息类型：3=Markdown（前端按文档块渲染，保留标题/列表/代码块），0=纯文本
  // 服务端目前只接受 0 / 3（图片 1 机器人不允许），填别的会被拒收并回 message_failed
  messageType: 3,
  // 通知开关
  notifyStart: true,   // 🧠 任务开始
  notifyResult: true,  // ✅ 执行结果（每条 assistant/message）
  notifyError: true,   // ⚠️ 异常结束
  notifyTools: false,  // 🔧 工具调用过程
  // 工具过程通知的最小间隔（毫秒），防刷屏
  toolMinIntervalMs: 5000,
  // 推送内容截断长度。服务端上限已放宽到 50000 字，这里仍截到 4000：
  // 通知只是摘要，把整段长回复塞进推送既刷屏又要多等一次网络往返
  maxText: 4000,
  // 单次推送的超时（毫秒）：headless 单任务进程退出前要能把在途推送了结
  pushTimeoutMs: 8000
}

/** 截断时追加的提示；长度要从上限里扣掉，否则总长会超出 maxText */
const TRUNCATED_SUFFIX = '\n\n…（内容过长已截断）'

/** 从 ContentBlock[] 提取纯文本 */
function textOf(blocks) {
  if (!Array.isArray(blocks)) return ''
  return blocks
    .filter(b => b && b.type === 'text')
    .map(b => b.text || '')
    .join('')
}

function truncate(text, max) {
  if (text.length <= max) return text
  return text.slice(0, Math.max(0, max - TRUNCATED_SUFFIX.length)) + TRUNCATED_SUFFIX
}

/**
 * 会话标识。session/event 与 session/disposed 传进来的是带 id 的 Session 实例；
 * 早前把对象本身字符串化会让所有会话共用一份状态（多会话下 turn 与节流互相串）。
 */
function sessionIdOf(session) {
  const id = session?.id
  return id === undefined || id === null ? String(session) : String(id)
}

/** 日志里不能出现带机器人令牌的 pushUrl */
function describeError(error) {
  const message = typeof error?.message === 'string' ? error.message : String(error)
  return message.replace(/\/api\/robots\/[^/\s]+/g, '/api/robots/***')
}

export function apply(ctx, config) {
  const cfg = { ...DEFAULTS, ...(config || {}) }

  if (!cfg.pushUrl || !cfg.sessionId) {
    // 不抛异常：插件是可选的通知旁路，配置没填不应该拦住 Harness 启动
    console.warn(
      '[dsh-bot-notify] 尚未配置 pushUrl / sessionId，插件已加载但不会推送通知。'
      + '请在 $DSH_HOME/profiles/<profile>/cordis.patch.yml 里覆盖 id 为 bot-notify 的那一行')
    return
  }

  const hmacHex = (secret, body) =>
    createHmac('sha256', secret).update(body).digest('hex')

  /** 未完成的推送（headless 单任务进程退出前 flush，避免结果丢失） */
  const pending = new Set()

  /**
   * 等未完成的推送收尾，最多等 timeoutMs。
   * 不调用 process.exit：那会覆盖任务的真实退出码，也会打断 Harness 自己的收尾逻辑。
   */
  async function flushPending(timeoutMs) {
    if (pending.size === 0) return
    let timer
    const deadline = new Promise(resolve => { timer = setTimeout(resolve, timeoutMs); timer.unref() })
    try {
      await Promise.race([Promise.allSettled([...pending]), deadline])
    } finally {
      clearTimeout(timer)
    }
  }

  // 进程监听属于插件资源，交给 ctx.effect 在卸载时摘掉
  ctx.effect(() => {
    const onBeforeExit = () => { void flushPending(cfg.pushTimeoutMs).catch(() => {}) }
    process.on('beforeExit', onBeforeExit)
    return () => process.off('beforeExit', onBeforeExit)
  })

  /** 通过机器人推送一条消息（失败仅告警，不干扰 Harness 本身） */
  function push(content) {
    const p = (async () => {
      try {
        const body = JSON.stringify({
          sessionType: cfg.sessionType,
          sessionId: cfg.sessionId,
          content,
          // 默认 3（Markdown）：通知正文里的标题/列表/代码块会被前端按文档块渲染；
          // 想退回纯文本把它配成 0
          messageType: cfg.messageType,
          replyTo: null
        })
        const headers = { 'Content-Type': 'application/json' }
        if (cfg.pushSecret) headers['X-Bot-Signature'] = hmacHex(cfg.pushSecret, body)
        const res = await fetch(cfg.pushUrl, {
          method: 'POST',
          headers,
          body,
          signal: AbortSignal.timeout(cfg.pushTimeoutMs)
        })
        if (!res.ok) {
          console.warn(`[dsh-bot-notify] 推送失败 ${res.status}: ${(await res.text().catch(() => '')).slice(0, 150)}`)
        }
      } catch (e) {
        console.warn(`[dsh-bot-notify] 推送异常: ${describeError(e)}`)
      }
    })()
    pending.add(p)
    p.finally(() => pending.delete(p)).catch(() => {})
    return p
  }

  /** 会话级状态：turn 记录 + 工具通知节流 */
  const sessions = new Map()

  ctx.on('session/event', (session, event) => {
    const data = event?.data
    if (!data) return
    const sid = sessionIdOf(session)
    let st = sessions.get(sid)
    if (!st) {
      st = { pendingStartTurn: null, notifiedStart: false, lastToolAt: 0, turns: new Map() }
      sessions.set(sid, st)
    }

    switch (event.type) {
      case 'turn/start': {
        // 下一批用户消息视为该 turn 的任务内容
        st.pendingStartTurn = data.turn
        st.notifiedStart = false
        break
      }

      case 'user/message': {
        // turn 开始后的第一条用户消息 → 开始通知（含任务内容）
        if (cfg.notifyStart && st.pendingStartTurn != null && !st.notifiedStart) {
          st.notifiedStart = true
          const task = truncate(textOf(data.content), 300)
          void push(`🧠 DeepSeek Harness 收到任务，开始执行…\n\n📋 任务：${task || '（无文本内容）'}`)
        }
        break
      }

      case 'tool/call': {
        if (cfg.notifyTools) {
          const now = Date.now()
          if (now - st.lastToolAt >= cfg.toolMinIntervalMs) {
            st.lastToolAt = now
            void push(`🔧 正在调用工具：${data.name}`)
          }
        }
        break
      }

      case 'assistant/message': {
        if (cfg.notifyResult) {
          const text = truncate(textOf(data.message?.content), cfg.maxText)
          if (text) {
            void push(`✅ DeepSeek Harness 执行结果（turn ${data.turn} · step ${data.step}）\n\n${text}`)
          }
        }
        break
      }

      case 'turn/end': {
        const reason = data.reason?.kind || 'unknown'
        if (cfg.notifyError && reason !== 'completed') {
          const detail = data.reason?.error?.message
            ? `\n\n${truncate(String(data.reason.error.message), 800)}`
            : ''
          void push(`⚠️ DeepSeek Harness 执行结束：${reason}${detail}`)
        }
        st.pendingStartTurn = null
        break
      }
    }
  })

  // 会话销毁后不再需要它的状态，避免长驻进程里 Map 只增不减
  ctx.on('session/disposed', (session) => {
    sessions.delete(sessionIdOf(session))
  })

  console.log(`[dsh-bot-notify] 已启用 → ${cfg.sessionType}#${cfg.sessionId}（开始=${cfg.notifyStart} 结果=${cfg.notifyResult} 异常=${cfg.notifyError} 工具=${cfg.notifyTools}）`)
}
