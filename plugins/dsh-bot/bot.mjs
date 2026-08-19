#!/usr/bin/env node
/**
 * DeepSeek Harness 机器人插件
 *
 * 把 LHZ.OnlineChat 机器人变成 DeepSeek Harness 任务入口：
 *   用户私聊/群聊 @ 机器人发送任务 → 系统回调本插件
 *   → 插件调用本机 DeepSeek Harness（dsh --profile headless 单任务模式）执行
 *   → 通过机器人推送接口把「过程通知」和「执行结果」通知用户。
 *
 * 运行：node bot.mjs（或 BOT_PORT=9312 node bot.mjs）
 * 配置见 .env.example（也支持环境变量直接传入）。
 * 零依赖：仅用 Node 18+ 内置能力（http / crypto / child_process / fetch）。
 *
 * 前提：本机已安装并配置好 DeepSeek Harness（dsh CLI 可用，DSH_HOME 已初始化）。
 */
import http from 'node:http'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { spawn } from 'node:child_process'
import { fileURLToPath } from 'node:url'

// ==================== 配置加载 ====================
const __dirname = path.dirname(fileURLToPath(import.meta.url))
if (fs.existsSync(path.join(__dirname, '.env'))) {
  for (const line of fs.readFileSync(path.join(__dirname, '.env'), 'utf8').split(/\r?\n/)) {
    const m = line.match(/^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)\s*$/)
    if (m && !process.env[m[1]]) process.env[m[1]] = m[2].replace(/^["']|["']$/g, '')
  }
}

const CFG = {
  port: Number(process.env.BOT_PORT || 9312),
  origin: (process.env.BOT_ORIGIN || 'http://localhost:8080').replace(/\/+$/, ''),
  robotToken: process.env.BOT_ROBOT_TOKEN || '',
  robotTokens: Object.fromEntries(
    (process.env.BOT_ROBOT_TOKENS || '')
      .split(',').map(s => s.trim()).filter(Boolean)
      .map(s => { const [uid, tok] = s.split(':'); return [uid, tok] })
  ),
  secret: process.env.BOT_SECRET || '',
  // ---- DSH 执行配置 ----
  // 命令与固定参数（默认 dsh --profile headless）。
  // Windows 下 pnpm/npm 是 .cmd shim，spawn 无法直接执行：
  // 可配 DSH_CMD=node + DSH_SCRIPT=<pnpm.cjs 绝对路径> + DSH_ARGS="dsh --profile headless"
  dshCmd: process.env.DSH_CMD || 'dsh',
  dshScript: process.env.DSH_SCRIPT || '',
  dshArgs: (process.env.DSH_ARGS || '--profile headless').split(/\s+/).filter(Boolean),
  // dsh 命令的工作目录（开发环境在 deepseek-harness 仓库根目录跑 pnpm dsh 时需指定；
  // 生产环境 dsh 已装进 PATH 时默认与任务工作目录一致即可）
  dshCwd: process.env.DSH_CWD || process.env.DSH_WORKDIR || __dirname,
  // 任务工作目录（agent 的可写工作区；默认插件的 workspace/ 子目录）
  dshWorkdir: process.env.DSH_WORKDIR || path.join(__dirname, 'workspace'),
  // 单任务超时（默认 10 分钟）
  timeoutMs: Number(process.env.DSH_TIMEOUT_MS || 600000),
  // 开始过程通知（任务提交时）
  startNotice: (process.env.BOT_START_NOTICE || 'true') !== 'false',
  // 结果最大推送长度（服务端单条消息上限 5000 字）
  resultMax: Number(process.env.BOT_RESULT_MAX || 4000)
}

fs.mkdirSync(CFG.dshWorkdir, { recursive: true })

console.log('==========================================')
console.log('🧠 DeepSeek Harness Bot Plugin')
console.log(`   监听端口   : ${CFG.port}`)
console.log(`   站点地址   : ${CFG.origin}`)
console.log(`   DSH 命令   : ${CFG.dshCmd} ${CFG.dshArgs.join(' ')}`)
console.log(`   工作目录   : ${CFG.dshWorkdir}`)
console.log(`   任务超时   : ${CFG.timeoutMs / 1000}s`)
console.log(`   签名密钥   : ${CFG.secret ? '已启用（回调验签 + 推送签名）' : '未启用'}`)
console.log(`   Webhook 地址: http://0.0.0.0:${CFG.port}/hook`)
console.log('==========================================')

// ==================== 去重 ====================
const processed = new Map()
function isDuplicate(messageId) {
  if (!messageId) return false
  const now = Date.now()
  for (const [id, t] of processed) if (now - t > 5 * 60 * 1000) processed.delete(id)
  if (processed.has(messageId)) return true
  processed.set(messageId, now)
  if (processed.size > 1000) processed.delete(processed.keys().next().value)
  return false
}

// ==================== 签名 ====================
function hmacHex(secret, body) {
  return crypto.createHmac('sha256', secret).update(body).digest('hex')
}
function verifySignature(rawBody, signature) {
  if (!CFG.secret) return true
  if (!signature) return false
  const a = Buffer.from(hmacHex(CFG.secret, rawBody))
  const b = Buffer.from(signature)
  return a.length === b.length && crypto.timingSafeEqual(a, b)
}

// ==================== 推送 ====================
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

// ==================== DSH 任务执行（串行队列） ====================
const queue = []
let running = false

/** 截断结果（服务端单条消息 5000 字上限） */
function truncate(text, max = CFG.resultMax) {
  if (text.length <= max) return text
  return text.slice(0, max) + '\n\n…（内容过长已截断，剩余部分请查看 Harness 会话日志）'
}

/**
 * 执行一个 DSH 单任务：dsh --profile headless "<任务>"
 * 返回 { ok, stdout, stderr }
 */
function runDshTask(task) {
  return new Promise((resolve) => {
    // 参数数组组装（不经 shell，无命令注入面）：
    // node [DSH_SCRIPT] [DSH_ARGS...] <task>   或   dsh [DSH_ARGS...] <task>
    const args = []
    if (CFG.dshScript) args.push(CFG.dshScript)
    args.push(...CFG.dshArgs, task)
    const child = spawn(CFG.dshCmd, args, {
      cwd: CFG.dshCwd,
      env: { ...process.env },
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true
    })
    let stdout = ''
    let stderr = ''
    const timer = setTimeout(() => {
      console.log(`[任务超时] ${CFG.timeoutMs / 1000}s，终止进程`)
      child.kill('SIGKILL')
    }, CFG.timeoutMs)
    child.stdout.on('data', d => { stdout += d })
    child.stderr.on('data', d => { stderr += d })
    child.on('error', err => {
      clearTimeout(timer)
      resolve({ ok: false, stdout, stderr: `${stderr}\n[启动失败] ${err.message}` })
    })
    child.on('close', (code) => {
      clearTimeout(timer)
      const ok = code === 0 && stdout.trim().length > 0
      resolve({ ok, stdout, stderr, code })
    })
  })
}

/** 串行执行队列中的一个任务 */
async function processQueue() {
  if (running) return
  running = true
  while (queue.length > 0) {
    const job = queue.shift()
    try {
      await handleTask(job)
    } catch (e) {
      console.log(`[任务处理异常] ${e.message}`)
    }
  }
  running = false
}

/** 执行单个任务（含过程通知与结果推送） */
async function handleTask(job) {
  const { sessionType, sessionId, task, messageId, robotUserId } = job

  // 过程通知：任务已提交给 Harness
  if (CFG.startNotice) {
    try {
      const preview = task.length > 80 ? task.slice(0, 80) + '…' : task
      await pushReply(sessionType, sessionId,
        `🧠 任务已提交给 DeepSeek Harness，执行中…\n\n📋 任务：${preview}\n\n完成后我会把结果发给你。`,
        messageId, robotUserId)
    } catch (e) {
      console.log(`[开始通知失败] ${e.message}`)
    }
  }

  console.log(`[执行] ${sessionType} #${sessionId}: ${task.slice(0, 60)}`)
  const start = Date.now()
  const r = await runDshTask(task)
  const costSec = ((Date.now() - start) / 1000).toFixed(1)

  if (r.ok) {
    const answer = truncate(r.stdout.trim())
    console.log(`[完成] ${costSec}s`)
    await pushReply(sessionType, sessionId,
      `✅ DeepSeek Harness 执行完成（${costSec}s）\n\n${answer}`,
      messageId, robotUserId)
  } else {
    const errTail = truncate((r.stderr || r.stdout || '').trim().slice(-1500), 1500)
    console.log(`[失败] code=${r.code ?? '?'} ${costSec}s`)
    await pushReply(sessionType, sessionId,
      `⚠️ DeepSeek Harness 执行失败（${costSec}s）\n\n${errTail || '无输出'}`,
      messageId, robotUserId)
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

    if (sessionType !== 'private' && sessionType !== 'group') return
    if (!sessionId || !content) return
    if (isDuplicate(messageId)) return

    // 群聊 @ 时内容里带 "@机器人名 "，去掉前缀再作为任务
    const task = content.replace(/^@\S+\s*/, '').trim()
    if (!task) {
      await pushReply(sessionType, sessionId, '请发送要执行的任务内容', messageId, robotUserId).catch(() => {})
      return
    }

    console.log(`[事件] ${sessionType} #${sessionId}: ${content.slice(0, 60)}`)

    // 入队（串行执行）：正在执行的任务也算一个占位，排队位置包含它
    const position = queue.length + (running ? 1 : 0) + 1
    queue.push({ sessionType, sessionId, task, messageId, robotUserId })
    if (position > 1) {
      try {
        await pushReply(sessionType, sessionId,
          `⏳ 前一个任务还在执行中，你的任务已排队（第 ${position} 个）`, messageId, robotUserId)
      } catch { }
    }
    void processQueue()
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

    res.writeHead(200, { 'Content-Type': 'application/json' })
    res.end(JSON.stringify({ success: true }))
    void handleEvent(evt)
  })
})

server.listen(CFG.port, '0.0.0.0', () => {
  console.log(`✅ 插件已启动，Webhook 地址: http://<本机IP>:${CFG.port}/hook`)
  console.log(`   机器人 WebhookUrl 填: http://host.docker.internal:${CFG.port}/hook（容器内访问宿主机）`)
})
