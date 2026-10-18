import type { WsMessage } from '@/types'
import type { ChatMessage } from '@/stores/chat'

/**
 * 发送消息的共用原语（原来在 ChatArea 里重复了一份，拆分后由输入区与多选转发共用）。
 *
 * 只做三件纯事情：生成消息 ID、剥掉本地字段、写入 WS 连接。
 * 不碰 store（乐观上屏由调用方决定），这样单测不需要造 Pinia。
 */

/** 生成消息 ID：优先 crypto.randomUUID；老浏览器用时间戳+随机串兜底 */
export function genMessageId(now = Date.now()): string {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) return crypto.randomUUID()
  return `${now.toString(36)}-${Math.random().toString(36).slice(2, 10)}`
}

/** 发送前剥离本地字段（status 只用于本地渲染，不进 WS 协议体） */
export function toPayload(msg: ChatMessage): WsMessage {
  const { status: _status, ...payload } = msg
  return payload
}

/**
 * 写入 WS 连接，返回是否真的发出（true = 连接已打开并已写入）。
 * websocket store 的 sendMessage 返回 boolean；这里用宽签名兼容返回 void 的实现，
 * 避免两侧类型未同步时构建失败（void 视为"未报告失败"）。
 */
export function sendMessage(
  ws: { sendMessage: (msg: WsMessage) => boolean | void },
  msg: ChatMessage
): boolean {
  return ws.sendMessage(toPayload(msg)) !== false
}
