import { describe, expect, it } from 'vitest'
import {
  INVITE_PATH_PREFIX,
  buildInviteUrl,
  isValidInviteToken,
  parseInviteToken
} from '@/utils/inviteLink'

/**
 * 邀请链接的拼装与解析。
 * 这两件事必须成对正确：拼错了扫码打不开，解析错了会把无效链接发去服务端。
 */
const TOKEN = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcd'

describe('buildInviteUrl', () => {
  it('拼成 origin + /join/token', () => {
    expect(buildInviteUrl('https://chat.example.com', TOKEN))
      .toBe(`https://chat.example.com${INVITE_PATH_PREFIX}${TOKEN}`)
  })

  it('origin 末尾的斜杠被去掉（反向代理常带）', () => {
    expect(buildInviteUrl('https://chat.example.com///', TOKEN))
      .toBe(`https://chat.example.com${INVITE_PATH_PREFIX}${TOKEN}`)
  })

  it('局域网 IP + 端口同样可用（二维码要能扫通）', () => {
    expect(buildInviteUrl('http://192.168.1.5:8080', TOKEN))
      .toBe(`http://192.168.1.5:8080${INVITE_PATH_PREFIX}${TOKEN}`)
  })
})

describe('parseInviteToken', () => {
  it('从完整路径取出 token', () => {
    expect(parseInviteToken(`${INVITE_PATH_PREFIX}${TOKEN}`)).toBe(TOKEN)
  })

  it('带查询串 / 尾斜杠 / 绝对 URL 都能取出', () => {
    expect(parseInviteToken(`${INVITE_PATH_PREFIX}${TOKEN}?from=qr`)).toBe(TOKEN)
    expect(parseInviteToken(`${INVITE_PATH_PREFIX}${TOKEN}/`)).toBe(TOKEN)
    expect(parseInviteToken(`https://chat.example.com${INVITE_PATH_PREFIX}${TOKEN}`)).toBe(TOKEN)
  })

  it('不是邀请路径返回 null', () => {
    expect(parseInviteToken('/chat')).toBeNull()
    expect(parseInviteToken('')).toBeNull()
  })

  it('形态不合法的 token 返回 null（不拿脏数据去请求服务端）', () => {
    expect(parseInviteToken(`${INVITE_PATH_PREFIX}short`)).toBeNull()
    expect(parseInviteToken(`${INVITE_PATH_PREFIX}${'a'.repeat(40)}!`)).toBeNull()
  })
})

describe('isValidInviteToken', () => {
  it('接受服务端同款 base64url token', () => {
    expect(isValidInviteToken(TOKEN)).toBe(true)
  })

  it('拒绝过短、过长与非法字符', () => {
    expect(isValidInviteToken('a'.repeat(19))).toBe(false)
    expect(isValidInviteToken('a'.repeat(65))).toBe(false)
    expect(isValidInviteToken(`${'a'.repeat(30)}+`)).toBe(false)
    expect(isValidInviteToken('')).toBe(false)
  })
})
