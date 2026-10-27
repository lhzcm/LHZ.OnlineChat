import { defineStore } from 'pinia'
import { ref } from 'vue'
import { groupApi } from '@/api/group'
import type {
  GroupInfo,
  GroupInviteLink,
  GroupJoinRequestInfo,
  GroupMemberInfo,
  JoinGroupResult
} from '@/types'

export const useGroupStore = defineStore('group', () => {
  const groups = ref<GroupInfo[]>([])
  const members = ref<GroupMemberInfo[]>([])

  // ==================== 邀请链接 / 入群审批 ====================
  /** 当前查看的群的邀请链接状态（仅群主/管理员能拿到） */
  const inviteLink = ref<GroupInviteLink | null>(null)
  /** 当前群的待审批申请 */
  const joinRequests = ref<GroupJoinRequestInfo[]>([])

  async function fetchGroups() {
    const res = await groupApi.getMyGroups()
    if (res.success && res.data) {
      groups.value = res.data
    }
  }

  async function createGroup(name: string) {
    const res = await groupApi.createGroup(name)
    if (res.success && res.data) {
      groups.value.unshift(res.data)
    }
    return res
  }

  // 只认最后发起的那次：连续切换群时，先发出的慢响应后到，会把当前群的成员列表覆盖成上一个群的
  let membersRequestSeq = 0

  async function fetchMembers(groupId: number) {
    const seq = ++membersRequestSeq
    const res = await groupApi.getMembers(groupId)
    if (res.success && res.data && seq === membersRequestSeq) {
      members.value = res.data
    }
    return res
  }

  async function inviteMembers(groupId: number, userIds: number[]) {
    const res = await groupApi.inviteMembers(groupId, userIds)
    return res
  }

  async function joinGroup(groupId: number) {
    const res = await groupApi.joinGroup(groupId)
    if (res.success) {
      await fetchGroups()
    }
    return res
  }

  async function leaveGroup(groupId: number) {
    const res = await groupApi.leaveGroup(groupId)
    if (res.success) {
      groups.value = groups.value.filter(g => g.id !== groupId)
    }
    return res
  }

  async function kickMember(groupId: number, userId: number) {
    const res = await groupApi.kickMember(groupId, userId)
    return res
  }

  async function dismissGroup(groupId: number) {
    const res = await groupApi.dismissGroup(groupId)
    if (res.success) {
      groups.value = groups.value.filter(g => g.id !== groupId)
    }
    return res
  }

  /** 读取邀请链接状态（群主/管理员） */
  async function fetchInviteLink(groupId: number) {
    const res = await groupApi.getInviteLink(groupId)
    if (res.success && res.data) inviteLink.value = res.data
    return res
  }

  /** 生成/重置邀请链接 */
  async function createInviteLink(groupId: number) {
    const res = await groupApi.createInviteLink(groupId)
    if (res.success && res.data) inviteLink.value = res.data
    return res
  }

  /** 关闭邀请链接 */
  async function disableInviteLink(groupId: number) {
    const res = await groupApi.disableInviteLink(groupId)
    if (res.success && inviteLink.value) {
      inviteLink.value = { ...inviteLink.value, token: null, expiresAt: null }
    }
    return res
  }

  /** 开关「凭链接入群需要审批」 */
  async function setJoinApproval(groupId: number, required: boolean) {
    const res = await groupApi.setJoinApproval(groupId, required)
    if (res.success && inviteLink.value) {
      inviteLink.value = { ...inviteLink.value, requiresApproval: required }
    }
    return res
  }

  /** 待审批的入群申请 */
  async function fetchJoinRequests(groupId: number) {
    const res = await groupApi.getJoinRequests(groupId)
    if (res.success && res.data) joinRequests.value = res.data
    return res
  }

  async function approveJoinRequest(groupId: number, requestId: number) {
    const res = await groupApi.approveJoinRequest(groupId, requestId)
    if (res.success) {
      joinRequests.value = joinRequests.value.filter(r => r.id !== requestId)
      // 成员数会变，顺手刷新成员列表
      await fetchMembers(groupId)
    }
    return res
  }

  async function rejectJoinRequest(groupId: number, requestId: number) {
    const res = await groupApi.rejectJoinRequest(groupId, requestId)
    if (res.success) {
      joinRequests.value = joinRequests.value.filter(r => r.id !== requestId)
    }
    return res
  }

  /** 凭 token 预览群信息（邀请页） */
  async function fetchInvitePreview(token: string) {
    return groupApi.getInvitePreview(token)
  }

  /** 凭 token 加入或提交申请 */
  async function joinByToken(token: string, message?: string): Promise<{
    ok: boolean
    message: string
    data: JoinGroupResult | null
  }> {
    const res = await groupApi.joinByToken(token, message)
    if (res.success) await fetchGroups()
    return { ok: res.success, message: res.message, data: res.data ?? null }
  }

  return {
    groups,
    members,
    inviteLink,
    joinRequests,
    fetchGroups,
    createGroup,
    fetchMembers,
    inviteMembers,
    joinGroup,
    leaveGroup,
    kickMember,
    dismissGroup,
    fetchInviteLink,
    createInviteLink,
    disableInviteLink,
    setJoinApproval,
    fetchJoinRequests,
    approveJoinRequest,
    rejectJoinRequest,
    fetchInvitePreview,
    joinByToken
  }
})
