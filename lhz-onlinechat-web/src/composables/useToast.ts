import { ref, onUnmounted } from 'vue'

/**
 * 轻提示 Toast（顶部居中，2.6s 自动消失）
 */
export function useToast() {
  const toastMsg = ref('')
  let toastTimer: number | null = null

  function toast(msg: string) {
    toastMsg.value = msg
    if (toastTimer) clearTimeout(toastTimer)
    toastTimer = window.setTimeout(() => { toastMsg.value = '' }, 2600)
  }

  onUnmounted(() => {
    if (toastTimer) clearTimeout(toastTimer)
  })

  return { toastMsg, toast }
}
