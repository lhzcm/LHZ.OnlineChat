import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { groupApi } from '@/api/group'
import { useGroupStore } from '@/stores/group'

/**
 * 群 store 的邀请链接 / 入群申请动作。
 * 这些动作驱动两个弹窗：列表增删错了会"点了通过但列表还在"，
 * 邀请链接状态没同步会让二维码指向旧 token。
 */
const GROUP = 7

function ok<T>(data: T, message = 'ok') {
  return { success: true, message, data } as never
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.restoreAllMocks()
})

describe('邀请链接', () => {
  it('生成后写入 inviteLink', async () => {
    vi.spyOn(groupApi, 'createInviteLink').mockResolvedValue(ok({
      token: 't'.repeat(43), expiresAt: '2026-01-01T00:00:00Z', requiresApproval: true
    }))
    const store = useGroupStore()

    await store.createInviteLink(GROUP)

    expect(store.inviteLink?.token).toBe('t'.repeat(43))
    expect(store.inviteLink?.requiresApproval).toBe(true)
  })

  it('关闭后 token 被清空，但保留审批开关状态', async () => {
    vi.spyOn(groupApi, 'createInviteLink').mockResolvedValue(ok({
      token: 't'.repeat(43), expiresAt: '2026-01-01T00:00:00Z', requiresApproval: false
    }))
    vi.spyOn(groupApi, 'disableInviteLink').mockResolvedValue(ok(undefined))
    const store = useGroupStore()
    await store.createInviteLink(GROUP)

    await store.disableInviteLink(GROUP)

    expect(store.inviteLink?.token).toBeNull()
    expect(store.inviteLink?.requiresApproval).toBe(false)
  })

  it('切换审批开关会同步本地状态', async () => {
    vi.spyOn(groupApi, 'createInviteLink').mockResolvedValue(ok({
      token: 't'.repeat(43), expiresAt: '2026-01-01T00:00:00Z', requiresApproval: true
    }))
    vi.spyOn(groupApi, 'setJoinApproval').mockResolvedValue(ok(undefined))
    const store = useGroupStore()
    await store.createInviteLink(GROUP)

    await store.setJoinApproval(GROUP, false)

    expect(store.inviteLink?.requiresApproval).toBe(false)
  })
})

describe('入群申请', () => {
  const pending = [
    { id: 1, userId: 10002, nickname: '路人甲', avatar: null, message: '想进群', createdAt: '' },
    { id: 2, userId: 10003, nickname: '路人乙', avatar: null, message: null, createdAt: '' }
  ]

  it('拉取待审批列表', async () => {
    vi.spyOn(groupApi, 'getJoinRequests').mockResolvedValue(ok(pending))
    const store = useGroupStore()

    await store.fetchJoinRequests(GROUP)

    expect(store.joinRequests).toHaveLength(2)
  })

  it('通过后从列表移除，并刷新成员', async () => {
    vi.spyOn(groupApi, 'getJoinRequests').mockResolvedValue(ok(pending))
    vi.spyOn(groupApi, 'approveJoinRequest').mockResolvedValue(ok(undefined))
    const membersSpy = vi.spyOn(groupApi, 'getMembers').mockResolvedValue(ok([]))
    const store = useGroupStore()
    await store.fetchJoinRequests(GROUP)

    await store.approveJoinRequest(GROUP, 1)

    expect(store.joinRequests.map(r => r.id)).toEqual([2])
    expect(membersSpy).toHaveBeenCalledWith(GROUP)
  })

  it('拒绝后从列表移除，失败时保留并透传原因', async () => {
    vi.spyOn(groupApi, 'getJoinRequests').mockResolvedValue(ok(pending))
    const rejectSpy = vi.spyOn(groupApi, 'rejectJoinRequest')
      .mockResolvedValueOnce(ok(undefined))
      .mockResolvedValueOnce({ success: false, message: '该申请已处理过了', data: null } as never)
    const store = useGroupStore()
    await store.fetchJoinRequests(GROUP)

    await store.rejectJoinRequest(GROUP, 1)
    expect(store.joinRequests.map(r => r.id)).toEqual([2])

    const failed = await store.rejectJoinRequest(GROUP, 2)
    expect(failed.message).toBe('该申请已处理过了')
    expect(store.joinRequests.map(r => r.id)).toEqual([2])
    expect(rejectSpy).toHaveBeenCalledTimes(2)
  })
})

describe('凭链接加入', () => {
  it('成功时刷新群列表并回传结果', async () => {
    vi.spyOn(groupApi, 'joinByToken').mockResolvedValue(ok({
      groupId: GROUP, groupName: '技术群', joined: false
    }, '已提交申请，请等待管理员审批'))
    const groupsSpy = vi.spyOn(groupApi, 'getMyGroups').mockResolvedValue(ok([]))
    const store = useGroupStore()

    const result = await store.joinByToken('token-token-token-token-token-token', '想进群')

    expect(result.ok).toBe(true)
    expect(result.data?.joined).toBe(false)
    expect(groupsSpy).toHaveBeenCalled()
  })

  it('失败时不刷新群列表，原因透传', async () => {
    vi.spyOn(groupApi, 'joinByToken').mockResolvedValue({
      success: false, message: '邀请链接已失效，请让群主重新生成', data: null
    } as never)
    const groupsSpy = vi.spyOn(groupApi, 'getMyGroups').mockResolvedValue(ok([]))
    const store = useGroupStore()

    const result = await store.joinByToken('expired-token-expired-token')

    expect(result.ok).toBe(false)
    expect(result.message).toContain('已失效')
    expect(groupsSpy).not.toHaveBeenCalled()
  })
})
