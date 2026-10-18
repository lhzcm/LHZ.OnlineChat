import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import MessageComposer from '@/components/chat/MessageComposer.vue'
import { useAuthStore } from '@/stores/auth'
import { useChatStore } from '@/stores/chat'
import { useWebSocketStore } from '@/stores/websocket'
import type { WsMessage } from '@/types'

/**
 * 消息输入区：发送、Markdown、超长拦截、引用回复、重新编辑、草稿按会话。
 *
 * WS 用 spy 拦掉（不连 socket）；关注的是"点了发送之后本地发生了什么"：
 * 是否乐观上屏、是否清草稿、是否 emit sent、是否带对 reply/messageType。
 */
const ME = 10001
const PEER = 10002
const chat = { type: 'private' as const, id: PEER, name: '张三' }

function mountComposer() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore()
  auth.user = { id: ME, nickname: '我', avatar: null } as never
  const ws = useWebSocketStore()
  const sendSpy = vi.spyOn(ws, 'sendMessage').mockReturnValue(true)

  const wrapper = mount(MessageComposer, {
    props: { chat },
    global: { plugins: [pinia] }
  })
  return { wrapper, sendSpy, chatStore: useChatStore() }
}

beforeEach(() => {
  setActivePinia(createPinia())
})

describe('MessageComposer：发送', () => {
  it('输入后发送：乐观上屏 + 清草稿 + emit sent', async () => {
    const { wrapper, chatStore, sendSpy } = mountComposer()

    await wrapper.find('input.input').setValue('你好')
    await wrapper.find('.send-btn').trigger('click')

    const list = chatStore.messages.get('private_10002')!
    expect(list).toHaveLength(1)
    expect(list[0]).toMatchObject({ content: '你好', messageType: 0, from: String(ME), to: String(PEER) })
    expect(list[0].status).toBe('sending')
    expect(sendSpy).toHaveBeenCalledTimes(1)
    expect(wrapper.emitted('sent')).toHaveLength(1)
    // 草稿清空（普通模式）
    expect(chatStore.getDraft('private_10002').text).toBe('')
  })

  it('Markdown 模式发出的消息 messageType = 3', async () => {
    const { wrapper, chatStore } = mountComposer()

    await wrapper.find('.md-btn').trigger('click')            // 切到 Markdown
    await wrapper.find('textarea.md-input').setValue('# 标题')
    await wrapper.find('.send-btn').trigger('click')

    expect(chatStore.messages.get('private_10002')![0]).toMatchObject({ content: '# 标题', messageType: 3 })
  })

  it('超长内容不发送（不产生消息、不 emit sent）', async () => {
    const { wrapper, chatStore } = mountComposer()

    const long = 'A'.repeat(50001)
    await wrapper.find('input.input').setValue(long)
    await wrapper.find('.send-btn').trigger('click')

    expect(chatStore.messages.get('private_10002')).toBeUndefined()
    expect(wrapper.emitted('sent')).toBeUndefined()
  })

  it('空内容不发送', async () => {
    const { wrapper, chatStore } = mountComposer()

    await wrapper.find('input.input').setValue('   ')
    await wrapper.find('.send-btn').trigger('click')

    expect(chatStore.messages.get('private_10002')).toBeUndefined()
  })
})

describe('MessageComposer：引用与草稿', () => {
  it('reply() 后发送，新消息带 replyTo/replyContent/replySender', async () => {
    const { wrapper, chatStore } = mountComposer()
    const target = { messageId: 'm1', content: '原消息', senderName: '张三' } as WsMessage

    ;(wrapper.vm as unknown as { reply: (msg: WsMessage) => void }).reply(target)
    await wrapper.find('input.input').setValue('收到')
    await wrapper.find('.send-btn').trigger('click')

    expect(chatStore.messages.get('private_10002')![0]).toMatchObject({
      replyTo: 'm1',
      replyContent: '原消息',
      replySender: '张三'
    })
  })


  it('引用语音消息时，引用条显示 [语音] 而不是音频地址', async () => {
    const { wrapper, chatStore } = mountComposer()
    const target = { messageId: 'm9', content: '/uploads/voices/a.webm', messageType: 5, senderName: '张三' } as WsMessage

    ;(wrapper.vm as unknown as { reply: (msg: WsMessage) => void }).reply(target)
    await wrapper.find('input.input').setValue('收到')
    await wrapper.find('.send-btn').trigger('click')

    expect(chatStore.messages.get('private_10002')![0].replyContent).toBe('[语音]')
  })
  it('reedit() 把内容填回输入框（撤回后重新编辑）', async () => {
    const { wrapper } = mountComposer()

    ;(wrapper.vm as unknown as { reedit: (c: string) => void }).reedit('被撤回的内容')
    await wrapper.vm.$nextTick()

    expect((wrapper.find('input.input').element as HTMLInputElement).value).toBe('被撤回的内容')
  })

  it('普通与 Markdown 两份草稿互不影响', async () => {
    const { wrapper, chatStore } = mountComposer()

    await wrapper.find('input.input').setValue('普通草稿')
    await wrapper.find('.md-btn').trigger('click')
    await wrapper.find('textarea.md-input').setValue('**MD 草稿**')

    const draft = chatStore.getDraft('private_10002')
    expect(draft.text).toBe('普通草稿')
    expect(draft.markdownText).toBe('**MD 草稿**')
    expect(draft.markdown).toBe(true)
  })
})