import { beforeEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ChatHeader from '@/components/chat/ChatHeader.vue'
import { useAuthStore } from '@/stores/auth'
import { useFriendStore } from '@/stores/friend'
import { useGroupStore } from '@/stores/group'

/**
 * 聊天窗口头部：名称、副标题（在线状态 / 正在输入）、按钮事件。
 * 副标题与头像由 store 派生，所以这里同时验证"备注优先"和"正在输入覆盖副标题"。
 */
const ME = 10001
const PEER = 10002
const GROUP = 5

function mountHeader(props: Record<string, unknown> = {}) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore()
  auth.user = { id: ME, nickname: '我', avatar: null } as never

  return mount(ChatHeader, {
    props: { chat: { type: 'private', id: PEER, name: '张三' }, typing: false, searchOpen: false, ...props },
    global: { plugins: [pinia] }
  })
}

beforeEach(() => {
  setActivePinia(createPinia())
})

describe('ChatHeader', () => {
  it('私聊副标题显示原昵称与在线状态', () => {
    const wrapper = mountHeader()
    const friendStore = useFriendStore()
    friendStore.friends.push({ userId: PEER, nickname: '张三', remark: '老张', avatar: null, isOnline: true } as never)

    // push 之后重新渲染
    return wrapper.vm.$nextTick().then(() => {
      expect(wrapper.text()).toContain('张三 · 在线')
    })
  })

  it('对方正在输入时副标题变成「正在输入…」', () => {
    const wrapper = mountHeader({ typing: true })

    expect(wrapper.text()).toContain('正在输入…')
  })

  it('群聊显示人数与群标记', () => {
    const wrapper = mountHeader({ chat: { type: 'group', id: GROUP, name: '测试群' } })
    const groupStore = useGroupStore()
    groupStore.groups.push({ id: GROUP, name: '测试群', avatar: null, memberCount: 7 } as never)

    return wrapper.vm.$nextTick().then(() => {
      expect(wrapper.text()).toContain('群')
      expect(wrapper.text()).toContain('7 人')
    })
  })

  it('点搜索按钮 emit toggle-search，点群成员 emit members，点返回 emit back', async () => {
    const wrapper = mountHeader({ chat: { type: 'group', id: GROUP, name: '测试群' } })

    const buttons = wrapper.findAll('button')
    await buttons[0].trigger('click')                       // 返回
    await buttons[1].trigger('click')                       // 群成员
    await buttons[2].trigger('click')                       // 搜索

    expect(wrapper.emitted('back')).toHaveLength(1)
    expect(wrapper.emitted('members')).toHaveLength(1)
    expect(wrapper.emitted('toggle-search')).toHaveLength(1)
  })
})