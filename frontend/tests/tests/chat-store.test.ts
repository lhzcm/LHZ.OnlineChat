import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useChatStore, type ChatMessage } from '@/stores/chat'

/**
 * 会话 store：消息归属、去重、状态回填、跨会话标失败、未读角标与内存上限。
 * 这些逻辑都在组件外，出错的表现是"消息重复/消失/角标不对"，非常难从界面上倒推。
 */
const ME = 10001
const PEER = 10002

function msg(over: Partial<ChatMessage> = {}): ChatMessage {
  return {
    type: 'private_message',
    from: String(PEER),
    to: String(ME),
    content: '你好',
    timestamp: 1_000,
    messageId: 'm1',
    messageType: 0,
    senderName: '张三',
    senderAvatar: null,
    ...over
  } as ChatMessage
}

function store() {
  return useChatStore()
}

beforeEach(() => {
  setActivePinia(createPinia())
  document.title = ''
})

describe('sessionKey', () => {
  it('私聊与群聊各自成 key', () => {
    expect(store().sessionKey('private', PEER)).toBe('private_10002')
    expect(store().sessionKey('group', 5)).toBe('group_5')
  })
})

describe('addMessage：归属与去重', () => {
  it('对方发来的私聊消息归到 from 的会话', () => {
    const { key, isNew } = store().addMessage(msg(), ME)

    expect(key).toBe('private_10002')
    expect(isNew).toBe(true)
    expect(store().messages.get(key)).toHaveLength(1)
  })

  it('自己发出的私聊消息归到 to 的会话（发送者视角）', () => {
    const { key } = store().addMessage(msg({ from: String(ME), to: String(PEER) }), ME)

    expect(key).toBe('private_10002')
  })

  it('群消息归到 to 的群会话', () => {
    const { key } = store().addMessage(
      msg({ type: 'group_message', to: '5', from: String(ME) }), ME
    )

    expect(key).toBe('group_5')
  })

  it('同一 messageId 重复到达不算新消息，并把乐观发送态回填成已发送', () => {
    const s = store()
    s.addMessage(msg({ messageId: 'cmid-1', status: 'sending' }), ME)

    const { isNew } = s.addMessage(msg({ messageId: 'cmid-1', status: 'sending' }), ME)

    expect(isNew).toBe(false)
    const list = s.messages.get('private_10002')!
    expect(list).toHaveLength(1)
    expect(list[0].status).toBe('sent')
  })

  it('乱序到达后按时间戳升序排列', () => {
    const s = store()
    s.addMessage(msg({ messageId: 'late', timestamp: 2_000 }), ME)
    s.addMessage(msg({ messageId: 'early', timestamp: 1_000 }), ME)

    expect(s.messages.get('private_10002')!.map(m => m.messageId)).toEqual(['early', 'late'])
  })
})

describe('消息状态与撤回', () => {
  it('setMessageStatus 只改指定会话里的那条', () => {
    const s = store()
    s.addMessage(msg({ messageId: 'a', status: 'sending' }), ME)

    s.setMessageStatus('private_10002', 'a', 'failed')

    expect(s.messages.get('private_10002')![0].status).toBe('failed')
  })

  it('markMessageFailedById 能标记**非当前会话**里的消息（回执只带 messageId）', () => {
    const s = store()
    s.addMessage(msg({ messageId: 'from-a', content: 'A' }), ME)
    s.addMessage(msg({ messageId: 'from-b', content: 'B', from: '10003' }), ME)
    s.setCurrentSession('private', PEER, '张三')

    const hit = s.markMessageFailedById('from-b')

    expect(hit).toBe(true)
    expect(s.messages.get('private_10003')![0].status).toBe('failed')
    expect(s.messages.get('private_10002')![0].status).toBeUndefined()
  })

  it('markMessageFailedById 找不到消息时返回 false（不会乱标）', () => {
    const s = store()
    s.addMessage(msg({ messageId: 'a' }), ME)

    expect(s.markMessageFailedById('不存在')).toBe(false)
    expect(s.markMessageFailedById(undefined)).toBe(false)
    expect(s.messages.get('private_10002')![0].status).toBeUndefined()
  })

  it('markMessageRecalled 置为已撤回', () => {
    const s = store()
    s.addMessage(msg({ messageId: 'a' }), ME)

    s.markMessageRecalled('private_10002', 'a')

    expect(s.messages.get('private_10002')![0].isDeleted).toBe(true)
  })

  it('removeMessage 移除指定消息', () => {
    const s = store()
    s.addMessage(msg({ messageId: 'a' }), ME)
    s.addMessage(msg({ messageId: 'b' }), ME)

    s.removeMessage('private_10002', 'a')

    expect(s.messages.get('private_10002')!.map(m => m.messageId)).toEqual(['b'])
  })
})

describe('未读与标题角标', () => {
  it('未读数累加，普通设置取较大值而不是覆盖', () => {
    const s = store()
    const key = 'private_10002'

    s.bumpUnread(key)
    s.bumpUnread(key)
    expect(s.unreadCounts.get(key)).toBe(2)

    s.setUnreadCount(key, 1)
    expect(s.unreadCounts.get(key)).toBe(2)

    s.setUnreadCount(key, 5)
    expect(s.unreadCounts.get(key)).toBe(5)
  })

  it('标题角标跟随总未读数（(N) OnlineChat）', async () => {
    const s = store()
    s.bumpUnread('private_10002')
    await Promise.resolve()

    expect(document.title).toBe('(1) OnlineChat')
  })
})

describe('已读回执', () => {
  it('对方已读后，我发出的消息被标记为已读', () => {
    const s = store()
    s.addMessage(msg({ from: String(ME), to: String(PEER), messageId: 'mine-1' }), ME)

    s.markSessionReadByPeer('private_10002')

    expect(s.isReadByPeer('mine-1')).toBe(true)
    expect(s.isReadByPeer('other')).toBe(false)
  })
})

describe('内存上限', () => {
  it('消息缓存超过 30 个会话时按 LRU 淘汰，当前会话永不淘汰', () => {
    const s = store()
    s.setCurrentSession('private', 20000, '当前会话')

    for (let i = 0; i < 31; i++) {
      // 20000 那个会话先灌一条，确保它是最早插入的（最容易成为淘汰目标）
      s.addMessage(msg({ from: String(i === 0 ? 20000 : 11000 + i), messageId: `m-${i}` }), ME)
    }

    expect(s.messages.size).toBe(30)
    expect(s.messages.has('private_20000')).toBe(true)   // 当前会话被保住
    expect(s.messages.has('private_11001')).toBe(false)  // 最早的非当前会话被淘汰
  })
})
