import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import EmojiPanel from '@/components/chat/composer/EmojiPanel.vue'
import MentionPanel from '@/components/chat/composer/MentionPanel.vue'
import { emojiGroups } from '@/constants/emojis'
import { createPinia } from 'pinia'

/**
 * 表情面板 / @提及面板：纯展示组件。
 * 只验证"渲染出什么"和"选中后抛出什么"——开合、定位、点外面关闭都在 MessageComposer。
 */
describe('EmojiPanel', () => {
  it('按分组渲染全部表情', () => {
    const wrapper = mount(EmojiPanel)

    expect(wrapper.findAll('.emoji-group')).toHaveLength(emojiGroups.length)
    const total = emojiGroups.reduce((n, g) => n + g.list.length, 0)
    expect(wrapper.findAll('.emoji-item')).toHaveLength(total)
  })

  it('点某个表情 emit pick 且值正确', async () => {
    const wrapper = mount(EmojiPanel)
    const first = emojiGroups[0].list[0]

    await wrapper.findAll('.emoji-item')[0].trigger('click')

    expect(wrapper.emitted('pick')).toEqual([[first]])
  })
})

describe('MentionPanel', () => {
  const members = [
    { userId: 1, nickname: '群主甲', avatar: null, role: 0 },
    { userId: 2, nickname: '成员乙', avatar: null, role: 2 }
  ]

  it('渲染成员昵称并给群主加标识', () => {
    const wrapper = mount(MentionPanel, {
      props: { members: members as never },
      global: { plugins: [createPinia()] }
    })

    expect(wrapper.findAll('.mention-item')).toHaveLength(2)
    expect(wrapper.text()).toContain('群主甲')
    // 只有 role=0 的那位有「群主」标签
    expect(wrapper.findAll('.mention-role')).toHaveLength(1)
  })

  it('点成员 emit pick，带完整成员对象', async () => {
    const wrapper = mount(MentionPanel, {
      props: { members: members as never },
      global: { plugins: [createPinia()] }
    })

    await wrapper.findAll('.mention-item')[1].trigger('click')

    expect(wrapper.emitted('pick')).toEqual([[members[1]]])
  })
})
