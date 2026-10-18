import { describe, expect, it, vi } from 'vitest'
import { genMessageId, sendMessage, toPayload } from '@/utils/messageSend'
import type { ChatMessage } from '@/stores/chat'

/**
 * 发送原语（从 ChatArea 抽出，输入区与多选转发共用）。
 * 这三件事写错的后果：ID 重复 → 消息被去重吞掉；status 进协议体 → 服务端字段污染；
 * 返回值判断错 → 断线时消息永远停在"发送中"。
 */
function msg(over: Partial<ChatMessage> = {}): ChatMessage {
  return {
    type: 'private_message',
    from: '10001',
    to: '10002',
    content: '你好',
    timestamp: 1_700_000_000_000,
    messageId: 'm1',
    messageType: 0,
    senderName: '我',
    senderAvatar: null,
    status: 'sending',
    ...over
  } as ChatMessage
}

describe('genMessageId', () => {
  it('生成非空且互不相同的 ID', () => {
    const ids = new Set(Array.from({ length: 50 }, () => genMessageId()))

    expect(ids.size).toBe(50)
    expect([...ids].every(id => typeof id === 'string' && id.length > 0)).toBe(true)
  })
})

describe('toPayload', () => {
  it('剥掉本地 status，保留协议字段', () => {
    const payload = toPayload(msg({ status: 'failed' }))

    expect('status' in payload).toBe(false)
    expect(payload).toMatchObject({
      type: 'private_message',
      from: '10001',
      to: '10002',
      content: '你好',
      messageId: 'm1',
      messageType: 0
    })
  })

  it('不修改原对象', () => {
    const original = msg({ status: 'failed' })

    toPayload(original)

    expect(original.status).toBe('failed')
  })
})

describe('sendMessage', () => {
  it('发送成功返回 true', () => {
    const sender = { sendMessage: vi.fn().mockReturnValue(true) }

    expect(sendMessage(sender, msg())).toBe(true)
    expect(sender.sendMessage).toHaveBeenCalledTimes(1)
  })

  it('发送器报告失败（false）时返回 false', () => {
    expect(sendMessage({ sendMessage: () => false }, msg())).toBe(false)
  })

  it('发送器返回 void（旧实现）视为成功', () => {
    expect(sendMessage({ sendMessage: () => {} }, msg())).toBe(true)
  })

  it('传给发送器的是剥掉 status 的载荷', () => {
    const sender = { sendMessage: vi.fn().mockReturnValue(true) }

    sendMessage(sender, msg({ status: 'sending' }))

    const payload = sender.sendMessage.mock.calls[0][0]
    expect('status' in payload).toBe(false)
  })
})
