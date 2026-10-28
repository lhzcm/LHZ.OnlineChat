import request from './request'
import type { AxiosProgressEvent } from 'axios'
import type { ApiResponse, FileMeta, MessageDto, GroupMessageDto, PagedResult, SessionInfo, MessageSearchResult } from '@/types'

export const messageApi = {
  getPrivateHistory(friendId: number, page = 1, pageSize = 50): Promise<ApiResponse<PagedResult<MessageDto>>> {
    return request.get(`/messages/private/${friendId}`, { params: { page, pageSize } })
  },
  getGroupHistory(groupId: number, page = 1, pageSize = 50): Promise<ApiResponse<PagedResult<GroupMessageDto>>> {
    return request.get(`/messages/group/${groupId}`, { params: { page, pageSize } })
  },
  searchMessages(keyword: string, page = 1, pageSize = 30, scopeType?: string, scopeId?: number): Promise<ApiResponse<PagedResult<MessageSearchResult>>> {
    return request.get('/messages/search', { params: { keyword, page, pageSize, scopeType, scopeId } })
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
  },
  /** 上传语音（webm/ogg/m4a…；服务端按扩展名白名单 + 10MB 上限校验） */
  uploadVoice(file: File): Promise<ApiResponse<{ url: string }>> {
    const form = new FormData()
    form.append('file', file)
    return request.post('/uploads/voice', form, { headers: { 'Content-Type': undefined } })
  },

  /**
   * 上传聊天文件（≤50MB；服务端按扩展名白名单校验）
   * onProgress 用于输入区进度条：大文件没有反馈时用户会以为卡死。
   */
  uploadFile(
    file: File,
    onProgress?: (percent: number) => void
  ): Promise<ApiResponse<{ url: string; name: string; size: number }>> {
    const form = new FormData()
    form.append('file', file)
    return request.post('/uploads/file', form, {
      headers: { 'Content-Type': undefined },
      onUploadProgress: (e: AxiosProgressEvent) => {
        if (onProgress && e.total) {
          onProgress(Math.min(100, Math.round((e.loaded / e.total) * 100)))
        }
      }
    })
  },

  /** 按 URL 批量取文件元数据（原始文件名 + 大小），一次最多 50 个 */
  getFileMeta(urls: string[]): Promise<ApiResponse<FileMeta[]>> {
    return request.post('/uploads/files/meta', { urls })
  }
}
