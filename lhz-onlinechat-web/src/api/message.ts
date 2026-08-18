import request from './request'
import type { ApiResponse, MessageDto, GroupMessageDto, PagedResult, SessionInfo, MessageSearchResult } from '@/types'

export const messageApi = {
  getPrivateHistory(friendId: number, page = 1, pageSize = 50): Promise<ApiResponse<PagedResult<MessageDto>>> {
    return request.get(`/messages/private/${friendId}`, { params: { page, pageSize } })
  },
  getGroupHistory(groupId: number, page = 1, pageSize = 50): Promise<ApiResponse<PagedResult<GroupMessageDto>>> {
    return request.get(`/messages/group/${groupId}`, { params: { page, pageSize } })
  },
  searchMessages(keyword: string, page = 1, pageSize = 30): Promise<ApiResponse<PagedResult<MessageSearchResult>>> {
    return request.get('/messages/search', { params: { keyword, page, pageSize } })
  },
  getSessions(): Promise<ApiResponse<SessionInfo[]>> {
    return request.get('/messages/sessions')
  },
  updateSessionSetting(type: string, id: number, patch: { isPinned?: boolean; muted?: boolean }): Promise<ApiResponse> {
    return request.put('/messages/session-setting', { type, id, ...patch })
  },
  markGroupRead(groupId: number): Promise<ApiResponse> {
    return request.put(`/messages/group/${groupId}/read`)
  },
  markAsRead(messageId: number): Promise<ApiResponse> {
    return request.put(`/messages/${messageId}/read`)
  },
  markAllAsRead(senderId: number): Promise<ApiResponse> {
    return request.put(`/messages/read-all/${senderId}`)
  },
  getUnreadCount(): Promise<ApiResponse<{ privateUnread: number }>> {
    return request.get('/messages/unread-count')
  },
  getOfflineMessages(): Promise<ApiResponse<MessageDto[]>> {
    return request.get('/messages/offline')
  },
  uploadImage(file: File): Promise<ApiResponse<{ url: string }>> {
    const form = new FormData()
    form.append('file', file)
    return request.post('/uploads/image', form, { headers: { 'Content-Type': undefined } })
  }
}
