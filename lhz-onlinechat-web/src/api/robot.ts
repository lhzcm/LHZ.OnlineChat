import request from './request'
import type { ApiResponse, RobotInfo, RobotTestResult } from '@/types'

export const robotApi = {
  createRobot(data: { name: string; avatar?: string | null; webhookUrl: string; webhookSecret?: string | null; timeoutMs?: number }): Promise<ApiResponse<RobotInfo>> {
    return request.post('/robots', data)
  },
  getMyRobots(): Promise<ApiResponse<RobotInfo[]>> {
    return request.get('/robots')
  },
  updateRobot(id: number, data: { name?: string; webhookUrl?: string; webhookSecret?: string | null; timeoutMs?: number; enabled?: boolean }): Promise<ApiResponse<RobotInfo>> {
    return request.put(`/robots/${id}`, data)
  },
  deleteRobot(id: number): Promise<ApiResponse> {
    return request.delete(`/robots/${id}`)
  },
  testRobot(id: number, content: string): Promise<ApiResponse<RobotTestResult>> {
    return request.post(`/robots/${id}/test`, { content })
  },
  addGroupRobot(groupId: number, userId: number): Promise<ApiResponse> {
    return request.post(`/groups/${groupId}/robots`, { userId })
  },
  removeGroupRobot(groupId: number, userId: number): Promise<ApiResponse> {
    return request.delete(`/groups/${groupId}/robots/${userId}`)
  },
  getGroupRobots(groupId: number): Promise<ApiResponse<RobotInfo[]>> {
    return request.get(`/groups/${groupId}/robots`)
  }
}
