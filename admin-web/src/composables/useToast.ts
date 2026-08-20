import { ref } from 'vue'

/** 轻提示（模块级单例） */
const toastMsg = ref('')
let timer: number | null = null

export function useToast() {
  function toast(msg: string) {
    toastMsg.value = msg
    if (timer) clearTimeout(timer)
    timer = window.setTimeout(() => {
      toastMsg.value = ''
    }, 2200)
  }
  return { toastMsg, toast }
}
