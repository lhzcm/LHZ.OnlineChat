/**
 * 群邀请链接的纯函数工具（拼 URL、解析 token、校验形态）。
 *
 * 服务端只存 token、不知道站点域名，所以链接由**浏览器**用当前 origin 拼出来：
 * 用 https 域名访问就得到 https 链接，用局域网 IP 访问就得到 IP 链接 —— 二维码也才扫得通。
 */

/** 邀请页路径前缀（与 router 里的 `/join/:token` 对应） */
export const INVITE_PATH_PREFIX = '/join/'

/** 与服务端 `Group.InviteTokenMinLength / MaxLength` 保持一致 */
export const INVITE_TOKEN_MIN_LENGTH = 20
export const INVITE_TOKEN_MAX_LENGTH = 64

const TOKEN_PATTERN = /^[A-Za-z0-9_-]+$/

/** token 形态是否合法（base64url、长度区间） */
export function isValidInviteToken(token: string): boolean {
  const value = (token || '').trim()
  return value.length >= INVITE_TOKEN_MIN_LENGTH
    && value.length <= INVITE_TOKEN_MAX_LENGTH
    && TOKEN_PATTERN.test(value)
}

/**
 * 拼完整邀请链接：`https://host/join/<token>`。
 * origin 末尾的斜杠要去掉（某些反向代理会带），token 做一次 encodeURIComponent。
 */
export function buildInviteUrl(origin: string, token: string): string {
  const base = (origin || '').replace(/\/+$/, '')
  return `${base}${INVITE_PATH_PREFIX}${encodeURIComponent((token || '').trim())}`
}

/**
 * 从路径里取出 token：`/join/<token>` → `<token>`。
 * 不是邀请路径、token 形态不合法都返回 null（调用方据此显示「链接无效」而不是去请求服务端）。
 */
export function parseInviteToken(path: string): string | null {
  const value = (path || '').trim()
  const index = value.indexOf(INVITE_PATH_PREFIX)
  if (index < 0) return null

  const raw = value.slice(index + INVITE_PATH_PREFIX.length).split(/[/?#]/)[0]
  if (!raw) return null

  const token = decodeURIComponent(raw)
  return isValidInviteToken(token) ? token : null
}
