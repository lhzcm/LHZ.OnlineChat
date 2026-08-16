// ==================== API 响应 ====================
export interface ApiResponse<T = any> {
  success: boolean
  message: string
  data: T
}

// ==================== 用户 ====================
export interface UserInfo {
  id: number
  username: string
  nickname: string
  avatar: string | null
}

export interface LoginRequest {
  username: string
  password: string
}

export interface RegisterRequest {
  username: string
  password: string
  nickname: string
}

export interface LoginResponse {
  token: string
  refreshToken: string
  user: UserInfo
}

// ==================== 好友 ====================
export interface FriendInfo {
  userId: number
  username: string
  nickname: string
  avatar: string | null
  isOnline: boolean
  status: number
}

export interface FriendRequestInfo {
  id: number
  userId: number
  username: string
  nickname: string
  avatar: string | null
  createdAt: string
}

// ==================== 群组 ====================
export interface GroupInfo {
  id: number
  name: string
  avatar: string | null
  ownerId: number
  memberCount: number
  createdAt: string
}

export interface GroupMemberInfo {
  userId: number
  username: string
  nickname: string
  avatar: string | null
  role: number
  isOnline: boolean
}

// ==================== 消息 ====================
export interface MessageDto {
  id: number
  senderId: number
  senderName: string
  senderAvatar: string | null
  content: string
  messageType: number
  isRead: boolean
  sentAt: string
}

export interface GroupMessageDto {
  id: number
  groupId: number
  senderId: number
  senderName: string
  senderAvatar: string | null
  content: string
  messageType: number
  sentAt: string
}

export interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

// ==================== WebSocket 消息 ====================
export interface WsMessage {
  type: string
  from: string
  to: string
  content: string
  timestamp: number
  messageId: string
  messageType: number
  senderName: string
  senderAvatar: string | null
}

// ==================== 聊天会话 ====================
export type ChatType = 'private' | 'group'

export interface ChatSession {
  type: ChatType
  id: number
  name: string
  avatar: string | null
  lastMessage: string
  lastTime: string
  unreadCount: number
}
