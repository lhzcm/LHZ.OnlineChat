import { ref } from 'vue'

/**
 * 轻提示 Toast（顶部居中，2.6s 自动消失）
 * 模块级单例：所有调用方共享同一状态，渲染方（如 ChatLayout 的 .app-toast）绑定同一 ref
 */
const toastMsg = ref('')
let toastTimer: number | null = null

export function useToast() {
  function toast(msg: string) {
    toastMsg.value = msg
    if (toastTimer) clearTimeout(toastTimer)
    toastTimer = window.setTimeout(() => { toastMsg.value = '' }, 2600)
  }

  return { toastMsg, toast }
}
