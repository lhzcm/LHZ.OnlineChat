import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { mount } from '@vue/test-utils'
import MessageBubble from '@/components/chat/MessageBubble.vue'
import { useAuthStore } from '@/stores/auth'
import type { ChatMessage } from '@/stores/chat'

/**
 * 气泡组件的分支：文本 / 图片 / Markdown / 已撤回，以及搜索高亮与失败重试入口。
 * 这里验证的是"哪条分支渲染什么"，不是像素长什么样 —— 后者靠人眼。
 */
const ME = 10001

function makeMsg(over: Partial<ChatMessage> = {}): ChatMessage {
  return {
    type: 'private_message',
    from: '10002',
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

function mountBubble(msg: ChatMessage, props: Record<string, unknown> = {}) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore()
  auth.user = { id: ME, nickname: '我', avatar: null } as never

  return mount(MessageBubble, {
    props: { msg, chatType: 'private', chatId: 10002, chatName: '张三', ...props },
    global: { plugins: [pinia] }
  })
}

beforeEach(() => {
  setActivePinia(createPinia())
})

describe('MessageBubble：内容分支', () => {
  it('普通文本走转义 + @提及高亮', () => {
    const html = mountBubble(makeMsg({ content: '你好 @张三' })).html()

    expect(html).toContain('class="mention"')
    expect(html).toContain('你好')
  })

  it('Markdown 消息渲染成文档块（不再是气泡里的纯文本）', () => {
    const html = mountBubble(makeMsg({ content: '# 标题', messageType: 3 })).html()

    expect(html).toContain('msg-markdown')
    expect(html).toContain('<h1')
  })

  it('图片消息渲染 img（点开走 lightbox 事件）', () => {
    const html = mountBubble(makeMsg({ content: '/uploads/a.png', messageType: 1 })).html()

    expect(html).toContain('<img')
    expect(html).toContain('/uploads/a.png')
  })

  it('已撤回：只显示占位文案，不渲染原文', () => {
    const html = mountBubble(makeMsg({ content: '机密原文', isDeleted: true })).html()

    expect(html).toContain('消息已撤回')
    expect(html).not.toContain('机密原文')
  })

  it('自己发出的消息带 mine 样式（用于右侧对齐）', () => {
    const wrapper = mountBubble(makeMsg({ from: String(ME), to: '10002' }))

    expect(wrapper.classes()).toContain('mine')
  })
})

describe('MessageBubble：安全与交互', () => {
  it('文本消息里的 HTML 被转义（不执行）', () => {
    const html = mountBubble(makeMsg({ content: '<script>alert(1)</script>' })).html()

    expect(html).not.toContain('<script')
    expect(html).toContain('&lt;script&gt;')
  })

  it('Markdown 消息里的 HTML 被消毒（组件链路端到端）', () => {
    const html = mountBubble(
      makeMsg({ content: '<img src=x onerror="alert(1)">', messageType: 3 })
    ).html()

    expect(html.toLowerCase()).not.toContain('onerror')
    expect(html.toLowerCase()).not.toContain('<img')
  })

  it('搜索关键词高亮（会话内搜索定位时传入 highlight）', () => {
    const html = mountBubble(makeMsg({ content: '今天部署上线' }), { highlight: '部署' }).html()

    expect(html).toContain('class="search-mark"')
  })

  it('发送失败时给出「重试」入口', () => {
    const html = mountBubble(makeMsg({ from: String(ME), to: '10002', status: 'failed' })).html()

    expect(html).toContain('重试')
  })
})
