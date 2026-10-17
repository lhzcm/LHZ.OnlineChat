import { describe, expect, it } from 'vitest'
import { decideIncomingAction, type IncomingContext } from '@/utils/unread'

/**
 * 未读策略：决定一条入站消息该「标已读 / 挂红点提示 / 忽略」。
 *
 * 这里的重点是第三条用例：**会话被选中不等于用户看到了消息** ——
 * 窗口切到后台时如果仍按已读处理，用户事后既看不到红点，发送方那边还显示"已读"。
 */
const base: IncomingContext = {
  isCurrentSession: false,
  isMine: false,
  isNew: true,
  muted: false,
  isUserLooking: false
}

describe('decideIncomingAction', () => {
  it('重复到达的消息（同 messageId）直接忽略', () => {
    expect(decideIncomingAction({ ...base, isNew: false })).toBe('ignore')
  })

  it('自己发出的消息回显：不提示、也不标已读', () => {
    expect(decideIncomingAction({
      ...base, isMine: true, isCurrentSession: true, isUserLooking: true
    })).toBe('ignore')
  })

  it('当前会话且窗口在前台：视为已读', () => {
    expect(decideIncomingAction({
      ...base, isCurrentSession: true, isUserLooking: true
    })).toBe('read')
  })

  it('当前会话但窗口在后台/失焦：按未读处理（红点 + 提示音）', () => {
    expect(decideIncomingAction({
      ...base, isCurrentSession: true, isUserLooking: false
    })).toBe('notify')
  })

  it('别的会话有新消息：按未读处理', () => {
    expect(decideIncomingAction(base)).toBe('notify')
  })

  it('免打扰会话不提示', () => {
    expect(decideIncomingAction({ ...base, muted: true })).toBe('ignore')
  })

  it('免打扰但会话正开着且在前台：仍然算已读（在眼前就该清掉）', () => {
    expect(decideIncomingAction({
      ...base, muted: true, isCurrentSession: true, isUserLooking: true
    })).toBe('read')
  })

  it('免打扰且窗口在后台：既不提示也不清未读', () => {
    expect(decideIncomingAction({
      ...base, muted: true, isCurrentSession: true, isUserLooking: false
    })).toBe('ignore')
  })
})
