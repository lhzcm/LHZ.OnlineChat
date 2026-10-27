import { describe, expect, it } from 'vitest'
import { localMediaSrc } from '@/utils/media'

/**
 * 图片 / 语音地址白名单：与后端 MessageContentRules 同口径。
 * 服务端已拒收外链，这里守的是历史数据与绕过客户端直连 WS 的内容。
 */
describe('localMediaSrc', () => {
  it.each([
    '/uploads/images/0123abcd.png',
    '/uploads/voices/0123abcd.webm',
    '/uploads/images/a_b-c.1.jpg'
  ])('站内上传地址原样放行：%s', (url) => {
    expect(localMediaSrc(url)).toBe(url)
  })

  it('首尾空白会被修剪', () => {
    expect(localMediaSrc('  /uploads/images/a.png \n')).toBe('/uploads/images/a.png')
  })

  it.each([
    'https://evil.example/t.png',
    'http://evil.example/t.png',
    '//evil.example/t.png',
    'javascript:alert(1)',
    'data:image/png;base64,AAAA',
    '/uploads/a.png',
    '/uploads/images/',
    '/uploads/images/../a.png',
    '/uploads/images/sub/a.png',
    '/uploads/images/a.png?x=1',
    '/uploads/files/a.png',
    '',
    null,
    undefined
  ])('其它一律返回空串：%s', (url) => {
    expect(localMediaSrc(url as string | null | undefined)).toBe('')
  })
})
