import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { WsMessage } from '@/types'

/** 心跳间隔与 pong 超时:超时未收到 pong 视为半开连接,主动断开重连 */
const HEARTBEAT_INTERVAL_MS = 30000
const PONG_TIMEOUT_MS = 10000

export const useWebSocketStore = defineStore('websocket', () => {
  const connected = ref(false)
  /** 重连次数已耗尽：连接不再自动恢复，界面要给出明确的「重新连接」入口 */
  const reconnectExhausted = ref(false)
  let ws: WebSocket | null = null
  let reconnectTimer: number | null = null
  let heartbeatTimer: number | null = null
  let pongTimer: number | null = null
  let retryCount = 0
  const maxRetries = 10
  /**
   * 主动断开（登出 / 被踢下线）标记。
   *
   * 必须显式区分「主动断开」与「链路自己断了」：close() 一样会触发 onclose，
   * 而被动的 onclose 会安排重连。没有这个标记时，登出后 1 秒就会**拿着旧 token 重连**，
   * 而服务端会话（7 天有效）并不会因为前端清掉本地令牌而失效 ——
   * 结果是换了账号浏览器里还挂着一个属于上一个账号的活连接。
   */
  let manualClose = false

  const onMessageCallbacks: ((msg: WsMessage) => void)[] = []
  const onStatusCallbacks: ((online: boolean, userId: number) => void)[] = []

  function connect(token: string) {
    if (!token) return
    // CONNECTING 也要挡:否则重复调用会开出第二条连接,前一条变成无人引用的僵尸
    if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) return

    manualClose = false
    reconnectExhausted.value = false

    // 生产:VITE_WS_URL 未配置时自动使用当前站点同域的 /ws 路径(经 nginx 反代);
    // 开发:通过 .env.development 配置 VITE_WS_URL=ws://localhost:5000
    const envWs = import.meta.env.VITE_WS_URL as string | undefined
    const wsUrl = envWs
      ? `${envWs}/?access_token=${token}`
      : `${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws?access_token=${token}`
    ws = new WebSocket(wsUrl)

    ws.onopen = () => {
      console.log('[WS] 已连接')
      connected.value = true
      retryCount = 0
      reconnectExhausted.value = false
      startHeartbeat()
    }

    ws.onmessage = (event) => {
      // 任何入站报文都证明链路是活的,清掉 pong 超时计时
      clearPongTimer()

      try {
        const msg: WsMessage = JSON.parse(event.data)
        if (msg.type === 'pong') return

        if (msg.type === 'online_status') {
          const userId = Number(msg.from)
          for (const cb of onStatusCallbacks) {
            cb(msg.content === 'online', userId)
          }
          return
        }

        for (const cb of onMessageCallbacks) {
          cb(msg)
        }
      } catch (e) {
        console.error('[WS] 消息解析失败', e)
      }
    }

    ws.onclose = () => {
      console.log('[WS] 已断开')
      connected.value = false
      stopHeartbeat()
      // 主动断开不再重连（登出/被踢场景）
      if (manualClose) return
      scheduleReconnect(token)
    }

    ws.onerror = (err) => {
      console.error('[WS] 错误', err)
    }
  }

  function disconnect() {
    manualClose = true
    retryCount = 0
    reconnectExhausted.value = false
    if (reconnectTimer) {
      clearTimeout(reconnectTimer)
      reconnectTimer = null
    }
    stopHeartbeat()
    if (ws) {
      ws.close()
      ws = null
    }
    connected.value = false
  }

  /**
   * 发送一条报文,返回是否真的写进了 socket。
   *
   * 返回值是必需的:调用方(聊天区)已经先把消息乐观地插进了列表,
   * 若这里静默丢弃,那条消息会永远停在「发送中」——既没有失败态也没有重试入口。
   */
  function sendMessage(msg: WsMessage): boolean {
    if (ws?.readyState !== WebSocket.OPEN) return false

    try {
      ws.send(JSON.stringify(msg))
      return true
    } catch (e) {
      console.error('[WS] 发送失败', e)
      return false
    }
  }

  function startHeartbeat() {
    stopHeartbeat()
    heartbeatTimer = window.setInterval(() => {
      if (ws?.readyState !== WebSocket.OPEN) return

      ws.send(JSON.stringify({ type: 'heartbeat' }))

      // 半开连接:TCP 尚未断开但对端已不可达时,只发不校验会一直以为「在线」,
      // 消息发出去却永远没有回音。发完心跳起一个超时,收不到 pong 就主动重连。
      armPongTimer()
    }, HEARTBEAT_INTERVAL_MS)
  }

  function stopHeartbeat() {
    if (heartbeatTimer) {
      clearInterval(heartbeatTimer)
      heartbeatTimer = null
    }
    clearPongTimer()
  }

  function armPongTimer() {
    clearPongTimer()
    pongTimer = window.setTimeout(() => {
      console.warn('[WS] 心跳超时(未收到 pong),按连接已失效处理并重连')
      // close 会触发 onclose → scheduleReconnect,复用既有的重连退避逻辑
      try {
        ws?.close()
      } catch (e) {
        console.error('[WS] 关闭失效连接时出错', e)
      }
    }, PONG_TIMEOUT_MS)
  }

  function clearPongTimer() {
    if (pongTimer) {
      clearTimeout(pongTimer)
      pongTimer = null
    }
  }

  function scheduleReconnect(token: string) {
    if (retryCount >= maxRetries) {
      console.log('[WS] 重连次数已达上限')
      // 不能只写日志：用户看到的是一个"看起来还在重连、实际永远不会好"的界面
      reconnectExhausted.value = true
      return
    }
    const delay = Math.min(1000 * Math.pow(2, retryCount), 30000)
    retryCount++
    console.log(`[WS] ${delay / 1000}s 后重连 (第 ${retryCount} 次)`)
    reconnectTimer = window.setTimeout(() => connect(token), delay)
  }

  /** 注册报文回调;返回取消订阅函数(组件卸载时调用,避免回调数组无限增长) */
  function onMessage(cb: (msg: WsMessage) => void) {
    onMessageCallbacks.push(cb)
    return () => {
      const index = onMessageCallbacks.indexOf(cb)
      if (index >= 0) onMessageCallbacks.splice(index, 1)
    }
  }

  function onStatusChange(cb: (online: boolean, userId: number) => void) {
    onStatusCallbacks.push(cb)
    return () => {
      const index = onStatusCallbacks.indexOf(cb)
      if (index >= 0) onStatusCallbacks.splice(index, 1)
    }
  }

  return { connected, reconnectExhausted, connect, disconnect, sendMessage, onMessage, onStatusChange }
})
