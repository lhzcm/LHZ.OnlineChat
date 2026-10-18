import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import VoiceMessage from '@/components/chat/messages/VoiceMessage.vue'
import MessageComposer from '@/components/chat/MessageComposer.vue'
import { useAuthStore } from '@/stores/auth'
import {
  MAX_VOICE_SECONDS,
  canRecordVoice,
  formatVoiceDuration,
  pickVoiceMimeType,
  voiceExtensionOf
} from '@/utils/voice'

/**
 * 语音消息：工具函数、播放组件、录音状态机。
 *
 * jsdom 没有 MediaRecorder / getUserMedia / 媒体播放实现，这里按需打桩 ——
 * 打桩本身也是"浏览器能力探测"的验证：没能力时按钮必须不出现。
 */
describe('voice 工具函数', () => {
  it('时长格式化为 m:ss，未知时长显示 --:--', () => {
    expect(formatVoiceDuration(0)).toBe('--:--')
    expect(formatVoiceDuration(Number.NaN)).toBe('--:--')
    expect(formatVoiceDuration(3.4)).toBe('0:03')
    expect(formatVoiceDuration(65)).toBe('1:05')
    expect(formatVoiceDuration(MAX_VOICE_SECONDS)).toBe('1:00')
  })

  it('MIME 映射到服务端白名单里的扩展名', () => {
    expect(voiceExtensionOf('audio/webm;codecs=opus')).toBe('webm')
    expect(voiceExtensionOf('audio/ogg;codecs=opus')).toBe('ogg')
    expect(voiceExtensionOf('audio/mp4')).toBe('m4a')
    expect(voiceExtensionOf('audio/mpeg')).toBe('mp3')
    expect(voiceExtensionOf('audio/wav')).toBe('wav')
    // 未知类型兜底 webm（浏览器默认产出）
    expect(voiceExtensionOf('audio/x-unknown')).toBe('webm')
  })

  it('没有 MediaRecorder 的环境不支持录音、也选不出 MIME', () => {
    expect(canRecordVoice()).toBe(false)
    expect(pickVoiceMimeType()).toBeUndefined()
  })
})

describe('VoiceMessage', () => {
  it('渲染音频地址并显示时长占位', () => {
    const wrapper = mount(VoiceMessage, { props: { src: '/uploads/voices/a.webm' } })

    expect(wrapper.find('audio').attributes('src')).toBe('/uploads/voices/a.webm')
    expect(wrapper.find('audio').attributes('preload')).toBe('metadata')
    expect(wrapper.find('.voice-duration').text()).toBe('--:--')
  })

  it('元数据回来之前显示占位，回来后显示时长', async () => {
    const wrapper = mount(VoiceMessage, { props: { src: '/uploads/voices/a.webm' } })
    const audio = wrapper.find('audio').element as HTMLAudioElement
    Object.defineProperty(audio, 'duration', { value: 3.2, configurable: true })

    await wrapper.find('audio').trigger('loadedmetadata')

    expect(wrapper.find('.voice-duration').text()).toBe('0:03')
  })

  it('点击播放/暂停切换状态并驱动 audio.play/pause', async () => {
    const play = vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue(undefined)
    const pause = vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => {})
    const wrapper = mount(VoiceMessage, { props: { src: '/uploads/voices/a.webm' } })

    await wrapper.find('.voice-btn').trigger('click')
    expect(play).toHaveBeenCalledTimes(1)
    expect(wrapper.find('.voice-btn').classes()).toContain('playing')

    await wrapper.find('.voice-btn').trigger('click')
    expect(pause).toHaveBeenCalled()
  })
})

describe('MessageComposer 录音', () => {
  function stubRecorder(supported = true) {
    class FakeMediaRecorder {
      static isTypeSupported = () => supported
      state = 'inactive'
      mimeType = 'audio/webm'
      ondataavailable: ((e: { data: Blob }) => void) | null = null
      onstop: (() => void) | null = null
      start() { this.state = 'recording' }
      stop() {
        this.state = 'inactive'
        this.ondataavailable?.({ data: new Blob(['audio'], { type: 'audio/webm' }) })
        this.onstop?.()
      }
    }
    ;(window as unknown as { MediaRecorder: unknown }).MediaRecorder = FakeMediaRecorder
    Object.defineProperty(navigator, 'mediaDevices', {
      value: { getUserMedia: () => Promise.resolve({ getTracks: () => [] }) },
      configurable: true
    })
  }

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
    delete (window as unknown as { MediaRecorder?: unknown }).MediaRecorder
  })

  it('不支持录音的浏览器不显示录音按钮', () => {
    expect(canRecordVoice()).toBe(false)

    const wrapper = mountComposer()

    expect(wrapper.find('.voice-btn').exists()).toBe(false)
  })

  it('点击开始录音 → 显示录音条；取消后回到输入框', async () => {
    stubRecorder()
    const wrapper = mountComposer()
    expect(wrapper.find('.voice-btn').exists()).toBe(true)

    await wrapper.find('.voice-btn').trigger('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.voice-rec').exists()).toBe(true)
    expect(wrapper.find('input.input').exists()).toBe(false)

    await wrapper.find('.voice-cancel-btn').trigger('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.voice-rec').exists()).toBe(false)
    expect(wrapper.find('input.input').exists()).toBe(true)
  })
})
