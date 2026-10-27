import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, shallowMount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { messageApi } from '@/api/message'
import { groupApi } from '@/api/group'
import ChatSearchPanel from '@/components/chat/ChatSearchPanel.vue'
import ChatLayout from '@/views/ChatLayout.vue'
import { useAuthStore } from '@/stores/auth'
import { useChatStore } from '@/stores/chat'
import { useGroupStore } from '@/stores/group'
import { useWebSocketStore } from '@/stores/websocket'

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  useRoute: () => ({ params: {}, query: {} })
}))

/**
 * 异步竞态：先发出的慢响应后到，不能盖掉后发出的结果，也不能写进下一个账号的 store。
 * 这类 bug 在界面上表现为"偶发"（串号、列表闪回旧内容），只能靠可控的响应顺序来复现。
 */
function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

function historyPage(page: number, total: number, items: unknown[] = []) {
  return { success: true, data: { items, page, pageSize: 20, total } } as never
}

const peerMsg = (id: number, content = '你好') => ({
  id, messageId: `m${id}`, senderId: 10002, content, messageType: 0, senderName: '张三',
  senderAvatar: null, sentAt: '2026-10-18T00:00:00Z', isDeleted: false, isRead: false
})

beforeEach(() => {
  setActivePinia(createPinia())
})

describe('chat store：历史分页失败后还能重试', () => {
  it('第 2 页请求失败：页码与 hasMore 保持不变，loading 复位，之后可以重试', async () => {
    const store = useChatStore()
    vi.spyOn(messageApi, 'getPrivateHistory').mockResolvedValueOnce(historyPage(1, 100, [peerMsg(1)]))
    await store.loadHistory('private', 10002, 1)
    expect(store.historyMeta.get('private_10002')).toMatchObject({ page: 1, hasMore: true })

    vi.spyOn(messageApi, 'getPrivateHistory').mockRejectedValueOnce(new Error('network'))
    await expect(store.loadMoreHistory('private', 10002)).rejects.toThrow('network')

    const meta = store.historyMeta.get('private_10002')!
    expect(meta).toMatchObject({ page: 1, hasMore: true, loading: false })

    vi.spyOn(messageApi, 'getPrivateHistory').mockResolvedValueOnce(historyPage(2, 100, [peerMsg(2)]))
    await store.loadMoreHistory('private', 10002)
    expect(store.historyMeta.get('private_10002')).toMatchObject({ page: 2, hasMore: true })
    expect(store.messages.get('private_10002')).toHaveLength(2)
  })

  it('业务失败（success=false）同样不把 hasMore 写成 false', async () => {
    const store = useChatStore()
    vi.spyOn(messageApi, 'getPrivateHistory').mockResolvedValueOnce(historyPage(1, 100, [peerMsg(1)]))
    await store.loadHistory('private', 10002, 1)

    vi.spyOn(messageApi, 'getPrivateHistory').mockResolvedValueOnce({ success: false, message: '错误' } as never)
    const hasMore = await store.loadMoreHistory('private', 10002)

    expect(hasMore).toBe(true)
    expect(store.historyMeta.get('private_10002')).toMatchObject({ page: 1, hasMore: true, loading: false })
  })
})

describe('chat store：登出后在途响应不写进新账号的 store', () => {
  it('loadHistory 在 clearAll 之后返回：消息与分页状态都不写入', async () => {
    const store = useChatStore()
    const pending = deferred<never>()
    vi.spyOn(messageApi, 'getPrivateHistory').mockReturnValue(pending.promise)

    const loading = store.loadHistory('private', 10002, 1)
    store.clearAll()
    pending.resolve(historyPage(1, 100, [peerMsg(1, '上一个账号的消息')]))
    await loading

    expect(store.messages.get('private_10002')).toBeUndefined()
    expect(store.historyMeta.get('private_10002')).toBeUndefined()
  })

  it('fetchSessions 在 clearAll 之后返回：会话列表与未读数不写入', async () => {
    const store = useChatStore()
    const pending = deferred<never>()
    vi.spyOn(messageApi, 'getSessions').mockReturnValue(pending.promise)

    const fetching = store.fetchSessions()
    store.clearAll()
    pending.resolve({
      success: true,
      data: [{ type: 'private', id: 10002, name: '张三', unreadCount: 3 }]
    } as never)
    await fetching

    expect(store.sessions).toEqual([])
    expect(store.unreadCounts.size).toBe(0)
  })

  it('loadOfflineMessages 在 clearAll 之后返回：不导入离线消息', async () => {
    const store = useChatStore()
    const pending = deferred<never>()
    vi.spyOn(messageApi, 'getOfflineMessages').mockReturnValue(pending.promise)

    const loading = store.loadOfflineMessages(10001)
    store.clearAll()
    pending.resolve({ success: true, data: [peerMsg(1)] } as never)
    const counts = await loading

    expect(counts.size).toBe(0)
    expect(store.messages.size).toBe(0)
  })
})

describe('group store：成员列表只认最后一次请求', () => {
  it('先发出的慢响应后到，不覆盖当前群的成员', async () => {
    const store = useGroupStore()
    const slow = deferred<never>()
    const fast = deferred<never>()
    vi.spyOn(groupApi, 'getMembers')
      .mockReturnValueOnce(slow.promise)
      .mockReturnValueOnce(fast.promise)

    const first = store.fetchMembers(1)
    const second = store.fetchMembers(2)
    fast.resolve({ success: true, data: [{ userId: 22, nickname: '群2成员' }] } as never)
    await second
    slow.resolve({ success: true, data: [{ userId: 11, nickname: '群1成员' }] } as never)
    await first

    expect(store.members.map(m => m.userId)).toEqual([22])
  })
})

describe('ChatSearchPanel：旧关键词的慢响应不覆盖新结果', () => {
  const chat = { type: 'private' as const, id: 10002, name: '张三' }
  const item = (messageId: string, content: string) => ({
    messageId, sessionId: chat.id, sessionName: '张三', senderName: '张三', content,
    messageType: 0, sentAt: '2026-10-18T00:00:00Z', type: 'private'
  })

  it('先搜 "a"（慢）再搜 "ab"（快）：最终显示 "ab" 的结果', async () => {
    vi.useFakeTimers()
    try {
      const slow = deferred<never>()
      const fast = deferred<never>()
      const spy = vi.spyOn(messageApi, 'searchMessages')
        .mockReturnValueOnce(slow.promise)
        .mockReturnValueOnce(fast.promise)
      const pinia = createPinia()
      setActivePinia(pinia)
      const wrapper = mount(ChatSearchPanel, { props: { chat, open: true }, global: { plugins: [pinia] } })

      await wrapper.find('.search-input').setValue('a')
      await vi.advanceTimersByTimeAsync(350)
      await wrapper.find('.search-input').setValue('ab')
      await vi.advanceTimersByTimeAsync(350)
      expect(spy).toHaveBeenCalledTimes(2)

      fast.resolve({ success: true, data: { items: [item('m-ab', '新结果')], total: 1 } } as never)
      await flushPromises()
      slow.resolve({ success: true, data: { items: [item('m-a', '旧结果')], total: 1 } } as never)
      await flushPromises()

      expect(wrapper.text()).toContain('新结果')
      expect(wrapper.text()).not.toContain('旧结果')
      wrapper.unmount()
    } finally {
      vi.useRealTimers()
    }
  })

  it('换会话后，上一个会话的慢响应被丢弃', async () => {
    vi.useFakeTimers()
    try {
      const slow = deferred<never>()
      vi.spyOn(messageApi, 'searchMessages').mockReturnValueOnce(slow.promise)
      const pinia = createPinia()
      setActivePinia(pinia)
      const wrapper = mount(ChatSearchPanel, { props: { chat, open: true }, global: { plugins: [pinia] } })

      await wrapper.find('.search-input').setValue('a')
      await vi.advanceTimersByTimeAsync(350)
      await wrapper.setProps({ chat: { type: 'private' as const, id: 10003, name: '李四' } })
      slow.resolve({ success: true, data: { items: [item('m-a', '上个会话的结果')], total: 1 } } as never)
      await flushPromises()

      expect(wrapper.text()).not.toContain('上个会话的结果')
      wrapper.unmount()
    } finally {
      vi.useRealTimers()
    }
  })
})

describe('ChatLayout：挂载期间被卸载', () => {
  it('fetchUser 未返回时卸载：不再注册全局监听与 WS 订阅，也不连接', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const auth = useAuthStore()
    auth.token = 'token'
    const pending = deferred<void>()
    vi.spyOn(auth, 'fetchUser').mockReturnValue(pending.promise as never)
    const ws = useWebSocketStore()
    const onMessage = vi.spyOn(ws, 'onMessage')
    const connect = vi.spyOn(ws, 'connect').mockImplementation(() => {})
    const addListener = vi.spyOn(window, 'addEventListener')

    const wrapper = shallowMount(ChatLayout, { global: { plugins: [pinia] } })
    wrapper.unmount()
    pending.resolve()
    await flushPromises()

    expect(addListener.mock.calls.filter(([type]) => type === 'oc:open-session')).toHaveLength(0)
    expect(onMessage).not.toHaveBeenCalled()
    expect(connect).not.toHaveBeenCalled()
  })
})
