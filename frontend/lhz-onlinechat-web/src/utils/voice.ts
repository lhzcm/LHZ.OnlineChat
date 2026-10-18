/**
 * 语音消息的工具函数与常量（纯逻辑，方便单测）。
 *
 * 约定：消息 `content` 是音频地址（和图片一致，不塞 JSON），**时长不落库** ——
 * 渲染时从 `<audio>` 的元数据读。列表预览只需要「[语音]」这个占位。
 */

/** 录音时长上限：超过就自动停止并发送（服务端不解码音频，只守 10MB 体积） */
export const MAX_VOICE_SECONDS = 60

/** 浏览器支持的录音 MIME 优先级：Chrome/Edge/Firefox 用 webm/ogg-opus，Safari 用 mp4 */
const PREFERRED_MIME_TYPES = [
  'audio/webm;codecs=opus',
  'audio/webm',
  'audio/ogg;codecs=opus',
  'audio/ogg',
  'audio/mp4'
]

/** 当前环境是否能录音（jsdom / 老浏览器返回 false，按钮据此隐藏） */
export function canRecordVoice(): boolean {
  if (typeof window === 'undefined' || typeof navigator === 'undefined') return false
  if (typeof window.MediaRecorder === 'undefined') return false
  return typeof navigator.mediaDevices?.getUserMedia === 'function'
}

/**
 * 选一个当前浏览器支持的录音 MIME；都不支持时返回 undefined（交给 MediaRecorder 默认值）。
 * 注意：Safari 只认 audio/mp4，所以不能硬编码 webm。
 */
export function pickVoiceMimeType(): string | undefined {
  const recorder = typeof window !== 'undefined' ? window.MediaRecorder : undefined
  if (!recorder?.isTypeSupported) return undefined
  return PREFERRED_MIME_TYPES.find(type => recorder.isTypeSupported(type))
}

/** MIME → 上传文件扩展名（服务端白名单按扩展名放行） */
export function voiceExtensionOf(mime: string): string {
  const value = (mime || '').toLowerCase()
  if (value.includes('webm')) return 'webm'
  if (value.includes('ogg')) return 'ogg'
  if (value.includes('mp4') || value.includes('m4a') || value.includes('aac')) return 'm4a'
  if (value.includes('mpeg') || value.includes('mp3')) return 'mp3'
  if (value.includes('wav')) return 'wav'
  return 'webm'
}

/** 秒 → `0:07` / `1:05`；未知时长显示 `--:--` */
export function formatVoiceDuration(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds <= 0) return '--:--'
  const total = Math.round(seconds)
  const minutes = Math.floor(total / 60)
  const rest = total % 60
  return `${minutes}:${rest.toString().padStart(2, '0')}`
}
