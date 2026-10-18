import request from './request'
import type {
  ApiResponse,
  GroupInfo,
  GroupInviteLink,
  GroupInvitePreview,
  GroupJoinRequestInfo,
  GroupMemberInfo,
  JoinGroupResult
} from '@/types'

export const groupApi = {
  getMyGroups(): Promise<ApiResponse<GroupInfo[]>> {
    return request.get('/groups')
  },
  createGroup(name: string): Promise<ApiResponse<GroupInfo>> {
    return request.post('/groups', { name })
  },
  getMembers(groupId: number): Promise<ApiResponse<GroupMemberInfo[]>> {
    return request.get(`/groups/${groupId}/members`)
  },
  inviteMembers(groupId: number, userIds: number[]): Promise<ApiResponse> {
    return request.post(`/groups/${groupId}/invite`, { userIds })
  },
  joinGroup(groupId: number): Promise<ApiResponse> {
    return request.post(`/groups/${groupId}/join`)
  },
  leaveGroup(groupId: number): Promise<ApiResponse> {
    return request.delete(`/groups/${groupId}/leave`)
  },
  kickMember(groupId: number, userId: number): Promise<ApiResponse> {
    return request.delete(`/groups/${groupId}/members/${userId}`)
  },
  setAnnouncement(groupId: number, announcement: string): Promise<ApiResponse> {
    return request.put(`/groups/${groupId}/announcement`, { announcement })
  },
  setAdmin(groupId: number, userId: number, isAdmin: boolean): Promise<ApiResponse> {
    return request.put(`/groups/${groupId}/admin`, { userId, isAdmin })
  },
  dismissGroup(groupId: number): Promise<ApiResponse> {
    return request.delete(`/groups/${groupId}`)
  },

  // ==================== 邀请链接 / 入群审批 ====================

  /** 生成（或重置）邀请链接（仅群主/管理员） */
  createInviteLink(groupId: number): Promise<ApiResponse<GroupInviteLink>> {
    return request.post(`/groups/${groupId}/invite-link`)
  },
  /** 查看当前邀请链接（仅群主/管理员） */
  getInviteLink(groupId: number): Promise<ApiResponse<GroupInviteLink>> {
    return request.get(`/groups/${groupId}/invite-link`)
  },
  /** 关闭邀请链接（仅群主/管理员） */
  disableInviteLink(groupId: number): Promise<ApiResponse> {
    return request.delete(`/groups/${groupId}/invite-link`)
  },
  /** 设置凭链接入群是否需要审批（仅群主/管理员） */
  setJoinApproval(groupId: number, required: boolean): Promise<ApiResponse> {
    return request.put(`/groups/${groupId}/join-approval`, { required })
  },
  /** 待审批的入群申请（仅群主/管理员） */
  getJoinRequests(groupId: number): Promise<ApiResponse<GroupJoinRequestInfo[]>> {
    return request.get(`/groups/${groupId}/join-requests`)
  },
  /** 通过入群申请（仅群主/管理员） */
  approveJoinRequest(groupId: number, requestId: number): Promise<ApiResponse> {
    return request.post(`/groups/${groupId}/join-requests/${requestId}/approve`)
  },
  /** 拒绝入群申请（仅群主/管理员） */
  rejectJoinRequest(groupId: number, requestId: number): Promise<ApiResponse> {
    return request.post(`/groups/${groupId}/join-requests/${requestId}/reject`)
  },
  /** 凭链接预览群信息（邀请页） */
  getInvitePreview(token: string): Promise<ApiResponse<GroupInvitePreview>> {
    return request.get(`/groups/join/${encodeURIComponent(token)}`)
  },
  /** 凭链接加入或提交入群申请 */
  joinByToken(token: string, message?: string): Promise<ApiResponse<JoinGroupResult>> {
    return request.post(`/groups/join/${encodeURIComponent(token)}`, { message })
  }
}
