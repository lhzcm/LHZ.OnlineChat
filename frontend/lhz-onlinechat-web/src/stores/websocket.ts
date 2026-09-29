import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { WsMessage } from '@/types'

export const useWebSocketStore = defineStore('websocket', () => {
  const connected = ref(false)
  let ws: WebSocket | null = null
  let reconnectTimer: number | null = null
  let heartbeatTimer: number | null = null
  let retryCount = 0
  const maxRetries = 10

  const onMessageCallbacks: ((msg: WsMessage) => void)[] = []
  const onStatusCallbacks: ((online: boolean, userId: number) => void)[] = []

  function connect(token: string) {
    if (!token) return
    if (ws?.readyState === WebSocket.OPEN) return

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
      startHeartbeat()
    }

    ws.onmessage = (event) => {
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
      scheduleReconnect(token)
    }

    ws.onerror = (err) => {
      console.error('[WS] 错误', err)
    }
  }

  function disconnect() {
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

  function sendMessage(msg: WsMessage) {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify(msg))
    }
  }

  function startHeartbeat() {
    stopHeartbeat()
    heartbeatTimer = window.setInterval(() => {
      if (ws?.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({ type: 'heartbeat' }))
      }
    }, 30000)
  }

  function stopHeartbeat() {
    if (heartbeatTimer) {
      clearInterval(heartbeatTimer)
      heartbeatTimer = null
    }
  }

  function scheduleReconnect(token: string) {
    if (retryCount >= maxRetries) {
      console.log('[WS] 重连次数已达上限')
      return
    }
    const delay = Math.min(1000 * Math.pow(2, retryCount), 30000)
    retryCount++
    console.log(`[WS] ${delay / 1000}s 后重连 (第 ${retryCount} 次)`)
    reconnectTimer = window.setTimeout(() => connect(token), delay)
  }

  function onMessage(cb: (msg: WsMessage) => void) {
    onMessageCallbacks.push(cb)
  }

  function onStatusChange(cb: (online: boolean, userId: number) => void) {
    onStatusCallbacks.push(cb)
  }

  return { connected, connect, disconnect, sendMessage, onMessage, onStatusChange }
})
