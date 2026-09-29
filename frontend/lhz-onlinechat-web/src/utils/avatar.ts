/**
 * 头像工具：按名称生成稳定的渐变背景与首字母
 */
const avatarColors = [
  'linear-gradient(135deg, #5b6cff, #9c6bff)',
  'linear-gradient(135deg, #00c6fb, #005bea)',
  'linear-gradient(135deg, #f093fb, #f5576c)',
  'linear-gradient(135deg, #4facfe, #00f2fe)',
  'linear-gradient(135deg, #43e97b, #38b6f9)',
  'linear-gradient(135deg, #fa709a, #fee140)',
  'linear-gradient(135deg, #a18cd1, #fbc2eb)',
  'linear-gradient(135deg, #f83600, #f9d423)'
]

export function avatarGradient(name: string): string {
  let h = 0
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) >>> 0
  return avatarColors[h % avatarColors.length]
}

export function avatarInitial(name: string): string {
  return (name || '?').charAt(0).toUpperCase()
}
