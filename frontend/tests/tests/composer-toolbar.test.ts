import { beforeEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import MessageComposer from '@/components/chat/MessageComposer.vue'
import { useAuthStore } from '@/stores/auth'

/**
 * 输入区工具栏的折叠行为。
 *
 * 手机端（≤720px）把 MD / @ / 图片 / 文件 / 语音 收进「+」浮层，把宽度让给输入框；
 * 桌面端这些按钮内联显示、「+」隐藏（CSS 控制显隐，jsdom 不跑媒体查询，所以这里断言的是
 * **DOM 结构与开合逻辑**：面板存在、点「+」加 open、选中动作后自动收起）。
 */
function mountComposer() {
  const pinia = createPinia()
  setActivePinia(pinia)
  useAuthStore().user = { id: 10001, nickname: '我', avatar: null } as never
  return mount(MessageComposer, {
    props: { chat: { type: 'private' as const, id: 10002, name: '张三' } },
    global: { plugins: [pinia] }
  })
}

beforeEach(() => {
  setActivePinia(createPinia())
})

describe('输入区工具栏折叠', () => {
  it('MD / 图片 / 文件按钮都在折叠容器里，且单独有一个「+」开关', () => {
    const wrapper = mountComposer()

    const actions = wrapper.find('.composer-actions')
    expect(actions.exists()).toBe(true)
    expect(actions.find('.md-btn').exists()).toBe(true)
    expect(actions.find('input[type="file"]').exists()).toBe(false) // file input 在容器外（隐藏 input 不参与布局）
    expect(actions.findAll('button').length).toBeGreaterThanOrEqual(3)

    // 「+」开关在容器外，桌面上由 CSS 隐藏
    expect(wrapper.find('.more-btn').exists()).toBe(true)
    expect(wrapper.find('.composer-actions .more-btn').exists()).toBe(false)
  })

  it('点「+」加 open，再点收起', async () => {
    const wrapper = mountComposer()

    await wrapper.find('.more-btn').trigger('click')
    expect(wrapper.find('.composer-actions').classes()).toContain('open')

    await wrapper.find('.more-btn').trigger('click')
    expect(wrapper.find('.composer-actions').classes()).not.toContain('open')
  })

  it('从浮层里选动作后自动收起（例如切 Markdown）', async () => {
    const wrapper = mountComposer()
    await wrapper.find('.more-btn').trigger('click')

    await wrapper.find('.composer-actions .md-btn').trigger('click')

    expect(wrapper.find('.composer-actions').classes()).not.toContain('open')
    // 动作本身仍然生效：切到 Markdown 模式后输入框变 textarea
    expect(wrapper.find('textarea.md-input').exists()).toBe(true)
  })

  it('打开「+」浮层会收起表情面板（两者占同一块位置）', async () => {
    const wrapper = mountComposer()
    // 表情按钮在浮层外，先打开表情面板
    const emojiBtn = wrapper.findAll('button').find(b => b.attributes('title') === '表情')!
    await emojiBtn.trigger('click')
    expect(wrapper.find('.emoji-panel').exists()).toBe(true)

    await wrapper.find('.more-btn').trigger('click')

    expect(wrapper.find('.emoji-panel').exists()).toBe(false)
    expect(wrapper.find('.composer-actions').classes()).toContain('open')
  })

  it('点输入框外部收起「+」浮层', async () => {
    const wrapper = mountComposer()
    await wrapper.find('.more-btn').trigger('click')
    expect(wrapper.find('.composer-actions').classes()).toContain('open')

    document.body.click()
    await wrapper.vm.$nextTick()

    expect(wrapper.find('.composer-actions').classes()).not.toContain('open')
  })
})
