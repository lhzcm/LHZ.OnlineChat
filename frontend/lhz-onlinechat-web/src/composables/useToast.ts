import { ref } from 'vue'

export type ToastType = 'info' | 'error'

/**
 * 轻提示（顶部居中的浮层卡片，自动消失）。
 * 模块级单例：所有调用方共享同一状态，由 `components/AppToast.vue` 统一渲染。
 *
 * 为什么给错误单独一档：错误往往需要用户去做点什么（开麦克风权限、换更小的图），
 * 2.6 秒读不完；而且颜色与图标要能一眼区分"提示"和"出错了"。
 */
const toastMsg = ref('')
const toastType = ref<ToastType>('info')
let toastTimer: number | null = null

/** 信息 2.6s、错误 4s（错误留够阅读与反应时间，点卡片可立刻关掉） */
const DURATIONS: Record<ToastType, number> = { info: 2600, error: 4000 }

export function useToast() {
  function clearToast() {
    if (toastTimer) {
      clearTimeout(toastTimer)
      toastTimer = null
    }
    toastMsg.value = ''
  }

  function toast(msg: string, type: ToastType = 'info') {
    toastMsg.value = msg
    toastType.value = type
    if (toastTimer) clearTimeout(toastTimer)
    toastTimer = window.setTimeout(() => { toastMsg.value = '' }, DURATIONS[type])
  }

  return { toastMsg, toastType, toast, clearToast }
}
