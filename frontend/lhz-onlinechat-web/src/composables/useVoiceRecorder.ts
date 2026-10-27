import { onUnmounted, ref } from 'vue'
import {
  MAX_VOICE_SECONDS,
  canRecordVoice,
  pickVoiceMimeType,
  voiceExtensionOf
} from '@/utils/voice'

export interface VoiceRecording {
  file: File
  /** 录音秒数（用于直接展示，不必等音频元数据） */
  seconds: number
}

/**
 * 录音状态机：开始 / 停止（发送）/ 取消。
 *
 * 只碰 MediaRecorder 与计时器，上传与发送由调用方（MessageComposer）做 ——
 * 这样录音逻辑可以在没有网络/store 的情况下单独推理，也便于以后复用到别的页面。
 */
export function useVoiceRecorder(options: {
  onComplete: (recording: VoiceRecording) => void
  onError: (message: string) => void
}) {
  const supported = canRecordVoice()
  const recording = ref(false)
  const seconds = ref(0)

  let recorder: MediaRecorder | null = null
  let stream: MediaStream | null = null
  let chunks: BlobPart[] = []
  let timer: number | null = null
  let cancelled = false
  // 正在等 getUserMedia（授权弹窗可能开很久）：recording 要等拿到流才为 true，
  // 这段时间里再点一次或卸载组件，都不能让第二路/无主的麦克风流漏出去
  let starting = false
  let abortStart = false

  function stopTracks() {
    stream?.getTracks().forEach(track => track.stop())
    stream = null
  }

  function reset() {
    if (timer) {
      clearInterval(timer)
      timer = null
    }
    recording.value = false
    stopTracks()
    recorder = null
  }

  async function start() {
    if (!supported || recording.value || starting) return
    starting = true
    abortStart = false
    try {
      const acquired = await navigator.mediaDevices.getUserMedia({ audio: true })
      if (abortStart) {
        // 等授权期间被取消 / 组件已卸载：拿到的流没人接手，必须在这里关掉
        acquired.getTracks().forEach(track => track.stop())
        return
      }
      stream = acquired
    } catch (err) {
      if (abortStart) return
      const name = (err as { name?: string })?.name || ''
      options.onError(
        name === 'NotAllowedError' || name === 'SecurityError'
          ? '没有麦克风权限，请在浏览器设置里允许后重试'
          : name === 'NotFoundError' || name === 'DevicesNotFoundError'
            ? '没有检测到麦克风'
            : '无法开始录音，请检查麦克风'
      )
      reset()
      return
    } finally {
      starting = false
    }

    const mime = pickVoiceMimeType()
    try {
      recorder = mime ? new MediaRecorder(stream, { mimeType: mime }) : new MediaRecorder(stream)
    } catch {
      options.onError('当前浏览器不支持录音')
      reset()
      return
    }

    chunks = []
    cancelled = false
    seconds.value = 0

    recorder.ondataavailable = (event) => {
      if (event.data.size > 0) chunks.push(event.data)
    }
    recorder.onstop = () => {
      const type = recorder?.mimeType || mime || 'audio/webm'
      const duration = Math.min(seconds.value, MAX_VOICE_SECONDS)
      const data = chunks
      reset()
      if (cancelled || data.length === 0) return
      const blob = new Blob(data, { type })
      const file = new File([blob], `voice-${Date.now()}.${voiceExtensionOf(type)}`, { type })
      options.onComplete({ file, seconds: duration })
    }

    recorder.start()
    recording.value = true
    // 到上限自动停（停在 onstop 里走正常发送流程，不打断用户）
    timer = window.setInterval(() => {
      seconds.value += 1
      if (seconds.value >= MAX_VOICE_SECONDS) stop()
    }, 1000)
  }

  /** 停止并交给 onComplete（发送） */
  function stop() {
    if (recorder && recorder.state === 'recording') recorder.stop()
  }

  /** 取消：不产生录音、不回调 onComplete */
  function cancel() {
    cancelled = true
    abortStart = starting
    if (recorder && recorder.state === 'recording') recorder.stop()
    else reset()
  }

  onUnmounted(() => {
    cancelled = true
    abortStart = starting
    if (recorder && recorder.state === 'recording') recorder.stop()
    else reset()
  })

  return { supported, recording, seconds, start, stop, cancel, maxSeconds: MAX_VOICE_SECONDS }
}
