import { describe, expect, it } from 'vitest'
import { pad, formatMsgTime } from '@/utils/format'

/** 按文档约定的三个分支算期望值（今天 / 今年 / 更早） */
function expected(ts: number, now = new Date()): string {
  const d = new Date(ts)
  const hm = `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
  if (d.toDateString() === now.toDateString()) return hm
  if (d.getFullYear() === now.getFullYear()) return `${d.getMonth() + 1}/${d.getDate()} ${hm}`
  return `${d.getFullYear()}/${d.getMonth() + 1}/${d.getDate()} ${hm}`
}

describe('pad', () => {
  it('个位数补零', () => {
    expect(pad(0)).toBe('00')
    expect(pad(9)).toBe('09')
  })

  it('两位数原样返回', () => {
    expect(pad(10)).toBe('10')
    expect(pad(59)).toBe('59')
  })
})

describe('formatMsgTime', () => {
  it('时间戳为 0 时返回空串（历史消息缺时间不显示 1970）', () => {
    expect(formatMsgTime(0)).toBe('')
  })

  it('今天只显示 HH:MM', () => {
    const ts = Date.now() - 60_000

    expect(formatMsgTime(ts)).toMatch(/^\d{2}:\d{2}$/)
    expect(formatMsgTime(ts)).toBe(expected(ts))
  })

  it('今年内的更早日期显示 M/D HH:MM', () => {
    // 取今年 1 月 15 日；如果今天正好是 1 月 15 日就退化成"今天"分支，用期望函数兜住
    const now = new Date()
    const ts = new Date(now.getFullYear(), 0, 15, 9, 5).getTime()

    expect(formatMsgTime(ts)).toBe(expected(ts))
  })

  it('往年的日期显示 YYYY/M/D HH:MM', () => {
    const now = new Date()
    const ts = new Date(now.getFullYear() - 1, 2, 8, 21, 30).getTime()

    expect(formatMsgTime(ts)).toBe(`${now.getFullYear() - 1}/3/8 21:30`)
  })

  it('同一天不同时刻只换时分', () => {
    const now = new Date()
    const a = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 8, 0).getTime()
    const b = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 23, 59).getTime()

    expect(formatMsgTime(a)).toBe('08:00')
    expect(formatMsgTime(b)).toBe('23:59')
  })
})
