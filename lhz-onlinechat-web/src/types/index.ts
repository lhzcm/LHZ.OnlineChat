// ==================== API 响应 ====================
export interface ApiResponse<T = any> {
  success: boolean
  message: string
  data: T
}

// ==================== 用户 ====================
export interface UserInfo {
  id: number
  nickname: string
  avatar: string | null
  email: string
}

export interface SendCodeRequest {
  email: string
}

export interface SendCodeResponse {
  devCode: string | null
  cooldownSeconds: number
}

export interface LoginRequest {
  /** 账号 ID 或邮箱 */
  account: string
  password: string
}

export interface RegisterRequest {
  nickname: string
  email: string
  code: string
  password: string
}

export interface RegisterResponse {
  accountId: number
}

export interface LoginResponse {
  token: string
  refreshToken: string
  user: UserInfo
}

// ==================== 好友 ====================
export interface FriendInfo {
  userId: number
  nickname: string
  avatar: string | null
  isOnline: boolean
  status: number
  /** 我设置的备注（空显示对方昵称） */
  remark: string | null
  /** 我设置的分类标签（空为未分组） */
  category: string | null
}

export interface FriendRequestInfo {
  id: number
  userId: number
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
  /** 客户端消息 ID，与 WS 推送一致（去重键）；为空时前端回退数据库 id */
  messageId: string | null
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
  messageId: string | null
  /** 被 @ 的成员账号 ID 列表 */
  mentions: number[]
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
  /** 被 @ 的成员账号 ID 列表（群聊提及） */
  mentions?: number[]
}

// ==================== 聊天会话 ====================
export type ChatType = 'private' | 'group'

export interface SessionInfo {
  type: ChatType
  id: number
  name: string
  avatar: string | null
  lastMessage: string
  lastTime: string
  unreadCount: number
  isPinned: boolean
  muted: boolean
}
