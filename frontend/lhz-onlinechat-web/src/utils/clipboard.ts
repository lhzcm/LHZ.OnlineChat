/**
 * 复制文本到剪贴板（带降级）
 *
 * - HTTPS / localhost（secure context）：优先 navigator.clipboard（异步）
 * - 非 HTTPS（http 站点 / IP 访问）：Clipboard API 不可用 → 同步回退
 *   document.execCommand('copy')（隐藏 textarea，在用户手势内执行才有效）
 * - Clipboard API 存在但被权限拒绝：catch 后同样回退
 *
 * @returns 是否复制成功
 */
export async function copyText(text: string): Promise<boolean> {
  // 非 secure context 时 navigator.clipboard 不存在：直接同步降级（仍在用户手势内）
  if (navigator.clipboard && window.isSecureContext) {
    try {
      await navigator.clipboard.writeText(text)
      return true
    } catch {
      // Clipboard API 权限被拒等：继续降级
    }
  }
  return fallbackCopy(text)
}

/** 同步降级复制：隐藏 textarea + execCommand（须在用户手势调用链内） */
function fallbackCopy(text: string): boolean {
  const ta = document.createElement('textarea')
  ta.value = text
  ta.style.position = 'fixed'
  ta.style.top = '-9999px'
  ta.style.opacity = '0'
  ta.setAttribute('readonly', '')
  document.body.appendChild(ta)
  ta.select()
  ta.setSelectionRange(0, text.length)
  let ok = false
  try {
    ok = document.execCommand('copy')
  } catch {
    ok = false
  }
  ta.remove()
  return ok
}
