import request from './request'
import type { ApiResponse, FriendInfo, FriendRequestInfo } from '@/types'

export const friendApi = {
  getFriends(): Promise<ApiResponse<FriendInfo[]>> {
    return request.get('/friends')
  },
  sendRequest(accountId: number): Promise<ApiResponse> {
    return request.post('/friends/request', { accountId })
  },
  acceptRequest(requestId: number): Promise<ApiResponse> {
    return request.put(`/friends/accept/${requestId}`)
  },
  rejectRequest(requestId: number): Promise<ApiResponse> {
    return request.delete(`/friends/reject/${requestId}`)
  },
  deleteFriend(friendId: number): Promise<ApiResponse> {
    return request.delete(`/friends/${friendId}`)
  },
  setRemark(friendId: number, remark: string): Promise<ApiResponse> {
    return request.put(`/friends/${friendId}/remark`, { remark })
  },
  setCategory(friendId: number, category: string): Promise<ApiResponse> {
    return request.put(`/friends/${friendId}/category`, { category })
  },
  getPendingRequests(): Promise<ApiResponse<FriendRequestInfo[]>> {
    return request.get('/friends/pending')
  }
}
