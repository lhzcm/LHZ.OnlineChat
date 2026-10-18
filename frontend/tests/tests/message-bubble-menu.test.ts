import { beforeEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import MessageBubble from '@/components/chat/MessageBubble.vue'
import { useAuthStore } from '@/stores/auth'
import { useMessageMenu } from '@/composables/useMessageMenu'
import type { ChatMessage } from '@/stores/chat'

/**
 * 消息操作菜单（右键 / 长按）的交互回归。
 *
 * 起因：行上的事件曾写成 `@contextmenu="selectMode ? undefined : onContextMenu"` ——
 * 三元表达式里的**裸函数引用**会被 Vue 编译成 `$event => (条件 ? undefined : fn)`，
 * 只返回函数、从不调用，于是右键完全没反应（构建与类型检查都是绿的）。
 * 这类"模板绑定在运行时静默失效"的问题只有挂载组件才能测出来。
 */
const ME = 10001
const PEER = 10002

function msg(over: Partial<ChatMessage> = {}): ChatMessage {
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

function mountBubble(m: ChatMessage = msg()) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore()
  auth.user = { id: ME, nickname: '我', avatar: null } as never

  return mount(MessageBubble, {
    props: { msg: m, chatType: 'private', chatId: PEER, chatName: '张三' },
    global: { plugins: [pinia] }
  })
}

beforeEach(() => {
  setActivePinia(createPinia())
  // 菜单状态是模块级单例：每个用例前清掉，避免跨用例串状态
  useMessageMenu().closeMenu(true)
})

describe('MessageBubble 操作菜单', () => {
  it('右键消息弹出操作菜单（含「复制」与「多选」）', async () => {
    const wrapper = mountBubble()
    expect(wrapper.find('.ctx-menu').exists()).toBe(false)

    await wrapper.find('.msg-row').trigger('contextmenu', { clientX: 120, clientY: 120 })

    expect(wrapper.find('.ctx-menu').exists()).toBe(true)
    expect(wrapper.text()).toContain('复制')
    expect(wrapper.text()).toContain('多选')
  })

  it('多选模式下右键不弹菜单（点击用于勾选）', async () => {
    const wrapper = mountBubble()
    await wrapper.setProps({ selectMode: true })

    await wrapper.find('.msg-row').trigger('contextmenu', { clientX: 10, clientY: 10 })

    expect(wrapper.find('.ctx-menu').exists()).toBe(false)
  })

  it('多选模式下点击整行触发选中切换', async () => {
    const wrapper = mountBubble()
    await wrapper.setProps({ selectMode: true })

    await wrapper.find('.msg-row').trigger('click')

    expect(wrapper.emitted('toggle-select')).toHaveLength(1)
  })

  it('普通模式下点击整行不会触发选中', async () => {
    const wrapper = mountBubble()

    await wrapper.find('.msg-row').trigger('click')

    expect(wrapper.emitted('toggle-select')).toBeUndefined()
  })
})