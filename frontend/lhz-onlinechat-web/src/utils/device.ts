/**
 * 从 UserAgent 解析简短的设备名（多端登录管理展示用）
 * 例：Windows · Chrome、Android · 微信内置浏览器、iPhone · Safari
 */
export function getDeviceName(ua: string = navigator.userAgent): string {
  const platform = detectPlatform(ua)
  const browser = detectBrowser(ua)
  return `${platform} · ${browser}`
}

function detectPlatform(ua: string): string {
  if (/Windows/i.test(ua)) return 'Windows'
  if (/Mac OS X|Macintosh/i.test(ua)) return 'macOS'
  if (/Android/i.test(ua)) return 'Android'
  if (/iPhone|iPad|iPod/i.test(ua)) return 'iPhone'
  if (/Linux/i.test(ua)) return 'Linux'
  return '未知系统'
}

function detectBrowser(ua: string): string {
  if (/MicroMessenger/i.test(ua)) return '微信内置浏览器'
  if (/Edg\//i.test(ua)) return 'Edge'
  if (/OPR\//i.test(ua)) return 'Opera'
  if (/Firefox\//i.test(ua)) return 'Firefox'
  if (/Chrome\//i.test(ua)) return 'Chrome'
  if (/Safari\//i.test(ua)) return 'Safari'
  return '浏览器'
}
