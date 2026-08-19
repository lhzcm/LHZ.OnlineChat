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
 * 安装：dsh plugin --profile <name> add file:<本目录绝对路径>
 * 配置：在 profile 的 cordis.patch.yml 中 insert 插件行并填写 config（见 README）
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
  // 通知开关
  notifyStart: true,   // 🧠 任务开始
  notifyResult: true,  // ✅ 执行结果（每条 assistant/message）
  notifyError: true,   // ⚠️ 异常结束
  notifyTools: false,  // 🔧 工具调用过程
  // 工具过程通知的最小间隔（毫秒），防刷屏
  toolMinIntervalMs: 5000,
  // 推送内容截断长度（服务端单条消息上限 5000 字）
  maxText: 4000
}

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
  return text.slice(0, max) + '\n\n…（内容过长已截断）'
}

export function apply(ctx, config) {
  const cfg = { ...DEFAULTS, ...(config || {}) }

  if (!cfg.pushUrl || !cfg.sessionId) {
    console.warn('[dsh-bot-notify] 未配置 pushUrl / sessionId，插件不会推送任何通知')
    return
  }

  const hmacHex = (secret, body) =>
    createHmac('sha256', secret).update(body).digest('hex')

  /** 未完成的推送（headless 单任务进程退出前 flush，避免结果丢失） */
  const pending = new Set()
  process.on('beforeExit', () => {
    if (pending.size > 0) {
      Promise.allSettled([...pending]).finally(() => process.exit(0))
    }
  })

  /** 通过机器人推送一条消息（失败仅告警，不干扰 Harness 本身） */
  function push(content) {
    const p = (async () => {
      try {
        const body = JSON.stringify({
          sessionType: cfg.sessionType,
          sessionId: cfg.sessionId,
          content,
          replyTo: null
        })
        const headers = { 'Content-Type': 'application/json' }
        if (cfg.pushSecret) headers['X-Bot-Signature'] = hmacHex(cfg.pushSecret, body)
        const res = await fetch(cfg.pushUrl, { method: 'POST', headers, body })
        if (!res.ok) {
          console.warn(`[dsh-bot-notify] 推送失败 ${res.status}: ${(await res.text().catch(() => '')).slice(0, 150)}`)
        }
      } catch (e) {
        console.warn(`[dsh-bot-notify] 推送异常: ${e.message}`)
      }
    })()
    pending.add(p)
    p.finally(() => pending.delete(p)).catch(() => {})
    return p
  }

  /** 会话级状态：turn 记录 + 工具通知节流 */
  const sessions = new Map()

  ctx.on('session/event', (_session, event) => {
    const data = event?.data
    if (!data) return
    const sid = String(_session)
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

  console.log(`[dsh-bot-notify] 已启用 → ${cfg.sessionType}#${cfg.sessionId}（开始=${cfg.notifyStart} 结果=${cfg.notifyResult} 异常=${cfg.notifyError} 工具=${cfg.notifyTools}）`)
}
