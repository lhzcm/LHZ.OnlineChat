import request from './request'
import type { ApiResponse, BlacklistUser } from '@/types'

export const blacklistApi = {
  getList(): Promise<ApiResponse<BlacklistUser[]>> {
    return request.get('/blacklist')
  },
  block(userId: number): Promise<ApiResponse> {
    return request.post('/blacklist', { accountId: userId })
  },
  unblock(userId: number): Promise<ApiResponse> {
    return request.delete(`/blacklist/${userId}`)
  }
}
