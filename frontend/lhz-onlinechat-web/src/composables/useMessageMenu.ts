import { ref } from 'vue'

/**
 * 消息操作菜单的**全局单例**状态。
 *
 * 为什么不能用每个气泡各持一份：每条消息一个 `menu` ref 时，右键 A 弹出 A 的菜单，
 * 再右键 B 只弹 B 的 —— A 的那份没人关，于是"右键几条就叠几个弹窗"。
 * 把"当前打开的是哪条消息 + 位置"收敛到模块级，全局同一时刻只可能有一个菜单。
 *
 * 监听器也只装一份（而不是每个气泡各装一套 document 监听：会话里有上千条消息时那是上千个监听器）。
 */
const openMessageId = ref<string | null>(null)
const position = ref<{ x: number; y: number } | null>(null)
/** 打开时间：长按（touch）后浏览器会补发一次合成 click，刚打开 300ms 内忽略，否则菜单一闪就没 */
let openedAt = 0
let installed = false

/** 菜单约宽高，用于把浮层限制在视口内 */
const MENU_W = 132
const MENU_H = 168

/**
 * 关闭菜单。
 * @param force 菜单项的主动操作（复制/引用/撤回…）传 true，跳过 300ms 防误关
 */
function closeMenu(force = false) {
  if (!force && Date.now() - openedAt < 300) return
  openMessageId.value = null
  position.value = null
}

function installListeners() {
  if (installed || typeof document === 'undefined') return
  installed = true
  // 点别处 / Esc / 滚动 都关闭：菜单是 position: fixed，列表滚动后会停在错误的消息旁边
  document.addEventListener('click', () => closeMenu())
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closeMenu() })
  document.addEventListener('scroll', () => closeMenu(true), true)
}

export function useMessageMenu() {
  installListeners()

  /** 在某个位置打开某条消息的菜单（会替换掉之前打开的） */
  function openMenu(messageId: string, x: number, y: number) {
    openedAt = Date.now()
    openMessageId.value = messageId
    position.value = {
      x: Math.max(8, Math.min(x, window.innerWidth - MENU_W - 8)),
      y: Math.max(8, Math.min(y, window.innerHeight - MENU_H - 8))
    }
  }

  /** 该消息的菜单是否打开（模板里按 messageId 判断，保证只有一条渲染菜单） */
  function isMenuOpen(messageId: string): boolean {
    return openMessageId.value === messageId && position.value !== null
  }

  return { position, openMenu, closeMenu, isMenuOpen }
}
