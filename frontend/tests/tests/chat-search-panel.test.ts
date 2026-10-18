import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { messageApi } from '@/api/message'
import ChatSearchPanel from '@/components/chat/ChatSearchPanel.vue'

/**
 * 会话内搜索：打开时聚焦、防抖搜索、点结果 emit locate（定位与高亮是父级的事）。
 * 防抖是 300ms，这里等 350ms 验证真实调用（不引入假时钟，避免与 nextTick 打架）。
 */
const chat = { type: 'private' as const, id: 10002, name: '张三' }

function mountPanel(open = true) {
  const pinia = createPinia()
  setActivePinia(pinia)
  return mount(ChatSearchPanel, {
    props: { chat, open },
    global: { plugins: [pinia] },
    attachTo: document.body
  })
}

beforeEach(() => {
  setActivePinia(createPinia())
})

describe('ChatSearchPanel', () => {
  it('打开时搜索框获得焦点', async () => {
    const wrapper = mountPanel()
    await wrapper.vm.$nextTick()

    const input = wrapper.find('.search-input').element
    expect(document.activeElement).toBe(input)
    wrapper.unmount()
  })

  it('输入关键词后（防抖 300ms）调用搜索并渲染结果', async () => {
    const spy = vi.spyOn(messageApi, 'searchMessages').mockResolvedValue({
      success: true,
      data: {
        items: [{ messageId: 'm1', sessionId: chat.id, sessionName: '张三', senderName: '张三', content: '部署完成', messageType: 0, sentAt: '2026-10-18T00:00:00Z', type: 'private' }],
        total: 1
      }
    } as never)

    const wrapper = mountPanel()
    await wrapper.find('.search-input').setValue('部署')
    await new Promise(r => setTimeout(r, 350))
    await wrapper.vm.$nextTick()

    expect(spy).toHaveBeenCalledWith('部署', 1, 20, 'private', chat.id)
    expect(wrapper.findAll('.search-result-item')).toHaveLength(1)
    expect(wrapper.text()).toContain('部署完成')
    wrapper.unmount()
  })

  it('点结果 emit locate，带消息 ID 与关键词', async () => {
    vi.spyOn(messageApi, 'searchMessages').mockResolvedValue({
      success: true,
      data: {
        items: [{ messageId: 'm9', sessionId: chat.id, sessionName: '张三', senderName: '张三', content: '目标消息', messageType: 0, sentAt: '2026-10-18T00:00:00Z', type: 'private' }],
        total: 1
      }
    } as never)

    const wrapper = mountPanel()
    await wrapper.find('.search-input').setValue('目标')
    await new Promise(r => setTimeout(r, 350))
    await wrapper.vm.$nextTick()
    await wrapper.find('.search-result-item').trigger('click')

    expect(wrapper.emitted('locate')).toEqual([['m9', '目标']])
    wrapper.unmount()
  })

  it('点关闭按钮 emit close', async () => {
    const wrapper = mountPanel()

    await wrapper.find('.search-close').trigger('click')

    expect(wrapper.emitted('close')).toHaveLength(1)
    wrapper.unmount()
  })
})
