/** 两位补零 */
export function pad(n: number): string {
  return String(n).padStart(2, '0')
}

/**
 * 消息时间显示：
 * - 今天：HH:MM
 * - 今年：M/D HH:MM
 * - 更早：YYYY/M/D HH:MM
 */
export function formatMsgTime(ts: number): string {
  if (!ts) return ''
  const d = new Date(ts)
  const now = new Date()
  const hm = `${pad(d.getHours())}:${pad(d.getMinutes())}`
  if (d.toDateString() === now.toDateString()) return hm
  if (d.getFullYear() === now.getFullYear()) return `${d.getMonth() + 1}/${d.getDate()} ${hm}`
  return `${d.getFullYear()}/${d.getMonth() + 1}/${d.getDate()} ${hm}`
}
