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
  /** 是否机器人账号 */
  isBot?: boolean
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

/** 黑名单用户 */
export interface BlacklistUser {
  userId: number
  nickname: string
  avatar: string | null
  blockedAt: string
}

// ==================== 群组 ====================
export interface GroupInfo {
  id: number
  name: string
  avatar: string | null
  ownerId: number
  memberCount: number
  createdAt: string
  /** 群公告（可为空） */
  announcement: string | null
  announcementAt: string | null
  /** 当前用户在该群的角色：0=群主, 1=管理员, 2=成员 */
  myRole: number
}

export interface GroupMemberInfo {
  userId: number
  username: string
  nickname: string
  avatar: string | null
  role: number
  isOnline: boolean
  /** 是否机器人账号 */
  isBot?: boolean
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
  /** 是否已撤回 */
  isDeleted: boolean
  sentAt: string
  /** 客户端消息 ID，与 WS 推送一致（去重键）；为空时前端回退数据库 id */
  messageId: string | null
  /** 被引用消息 ID */
  replyTo: string | null
  /** 被引用消息原文预览 */
  replyContent: string | null
  /** 被引用消息发送者 */
  replySender: string | null
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
  /** 是否已撤回 */
  isDeleted: boolean
  /** 被引用消息 ID */
  replyTo: string | null
  /** 被引用消息原文预览 */
  replyContent: string | null
  /** 被引用消息发送者 */
  replySender: string | null
  /** 被 @ 的成员账号 ID 列表 */
  mentions: number[]
}

export interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

/** 消息搜索结果（私聊 + 群聊聚合） */
export interface MessageSearchResult {
  type: 'private' | 'group'
  sessionId: number
  sessionName: string
  senderName: string
  senderAvatar: string | null
  content: string
  messageType: number
  messageId: string | null
  sentAt: string
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
  /** 是否已撤回 */
  isDeleted?: boolean
  replyTo?: string
  replyContent?: string
  replySender?: string
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
  /** 是否机器人会话（私聊） */
  isBot?: boolean
}

/** 机器人（Webhook） */
export interface RobotInfo {
  id: number
  userId: number
  name: string
  avatar: string | null
  webhookUrl: string
  webhookSecret: string | null
  timeoutMs: number
  enabled: boolean
  createdAt: string
  /** 对外令牌（加密 ID）：第三方推送用 /api/robots/{token}/reply */
  token: string
}

/** 机器人测试结果 */
export interface RobotTestResult {
  success: boolean
  reply: string | null
  message: string
}
