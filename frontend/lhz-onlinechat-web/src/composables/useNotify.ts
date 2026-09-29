import { ref } from 'vue'
import type { ChatType } from '@/types'

/**
 * 通知设置（模块级单例，跨组件共享）：
 * - 桌面通知开关（Notification API，页面隐藏时弹窗）
 * - 全局免打扰时段（时段内不弹桌面通知、不响提示音）
 */

const desktopNotifyEnabled = ref(localStorage.getItem('desktopNotify') !== '0')
const dndEnabled = ref(localStorage.getItem('dndEnabled') === '1')
const dndStart = ref(localStorage.getItem('dndStart') || '22:00')
const dndEnd = ref(localStorage.getItem('dndEnd') || '08:00')

function persist() {
  localStorage.setItem('desktopNotify', desktopNotifyEnabled.value ? '1' : '0')
  localStorage.setItem('dndEnabled', dndEnabled.value ? '1' : '0')
  localStorage.setItem('dndStart', dndStart.value)
  localStorage.setItem('dndEnd', dndEnd.value)
}

/** 当前时间是否在免打扰时段内（支持跨午夜，如 22:00 - 08:00） */
export function inDnd(): boolean {
  if (!dndEnabled.value) return false
  const now = new Date()
  const cur = now.getHours() * 60 + now.getMinutes()
  const [sh, sm] = (dndStart.value || '22:00').split(':').map(Number)
  const [eh, em] = (dndEnd.value || '08:00').split(':').map(Number)
  const s = sh * 60 + sm
  const e = eh * 60 + em
  if (s === e) return false // 开始=结束视为未设置
  return s < e ? cur >= s && cur < e : cur >= s || cur < e
}

/** 请求桌面通知权限（须在用户手势中调用） */
export async function requestNotifyPermission(): Promise<boolean> {
  if (!('Notification' in window)) return false
  if (Notification.permission === 'granted') return true
  if (Notification.permission === 'denied') return false
  try {
    const p = await Notification.requestPermission()
    return p === 'granted'
  } catch {
    return false
  }
}

/** 桌面通知是否可用（已授权） */
export function notifyAvailable(): boolean {
  return 'Notification' in window && Notification.permission === 'granted'
}

/**
 * 弹桌面通知（仅页面隐藏时）：title=发送者/会话名，body=内容；
 * 点击通知聚焦窗口并派发 oc:open-session 事件（ChatLayout 监听后打开会话）
 */
export function showDesktopNotify(
  title: string,
  body: string,
  session?: { type: ChatType; id: number }
): void {
  if (!desktopNotifyEnabled.value) return
  if (inDnd()) return
  if (document.visibilityState !== 'hidden') return
  if (!notifyAvailable()) return
  try {
    const n = new Notification(title, {
      body,
      tag: session ? `oc:${session.type}:${session.id}` : 'oc',
      icon: '/icons/icon-192.png',
      badge: '/icons/icon-192.png'
    })
    n.onclick = () => {
      window.focus()
      n.close()
      if (session) {
        window.dispatchEvent(new CustomEvent('oc:open-session', { detail: session }))
      }
    }
  } catch {
    /* 通知创建失败不影响主流程 */
  }
}

export function useNotifySettings() {
  return {
    desktopNotifyEnabled,
    dndEnabled,
    dndStart,
    dndEnd,
    persist
  }
}
