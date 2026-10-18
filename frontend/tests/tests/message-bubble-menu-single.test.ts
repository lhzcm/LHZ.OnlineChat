import { beforeEach, describe, expect, it } from 'vitest'
import { defineComponent } from 'vue'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import MessageBubble from '@/components/chat/MessageBubble.vue'
import { useAuthStore } from '@/stores/auth'
import { useMessageMenu } from '@/composables/useMessageMenu'
import type { ChatMessage } from '@/stores/chat'

/**
 * 回归：同时右键多条消息**只能有一个菜单**。
 *
 * 以前的实现里 menu 是每个气泡各自的 ref：右键 A 打开 A 的菜单，再右键 B 只开 B 的，
 * A 的那份没人关 —— 于是右键几条就叠几个弹窗。现在状态收敛到 `useMessageMenu` 的模块级单例，
 * 任何一次打开都会替换掉之前打开的。
 */
const ME = 10001
const PEER = 10002

function msg(messageId: string, content = '你好'): ChatMessage {
  return {
    type: 'private_message',
    from: String(PEER),
    to: String(ME),
    content,
    timestamp: 1_700_000_000_000,
    messageId,
    messageType: 0,
    senderName: '张三',
    senderAvatar: null
  } as ChatMessage
}

function mountTwoBubbles() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore()
  auth.user = { id: ME, nickname: '我', avatar: null } as never

  const Parent = defineComponent({
    components: { MessageBubble },
    setup: () => ({ a: msg('m1', '第一条'), b: msg('m2', '第二条') }),
    template: `
      <div>
        <MessageBubble :msg="a" chat-type="private" :chat-id="10002" chat-name="张三" />
        <MessageBubble :msg="b" chat-type="private" :chat-id="10002" chat-name="张三" />
      </div>`
  })

  return mount(Parent, { global: { plugins: [pinia] } })
}

beforeEach(() => {
  setActivePinia(createPinia())
  useMessageMenu().closeMenu(true)
})

describe('消息操作菜单：全局只能有一个', () => {
  it('右键第二条消息时，第一条的菜单会被关掉', async () => {
    const wrapper = mountTwoBubbles()
    const rows = wrapper.findAll('.msg-row')

    await rows[0].trigger('contextmenu', { clientX: 40, clientY: 40 })
    expect(wrapper.findAll('.ctx-menu')).toHaveLength(1)

    await rows[1].trigger('contextmenu', { clientX: 80, clientY: 80 })

    // 关键断言：仍然只有一个菜单，且挂在第二条消息上
    expect(wrapper.findAll('.ctx-menu')).toHaveLength(1)
    expect(rows[1].find('.ctx-menu').exists()).toBe(true)
    expect(rows[0].find('.ctx-menu').exists()).toBe(false)

    wrapper.unmount()
  })

  it('再次右键同一条消息不会叠加出第二个菜单', async () => {
    const wrapper = mountTwoBubbles()
    const row = wrapper.findAll('.msg-row')[0]

    await row.trigger('contextmenu', { clientX: 10, clientY: 10 })
    await row.trigger('contextmenu', { clientX: 12, clientY: 12 })

    expect(wrapper.findAll('.ctx-menu')).toHaveLength(1)

    wrapper.unmount()
  })

  it('点菜单项后菜单关闭（例如复制）', async () => {
    const wrapper = mountTwoBubbles()
    const row = wrapper.findAll('.msg-row')[0]

    await row.trigger('contextmenu', { clientX: 10, clientY: 10 })
    await row.find('.ctx-item').trigger('click')

    expect(wrapper.findAll('.ctx-menu')).toHaveLength(0)

    wrapper.unmount()
  })

  it('组件卸载时关掉自己的菜单（换会话不留浮层）', async () => {
    const wrapper = mountTwoBubbles()
    const row = wrapper.findAll('.msg-row')[0]

    await row.trigger('contextmenu', { clientX: 10, clientY: 10 })
    expect(wrapper.findAll('.ctx-menu')).toHaveLength(1)

    wrapper.unmount()

    expect(useMessageMenu().isMenuOpen('m1')).toBe(false)
  })
})
