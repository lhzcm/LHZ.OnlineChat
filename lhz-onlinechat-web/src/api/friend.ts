import request from './request'
import type { ApiResponse, FriendInfo, FriendRequestInfo } from '@/types'

export const friendApi = {
  getFriends(): Promise<ApiResponse<FriendInfo[]>> {
    return request.get('/friends')
  },
  sendRequest(username: string): Promise<ApiResponse> {
    return request.post('/friends/request', { username })
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
  getPendingRequests(): Promise<ApiResponse<FriendRequestInfo[]>> {
    return request.get('/friends/pending')
  }
}
