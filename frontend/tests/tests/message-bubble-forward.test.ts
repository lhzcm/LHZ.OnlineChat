import { beforeEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import MessageBubble from '@/components/chat/MessageBubble.vue'
import { useAuthStore } from '@/stores/auth'
import { useMessageMenu } from '@/composables/useMessageMenu'
import { buildForwardBundle } from '@/utils/forward'
import type { ChatMessage } from '@/stores/chat'

/**
 * 「聊天记录」转发卡片（messageType = 4）的渲染与点击。
 *
 * 卡片是独立样式、不是气泡；内容来自 JSON 快照，解析失败时必须退化成占位而不是白屏。
 */
const ME = 10001
const PEER = 10002

function base(over: Partial<ChatMessage> = {}): ChatMessage {
  return {
    type: 'private_message',
    from: String(PEER),
    to: String(ME),
    content: '你好',
    timestamp: 1_700_000_000_000,
    messageId: 'm1',
    messageType: 0,
    senderName: '张三',
    senderAvatar: null,
    ...over
  } as ChatMessage
}

function forwardMsg(items: ChatMessage[]): ChatMessage {
  return base({ messageId: 'fwd-1', messageType: 4, content: buildForwardBundle(items) })
}

function mountBubble(m: ChatMessage, props: Record<string, unknown> = {}) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore()
  auth.user = { id: ME, nickname: '我', avatar: null } as never

  return mount(MessageBubble, {
    props: { msg: m, chatType: 'private', chatId: PEER, chatName: '张三', ...props },
    global: { plugins: [pinia] }
  })
}

beforeEach(() => {
  setActivePinia(createPinia())
  // 菜单状态是模块级单例：每个用例前清掉，避免跨用例串状态
  useMessageMenu().closeMenu(true)
})

describe('转发卡片渲染', () => {
  it('显示「聊天记录」、条数与明细行', () => {
    const wrapper = mountBubble(forwardMsg([
      base({ messageId: 'a', content: '第一句', senderName: '张三' }),
      base({ messageId: 'b', content: '第二句', senderName: '李四' })
    ]))

    const card = wrapper.find('.msg-forward')
    expect(card.exists()).toBe(true)
    expect(card.text()).toContain('聊天记录')
    expect(card.text()).toContain('2 条')
    expect(card.text()).toContain('张三')
    expect(card.text()).toContain('第一句')
    // 卡片不是气泡：外层带 is-forward，走独立样式
    expect(wrapper.find('.msg-bubble').classes()).toContain('is-forward')
  })

  it('明细超过 3 条时只预览 3 行 + 总数', () => {
    const many = Array.from({ length: 6 }, (_, i) => base({ messageId: `m${i}`, content: `第${i}条` }))

    const text = mountBubble(forwardMsg(many)).find('.msg-forward').text()

    expect(text).toContain('第0条')
    expect(text).toContain('第2条')
    expect(text).not.toContain('第3条')
    expect(text).toContain('共 6 条')
  })

  it('载荷损坏时退化成占位卡片，不崩', () => {
    const wrapper = mountBubble(base({ messageId: 'bad', messageType: 4, content: 'not-json' }))

    const card = wrapper.find('.msg-forward')
    expect(card.exists()).toBe(true)
    expect(card.text()).toContain('记录无法解析')
  })
})

describe('转发卡片交互', () => {
  it('点击卡片发出 open-forward，带上解析后的明细', async () => {
    const wrapper = mountBubble(forwardMsg([base({ messageId: 'a', content: '第一句' })]))

    await wrapper.find('.msg-forward').trigger('click')

    const events = wrapper.emitted('open-forward')
    expect(events).toHaveLength(1)
    const items = events![0][0] as Array<{ c: string }>
    expect(items[0].c).toBe('第一句')
  })

  it('多选模式下点卡片是勾选，不打开查看器', async () => {
    const wrapper = mountBubble(forwardMsg([base()]), { selectMode: true })

    await wrapper.find('.msg-forward').trigger('click')

    expect(wrapper.emitted('toggle-select')).toHaveLength(1)
    expect(wrapper.emitted('open-forward')).toBeUndefined()
  })
})