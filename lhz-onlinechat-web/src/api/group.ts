import request from './request'
import type { ApiResponse, GroupInfo, GroupMemberInfo } from '@/types'

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
  }
}
