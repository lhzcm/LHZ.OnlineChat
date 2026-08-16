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
  joinGroup(groupId: number): Promise<ApiResponse> {
    return request.post(`/groups/${groupId}/join`)
  },
  leaveGroup(groupId: number): Promise<ApiResponse> {
    return request.delete(`/groups/${groupId}/leave`)
  },
  kickMember(groupId: number, userId: number): Promise<ApiResponse> {
    return request.delete(`/groups/${groupId}/members/${userId}`)
  },
  dismissGroup(groupId: number): Promise<ApiResponse> {
    return request.delete(`/groups/${groupId}`)
  }
}
