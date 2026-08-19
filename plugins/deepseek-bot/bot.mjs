#!/usr/bin/env node
/**
 * DeepSeek 机器人插件
 *
 * 把 LHZ.OnlineChat 机器人变成 DeepSeek AI 对话助手：
 *   用户在私聊/群聊 @ 机器人 → 系统回调本插件 → 调 DeepSeek API
 *   → 通过机器人推送接口把「过程通知」和「结果」回复给用户。
 *
 * 运行：node bot.mjs（或 BOT_PORT=9311 node bot.mjs）
 * 配置见 .env.example（也支持环境变量直接传入）。
 *
 * 零依赖：仅用 Node 18+ 内置能力（http / crypto / fetch）。
 */
import http from 'node:http'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

// ==================== 配置加载 ====================
const __dirname = path.dirname(fileURLToPath(import.meta.url))
// 读取 .env（简单解析，不依赖第三方库）
if (fs.existsSync(path.join(__dirname, '.env'))) {
  for (const line of fs.readFileSync(path.join(__dirname, '.env'), 'utf8').split(/\r?\n/)) {
    const m = line.match(/^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)\s*$/)
    if (m && !process.env[m[1]]) process.env[m[1]] = m[2].replace(/^["']|["']$/g, '')
  }
}

const CFG = {
  port: Number(process.env.BOT_PORT || 9311),
  // DeepSeek API Key；未配置时进入模拟模式（便于无 Key 联调）
  apiKey: process.env.DEEPSEEK_API_KEY || '',
  baseUrl: process.env.DEEPSEEK_BASE_URL || 'https://api.deepseek.com',
  model: process.env.DEEPSEEK_MODEL || 'deepseek-chat',
  // 站点地址（推送机器人回复用）
  origin: (process.env.BOT_ORIGIN || 'http://localhost:8080').replace(/\/+$/, ''),
  // 机器人的推送令牌（机器人管理面板「复制调用链接」里的令牌）
  robotToken: process.env.BOT_ROBOT_TOKEN || '',
  // 多机器人映射（可选）："机器人账号ID:令牌,账号ID:令牌"
  robotTokens: Object.fromEntries(
    (process.env.BOT_ROBOT_TOKENS || '')
      .split(',').map(s => s.trim()).filter(Boolean)
      .map(s => { const [uid, tok] = s.split(':'); return [uid, tok] })
  ),
  // 机器人配置的「签名密钥」（WebhookSecret）：
  // 配置后，回调验签 + 推送签名都启用；不配置则两者都不启用
  secret: process.env.BOT_SECRET || '',
  // 收到消息时先推送「正在思考…」（过程通知）
  thinkingNotice: (process.env.BOT_THINKING_NOTICE || 'true') !== 'false',
  // 对话记忆：按会话保留最近 N 轮
  memory: (process.env.BOT_MEMORY || 'true') !== 'false',
  memoryRounds: Number(process.env.BOT_MEMORY_ROUNDS || 10),
  // 单次 DeepSeek 调用超时（秒）
  timeoutMs: Number(process.env.BOT_TIMEOUT_MS || 60000)
}

const isMock = !CFG.apiKey
console.log('==========================================')
console.log('🤖 DeepSeek Bot Plugin')
console.log(`   监听端口   : ${CFG.port}`)
console.log(`   站点地址   : ${CFG.origin}`)
console.log(`   模型       : ${CFG.model}${isMock ? '  (模拟模式：未配置 DEEPSEEK_API_KEY)' : ''}`)
console.log(`   签名密钥   : ${CFG.secret ? '已启用（回调验签 + 推送签名）' : '未启用'}`)
console.log(`   过程通知   : ${CFG.thinkingNotice ? '开启（先推送“正在思考…”）' : '关闭'}`)
console.log(`   对话记忆   : ${CFG.memory ? `开启（每会话最近 ${CFG.memoryRounds} 轮）` : '关闭'}`)
console.log(`   Webhook 地址: http://0.0.0.0:${CFG.port}/hook`)
console.log('==========================================')

// ==================== 去重与记忆 ====================
/** 已处理消息 ID（5 分钟 TTL，上限 1000），防止重复回调 */
const processed = new Map()
/** 对话记忆：sessionKey → [{role, content}]（LRU，上限 200 会话） */
const memories = new Map()
const MEMORY_TTL_MS = 30 * 60 * 1000

function isDuplicate(messageId) {
  if (!messageId) return false
  const now = Date.now()
  for (const [id, t] of processed) if (now - t > 5 * 60 * 1000) processed.delete(id)
  if (processed.has(messageId)) return true
  processed.set(messageId, now)
  if (processed.size > 1000) {
    const oldest = processed.keys().next().value
    processed.delete(oldest)
  }
  return false
}

function getHistory(sessionKey) {
  if (!CFG.memory) return []
  const m = memories.get(sessionKey)
  if (!m) return []
  if (Date.now() - m.at > MEMORY_TTL_MS) { memories.delete(sessionKey); return [] }
  return m.msgs
}

function pushHistory(sessionKey, role, content) {
  if (!CFG.memory) return
  let m = memories.get(sessionKey)
  if (!m || Date.now() - m.at > MEMORY_TTL_MS) m = { at: Date.now(), msgs: [] }
  m.msgs.push({ role, content })
  const max = CFG.memoryRounds * 2
  if (m.msgs.length > max) m.msgs = m.msgs.slice(-max)
  m.at = Date.now()
  memories.set(sessionKey, m)
  if (memories.size > 200) {
    const oldest = memories.keys().next().value
    memories.delete(oldest)
  }
}

// ==================== 签名 ====================
function hmacHex(secret, body) {
  return crypto.createHmac('sha256', secret).update(body).digest('hex')
}

function verifySignature(rawBody, signature) {
  if (!CFG.secret) return true // 未配置密钥：不验签（系统也只在校验配置了密钥时签名）
  if (!signature) return false
  const expected = hmacHex(CFG.secret, rawBody)
  // 常量时间比较
  const a = Buffer.from(expected), b = Buffer.from(signature)
  return a.length === b.length && crypto.timingSafeEqual(a, b)
}

// ==================== DeepSeek 调用 ====================
async function askDeepSeek(content, history) {
  if (isMock) {
    // 模拟模式：回显用户输入，方便无 Key 联调
    await new Promise(r => setTimeout(r, 300))
    return `（模拟回复，未配置 DEEPSEEK_API_KEY）你说的是：${content}`
  }
  const system = [
    '你是 OnlineChat 里一个叫机器人的 AI 助手（由 DeepSeek 驱动）。',
    '回答要简洁、友好，使用与用户相同的语言（默认中文）。',
    '群聊中如果用户 @ 了你，直接回答问题即可，不要重复 @ 对方。',
    '如果用户的问题与聊天内容无关（如询问你的身份），如实说明你是 DeepSeek 驱动的机器人。'
  ].join(' ')
  const messages = [{ role: 'system', content: system }]
  for (const m of history) messages.push(m)
  messages.push({ role: 'user', content })

  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), CFG.timeoutMs)
  try {
    const res = await fetch(`${CFG.baseUrl}/chat/completions`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${CFG.apiKey}`
      },
      body: JSON.stringify({ model: CFG.model, messages, stream: false }),
      signal: controller.signal
    })
    if (!res.ok) {
      const detail = (await res.text().catch(() => '')).slice(0, 200)
      throw new Error(`DeepSeek API ${res.status}: ${detail}`)
    }
    const data = await res.json()
    const reply = data?.choices?.[0]?.message?.content
    if (!reply) throw new Error('DeepSeek 返回内容为空')
    return reply.trim()
  } finally {
    clearTimeout(timer)
  }
}

// ==================== 推送回复 ====================
/** 以机器人身份推送消息到私聊/群聊 */
async function pushReply(sessionType, sessionId, content, replyTo, robotUserId) {
  const token = (robotUserId && CFG.robotTokens[String(robotUserId)]) || CFG.robotToken
  if (!token) throw new Error('未配置 BOT_ROBOT_TOKEN（机器人推送令牌）')
  const body = JSON.stringify({ sessionType, sessionId, content, replyTo: replyTo || null })
  const headers = { 'Content-Type': 'application/json' }
  if (CFG.secret) headers['X-Bot-Signature'] = hmacHex(CFG.secret, body)
  const res = await fetch(`${CFG.origin}/api/robots/${token}/reply`, {
    method: 'POST', headers, body
  })
  if (!res.ok) {
    const detail = (await res.text().catch(() => '')).slice(0, 200)
    throw new Error(`推送失败 ${res.status}: ${detail}`)
  }
}

// ==================== 事件处理 ====================
async function handleEvent(evt) {
  try {
    const sessionType = evt?.session?.type
    const sessionId = evt?.session?.id
    const messageId = evt?.message?.messageId
    const content = evt?.message?.content
    const robotUserId = evt?.robot?.userId
    const fromUserId = evt?.from?.userId

    if (sessionType !== 'private' && sessionType !== 'group') return
    if (!sessionId || !content) return

    // 消息去重（系统可能重试回调）
    if (isDuplicate(messageId)) return

    console.log(`[事件] ${sessionType} #${sessionId} 来自 ${fromUserId}: ${content.slice(0, 60)}`)

    // 过程通知：先告诉用户已收到、正在处理
    if (CFG.thinkingNotice) {
      try {
        await pushReply(sessionType, sessionId, '🤔 已收到，正在思考…', messageId, robotUserId)
      } catch (e) {
        console.log(`[过程通知失败] ${e.message}`)
      }
    }

    // 调 DeepSeek
    const sessionKey = `${sessionType}:${sessionId}`
    const history = getHistory(sessionKey)
    let reply
    try {
      reply = await askDeepSeek(content, history)
    } catch (e) {
      console.log(`[DeepSeek 失败] ${e.message}`)
      await pushReply(sessionType, sessionId, `⚠️ DeepSeek 调用失败：${e.message}`, messageId, robotUserId).catch(() => {})
      return
    }

    // 记忆对话（用户消息 + 助手回复）
    pushHistory(sessionKey, 'user', content)
    pushHistory(sessionKey, 'assistant', reply)

    // 推送结果
    await pushReply(sessionType, sessionId, reply, messageId, robotUserId)
    console.log(`[回复] ${sessionType} #${sessionId}: ${reply.slice(0, 60)}`)
  } catch (e) {
    console.log(`[处理异常] ${e.message}`)
  }
}

// ==================== HTTP 服务 ====================
const server = http.createServer((req, res) => {
  if (req.method !== 'POST' || req.url !== '/hook') {
    res.writeHead(404, { 'Content-Type': 'application/json' })
    res.end(JSON.stringify({ error: 'not found' }))
    return
  }

  const chunks = []
  req.on('data', c => chunks.push(c))
  req.on('end', () => {
    const rawBody = Buffer.concat(chunks)
    const signature = req.headers['x-bot-signature']

    // 验签（配置了 BOT_SECRET 时）
    if (!verifySignature(rawBody.toString(), signature)) {
      console.log('[验签失败] 拒绝回调')
      res.writeHead(401, { 'Content-Type': 'application/json' })
      res.end(JSON.stringify({ error: 'bad signature' }))
      return
    }

    let evt
    try {
      evt = JSON.parse(rawBody.toString())
    } catch {
      res.writeHead(400, { 'Content-Type': 'application/json' })
      res.end(JSON.stringify({ error: 'bad json' }))
      return
    }

    // 立即 200（异步处理，避免同步回复 10s 超时）
    res.writeHead(200, { 'Content-Type': 'application/json' })
    res.end(JSON.stringify({ success: true }))

    // 异步处理：执行过程与结果都通过机器人推送通知用户
    void handleEvent(evt)
  })
})

server.listen(CFG.port, '0.0.0.0', () => {
  console.log(`✅ 插件已启动，Webhook 地址: http://<本机IP>:${CFG.port}/hook`)
  console.log(`   机器人 WebhookUrl 填: http://host.docker.internal:${CFG.port}/hook（容器内访问宿主机）`)
})
