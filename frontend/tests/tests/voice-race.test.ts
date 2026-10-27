import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import MessageComposer from '@/components/chat/MessageComposer.vue'
import { messageApi } from '@/api/message'
import { useVoiceRecorder } from '@/composables/useVoiceRecorder'
import { useAuthStore } from '@/stores/auth'
import { useChatStore } from '@/stores/chat'
import { useWebSocketStore } from '@/stores/websocket'

// 图片压缩依赖 canvas，jsdom 没有；这里只关心压缩之后的发送路径
vi.mock('@/utils/image', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/utils/image')>()),
  compressChatImage: (file: File) => Promise.resolve(file)
}))

/**
 * 录音与上传的竞态：
 *  - 授权弹窗开着的时候重复点击 / 卸载组件 / 取消，不能漏掉任何一路麦克风；
 *  - 上传期间切换会话，消息必须发给发起上传的那个会话。
 *
 * 假 stream 带真实的 getTracks（每条轨道有 stop 桩），"轨道已关闭"才有可能失败。
 */
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => { resolve = res })
  return { promise, resolve }
}

function fakeStream() {
  const track = { stop: vi.fn() }
  return { track, stream: { getTracks: () => [track] } as unknown as MediaStream }
}

let recorders: FakeMediaRecorder[] = []

class FakeMediaRecorder {
  static isTypeSupported = () => true
  state = 'inactive'
  mimeType = 'audio/webm'
  ondataavailable: ((e: { data: Blob }) => void) | null = null
  onstop: (() => void) | null = null
  constructor() { recorders.push(this) }
  start() { this.state = 'recording' }
  stop() {
    this.state = 'inactive'
    this.ondataavailable?.({ data: new Blob(['audio'], { type: 'audio/webm' }) })
    this.onstop?.()
  }
}

function stubMedia(getUserMedia: () => Promise<MediaStream>) {
  ;(window as unknown as { MediaRecorder: unknown }).MediaRecorder = FakeMediaRecorder
  const spy = vi.fn(getUserMedia)
  Object.defineProperty(navigator, 'mediaDevices', { value: { getUserMedia: spy }, configurable: true })
  return spy
}

beforeEach(() => {
  setActivePinia(createPinia())
  recorders = []
})

describe('useVoiceRecorder：麦克风流不泄漏', () => {
  function mountRecorder() {
    const onComplete = vi.fn()
    const onError = vi.fn()
    let api!: ReturnType<typeof useVoiceRecorder>
    const wrapper = mount(defineComponent({
      setup() {
        api = useVoiceRecorder({ onComplete, onError })
        return () => h('div')
      }
    }))
    return { api, wrapper, onComplete, onError }
  }

  it('授权弹窗未返回时重复点击，只申请一路麦克风', async () => {
    const { stream } = fakeStream()
    const pending = deferred<MediaStream>()
    const getUserMedia = stubMedia(() => pending.promise)
    const { api } = mountRecorder()

    const first = api.start()
    const second = api.start()
    pending.resolve(stream)
    await Promise.all([first, second])

    expect(getUserMedia).toHaveBeenCalledTimes(1)
    expect(recorders).toHaveLength(1)
    expect(api.recording.value).toBe(true)
  })

  it('授权弹窗未返回时卸载组件：事后拿到的流立即关闭，不再建录音器', async () => {
    const { stream, track } = fakeStream()
    const pending = deferred<MediaStream>()
    stubMedia(() => pending.promise)
    const { api, wrapper } = mountRecorder()

    const started = api.start()
    wrapper.unmount()
    pending.resolve(stream)
    await started

    expect(track.stop).toHaveBeenCalledTimes(1)
    expect(recorders).toHaveLength(0)
    expect(api.recording.value).toBe(false)
  })

  it('授权弹窗未返回时取消：同样释放事后拿到的流', async () => {
    const { stream, track } = fakeStream()
    const pending = deferred<MediaStream>()
    stubMedia(() => pending.promise)
    const { api, onComplete } = mountRecorder()

    const started = api.start()
    api.cancel()
    pending.resolve(stream)
    await started

    expect(track.stop).toHaveBeenCalledTimes(1)
    expect(recorders).toHaveLength(0)
    expect(onComplete).not.toHaveBeenCalled()
  })

  it('取消之后可以重新开始录音（取消标记不会残留）', async () => {
    const first = fakeStream()
    const second = fakeStream()
    const queue = [first.stream, second.stream]
    const pending = deferred<MediaStream>()
    stubMedia(() => (queue.length === 2 ? pending.promise : Promise.resolve(queue.shift()!)))
    const { api } = mountRecorder()

    const started = api.start()
    api.cancel()
    queue.shift()
    pending.resolve(first.stream)
    await started
    await api.start()

    expect(api.recording.value).toBe(true)
    expect(first.track.stop).toHaveBeenCalledTimes(1)
    expect(second.track.stop).not.toHaveBeenCalled()
  })

  it('正常结束：轨道被关闭，并回调 onComplete', async () => {
    const { stream, track } = fakeStream()
    stubMedia(() => Promise.resolve(stream))
    const { api, onComplete } = mountRecorder()

    await api.start()
    api.stop()

    expect(track.stop).toHaveBeenCalledTimes(1)
    expect(onComplete).toHaveBeenCalledTimes(1)
    expect(onComplete.mock.calls[0][0].file.name).toMatch(/^voice-\d+\.webm$/)
    expect(api.recording.value).toBe(false)
  })

  it('录音中取消：轨道被关闭，不回调 onComplete', async () => {
    const { stream, track } = fakeStream()
    stubMedia(() => Promise.resolve(stream))
    const { api, onComplete } = mountRecorder()

    await api.start()
    api.cancel()

    expect(track.stop).toHaveBeenCalledTimes(1)
    expect(onComplete).not.toHaveBeenCalled()
  })

  it('录音中卸载组件：轨道被关闭，不回调 onComplete', async () => {
    const { stream, track } = fakeStream()
    stubMedia(() => Promise.resolve(stream))
    const { api, wrapper, onComplete } = mountRecorder()

    await api.start()
    wrapper.unmount()

    expect(track.stop).toHaveBeenCalledTimes(1)
    expect(onComplete).not.toHaveBeenCalled()
  })

  it.each([
    ['NotAllowedError', '没有麦克风权限'],
    ['NotFoundError', '没有检测到麦克风'],
    ['AbortError', '无法开始录音']
  ])('getUserMedia 失败（%s）给出对应提示且不进入录音态', async (name, message) => {
    stubMedia(() => Promise.reject(Object.assign(new Error(name), { name })))
    const { api, onError } = mountRecorder()

    await api.start()

    expect(onError).toHaveBeenCalledWith(expect.stringContaining(message))
    expect(api.recording.value).toBe(false)
  })
})

describe('MessageComposer：上传期间切换会话', () => {
  const ME = 10001
  const chatA = { type: 'private' as const, id: 10002, name: '张三' }
  const chatB = { type: 'private' as const, id: 10003, name: '李四' }

  function mountComposer() {
    const pinia = createPinia()
    setActivePinia(pinia)
    useAuthStore().user = { id: ME, nickname: '我', avatar: null } as never
    const ws = useWebSocketStore()
    ws.connected = true
    vi.spyOn(ws, 'sendMessage').mockReturnValue(true)
    const wrapper = mount(MessageComposer, { props: { chat: chatA }, global: { plugins: [pinia] } })
    return { wrapper, chatStore: useChatStore() }
  }

  it('语音上传期间切到别的会话：语音仍发给原会话，新会话没有消息', async () => {
    stubMedia(() => Promise.resolve(fakeStream().stream))
    const upload = deferred<{ success: boolean; data: { url: string } }>()
    vi.spyOn(messageApi, 'uploadVoice').mockReturnValue(upload.promise as never)
    const { wrapper, chatStore } = mountComposer()

    await wrapper.find('.voice-btn').trigger('click')   // 开始录音
    await flushPromises()
    await wrapper.find('.voice-btn').trigger('click')   // 结束并发送 → 进入上传（挂起）
    await wrapper.setProps({ chat: chatB })             // 用户在上传期间切了会话
    upload.resolve({ success: true, data: { url: '/uploads/voices/v1.webm' } })
    await flushPromises()

    const original = chatStore.messages.get('private_10002')
    expect(original).toHaveLength(1)
    expect(original![0]).toMatchObject({
      messageType: 5, content: '/uploads/voices/v1.webm', to: String(chatA.id), from: String(ME)
    })
    expect(chatStore.messages.get('private_10003')).toBeUndefined()
    // 已经不在原会话了，不应该通知父级去滚动当前（别的）会话
    expect(wrapper.emitted('sent')).toBeUndefined()
  })

  it('图片上传期间切到别的会话：图片仍发给原会话', async () => {
    const upload = deferred<{ success: boolean; data: { url: string } }>()
    vi.spyOn(messageApi, 'uploadImage').mockReturnValue(upload.promise as never)
    const { wrapper, chatStore } = mountComposer()

    const input = wrapper.find('input[type="file"]')
    const file = new File(['x'], 'a.png', { type: 'image/png' })
    Object.defineProperty(input.element, 'files', { value: [file], configurable: true })
    await input.trigger('change')
    await wrapper.setProps({ chat: chatB })
    upload.resolve({ success: true, data: { url: '/uploads/images/i1.png' } })
    await flushPromises()

    expect(chatStore.messages.get('private_10002')![0]).toMatchObject({
      messageType: 1, content: '/uploads/images/i1.png', to: String(chatA.id)
    })
    expect(chatStore.messages.get('private_10003')).toBeUndefined()
  })

  it('没切会话时照常发送并通知父级', async () => {
    stubMedia(() => Promise.resolve(fakeStream().stream))
    vi.spyOn(messageApi, 'uploadVoice').mockResolvedValue(
      { success: true, data: { url: '/uploads/voices/v2.webm' } } as never)
    const { wrapper, chatStore } = mountComposer()

    await wrapper.find('.voice-btn').trigger('click')
    await flushPromises()
    await wrapper.find('.voice-btn').trigger('click')
    await flushPromises()

    expect(chatStore.messages.get('private_10002')).toHaveLength(1)
    expect(wrapper.emitted('sent')).toHaveLength(1)
  })
})
