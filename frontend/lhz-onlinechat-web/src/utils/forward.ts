import type { ChatType } from '@/types'
import type { ChatMessage } from '@/stores/chat'
import { stripMarkdown } from '@/utils/markdown'

/**
 * 「聊天记录」合并转发（messageType = 4）。
 *
 * 一次转发 = 一条消息 = 一张卡片，卡片里是**只读快照**（最多 20 条明细）：
 *   { "v":1, "ts":1700000000000, "items":[ {"n":"张三","c":"内容","t":0,"ts":1700000000000} ] }
 *
 * 为什么不逐条发：那样"一次转发"在会话里是一串互不相关的消息，收件人看不出是一起的；
 * 同一批消息发到多个会话时也更容易重复上屏。快照则天然回答了"谁、什么时候、转了什么"。
 *
 * 上限与服务端 `ForwardBundle` 对齐（服务端会拒收，这里先挡住，免得白等一个失败回执）。
 */
export const FORWARD_BUNDLE_VERSION = 1
/** 一次最多合并多少条（与服务端 ForwardBundle.MaxItems 对齐） */
export const MAX_FORWARD_MESSAGES = 20
/** 单条被转发内容的长度上限（与服务端 ForwardBundle.MaxItemLength 对齐） */
export const MAX_FORWARD_ITEM_LENGTH = 2000
/** 一次最多发往几个会话（纯客户端限制：目标越多，帧越多） */
export const MAX_FORWARD_TARGETS = 5
/** 卡片明细里允许出现的消息类型：文字 / 图片 / Markdown（不允许嵌套转发） */
export type ForwardItemType = 0 | 1 | 3 | 5

export interface ForwardTarget {
  type: ChatType
  id: number
  name: string
  avatar?: string | null
}

export interface ForwardedItem {
  /** 原发送者昵称 */
  n: string
  /** 原内容（Markdown 存源文本，图片存 URL） */
  c: string
  /** 消息类型 0/1/3/5（5 = 语音，c 是音频地址） */
  t: ForwardItemType
  /** 原消息时间戳（毫秒） */
  ts: number
}

/** 把选中的消息打包成卡片载荷（JSON 字符串） */
export function buildForwardBundle(messages: ChatMessage[], now = Date.now()): string {
  const items: ForwardedItem[] = messages.slice(0, MAX_FORWARD_MESSAGES).map(m => {
    // 卡片里只保留四种类型：图片/文字/Markdown/语音；其它（文件、嵌套转发）退化成文字
    const type: ForwardItemType = m.messageType === 1 ? 1 : m.messageType === 3 ? 3 : m.messageType === 5 ? 5 : 0
    const raw = m.isDeleted ? '（消息已撤回）' : (m.content || '')
    return {
      n: (m.senderName || '').trim().slice(0, 64),
      c: raw.slice(0, MAX_FORWARD_ITEM_LENGTH),
      t: type,
      ts: m.timestamp || now
    }
  })

  return JSON.stringify({ v: FORWARD_BUNDLE_VERSION, ts: now, items })
}

/** 解析卡片载荷；结构不对返回 null（老数据 / 被篡改的 content 都不能让界面崩） */
export function parseForwardBundle(content: string): ForwardedItem[] | null {
  let parsed: unknown
  try {
    parsed = JSON.parse(content || '')
  } catch {
    return null
  }
  if (!parsed || typeof parsed !== 'object') return null
  const rawItems = (parsed as { items?: unknown }).items
  if (!Array.isArray(rawItems) || rawItems.length === 0) return null

  return rawItems.map(raw => {
    const item = (raw ?? {}) as Record<string, unknown>
    const type = item.t === 1 ? 1 : item.t === 3 ? 3 : 0
    return {
      n: typeof item.n === 'string' ? item.n : '',
      c: typeof item.c === 'string' ? item.c : '',
      t: type as ForwardItemType,
      ts: typeof item.ts === 'number' ? item.ts : 0
    }
  })
}

/** 会话列表 / 搜索结果里的单行预览：转发的聊天记录不展示 JSON */
export function messagePreview(content: string, messageType: number): string {
  if (messageType === 4) return '[聊天记录]'
  if (messageType === 1) return '[图片]'
  if (messageType === 2) return '[文件]'
  if (messageType === 5) return '[语音]'
  if (messageType === 3) return stripMarkdown(content)
  return content
}

/** 卡片里某一行的摘要（图片显示 [图片]，Markdown 抹掉记号，超长截断） */
export function forwardItemPreview(item: ForwardedItem, max = 40): string {
  if (item.t === 1) return '[图片]'
  if (item.t === 5) return '[语音]'
  const text = item.t === 3 ? stripMarkdown(item.c) : item.c
  return text.length > max ? `${text.slice(0, max)}…` : text
}

/** 批量复制的文本：每条一行「[发送者] 内容」，撤回的标注出来 */
export function buildBatchCopyText(messages: ChatMessage[]): string {
  return messages
    .map(m => {
      const name = (m.senderName || '').trim() || '未知'
      const body = m.isDeleted ? '（消息已撤回）' : (m.content || '')
      return `[${name}] ${body}`
    })
    .join('\n')
}