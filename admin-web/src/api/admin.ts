import request from './request'

export interface ApiResponse<T = any> {
  success: boolean
  message: string
  data: T
}

export interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface AdminInfo {
  id: number
  username: string
  role: number
  status: number
  lastLoginAt: string | null
}

export interface AdminUser {
  id: number
  nickname: string
  email: string | null
  avatar: string | null
  isBot: boolean
  isBanned: boolean
  banReason: string | null
  bannedAt: string | null
  createdAt: string
  isOnline: boolean
  friendCount: number
  groupCount: number
  messageCount: number
}

export interface SessionInfoDto {
  sessionId: string
  deviceName: string
  ip: string
  createdAt: number
  lastActiveAt: number
  isCurrent: boolean
}

export interface AdminUserDetail {
  user: AdminUser
  sessions: SessionInfoDto[]
}

export interface DashboardOverview {
  onlineUsers: number
  wsConnections: number
  totalUsers: number
  bannedUsers: number
  totalGroups: number
  totalRobots: number
  totalMessages: number
  todayMessages: number
  todayRegistrations: number
  registerTrend: { date: string; count: number }[]
  messageTrend: { date: string; count: number }[]
}

export interface AdminLog {
  id: number
  adminName: string
  action: string
  targetType: string
  targetId: string | null
  detail: string | null
  ip: string | null
  createdAt: string
}

// ==================== P1：群 / 消息 / 机器人 ====================

export interface AdminGroupDto {
  id: number
  name: string
  avatar: string | null
  ownerId: number
  ownerName: string
  memberCount: number
  messageCount: number
  announcement: string | null
  createdAt: string
}

export interface AdminGroupMemberDto {
  userId: number
  nickname: string
  avatar: string | null
  role: number
  isOnline: boolean
  isBot: boolean
  mutedUntil: string | null
}

export interface AdminGroupDetailDto {
  group: AdminGroupDto
  members: AdminGroupMemberDto[]
}

export interface AdminMessageDto {
  id: number
  messageId: string
  type: 'private' | 'group'
  senderId: number
  senderName: string
  senderAvatar: string | null
  content: string
  messageType: number
  sessionId: number
  isDeleted: boolean
  sentAt: string
}

export interface AdminRobotDto {
  id: number
  userId: number
  name: string
  ownerId: number
  ownerName: string
  webhookUrl: string
  enabled: boolean
  pushCount: number
  callbackFailCount: number
  createdAt: string
}

export const adminApi = {
  login(username: string, password: string): Promise<ApiResponse<{ token: string; admin: AdminInfo }>> {
    return request.post('/admin/auth/login', { username, password })
  },
  me(): Promise<ApiResponse<AdminInfo>> {
    return request.get('/admin/auth/me')
  },
  changePassword(oldPassword: string, newPassword: string): Promise<ApiResponse> {
    return request.put('/admin/auth/password', { oldPassword, newPassword })
  },
  listUsers(params: { keyword?: string; page?: number; pageSize?: number; isBot?: boolean; banned?: boolean }): Promise<ApiResponse<PagedResult<AdminUser>>> {
    return request.get('/admin/users', { params })
  },
  userDetail(userId: number): Promise<ApiResponse<AdminUserDetail>> {
    return request.get(`/admin/users/${userId}`)
  },
  banUser(userId: number, banned: boolean, reason?: string): Promise<ApiResponse> {
    return request.put(`/admin/users/${userId}/ban`, { banned, reason })
  },
  kickUser(userId: number): Promise<ApiResponse> {
    return request.post(`/admin/users/${userId}/kick`)
  },
  resetPassword(userId: number, newPassword: string): Promise<ApiResponse> {
    return request.put(`/admin/users/${userId}/password`, { newPassword })
  },
  dashboard(): Promise<ApiResponse<DashboardOverview>> {
    return request.get('/admin/dashboard/overview')
  },
  listAdmins(): Promise<ApiResponse<AdminInfo[]>> {
    return request.get('/admin/admins')
  },
  createAdmin(username: string, password: string, role: number): Promise<ApiResponse> {
    return request.post('/admin/admins', { username, password, role })
  },
  updateAdmin(id: number, patch: { role?: number; status?: number }): Promise<ApiResponse> {
    return request.put(`/admin/admins/${id}`, patch)
  },
  deleteAdmin(id: number): Promise<ApiResponse> {
    return request.delete(`/admin/admins/${id}`)
  },
  listLogs(params: { page?: number; pageSize?: number; action?: string }): Promise<ApiResponse<PagedResult<AdminLog>>> {
    return request.get('/admin/logs', { params })
  },
  // ==================== P1 ====================
  listGroups(params: { keyword?: string; page?: number; pageSize?: number }): Promise<ApiResponse<PagedResult<AdminGroupDto>>> {
    return request.get('/admin/groups', { params })
  },
  groupDetail(groupId: number): Promise<ApiResponse<AdminGroupDetailDto>> {
    return request.get(`/admin/groups/${groupId}`)
  },
  dissolveGroup(groupId: number): Promise<ApiResponse> {
    return request.delete(`/admin/groups/${groupId}`)
  },
  removeGroupMember(groupId: number, userId: number): Promise<ApiResponse> {
    return request.delete(`/admin/groups/${groupId}/members/${userId}`)
  },
  muteGroupMember(groupId: number, userId: number, mutedUntil: string | null): Promise<ApiResponse> {
    return request.put(`/admin/groups/${groupId}/members/${userId}/mute`, { mutedUntil })
  },
  transferOwner(groupId: number, newOwnerId: number): Promise<ApiResponse> {
    return request.put(`/admin/groups/${groupId}/owner`, { newOwnerId })
  },
  searchMessages(params: { keyword?: string; userId?: number; groupId?: number; page?: number; pageSize?: number }): Promise<ApiResponse<PagedResult<AdminMessageDto>>> {
    return request.get('/admin/messages', { params })
  },
  deleteMessage(type: string, id: number): Promise<ApiResponse> {
    return request.delete(`/admin/messages/${type}/${id}`)
  },
  listRobots(params: { keyword?: string; page?: number; pageSize?: number }): Promise<ApiResponse<PagedResult<AdminRobotDto>>> {
    return request.get('/admin/robots', { params })
  },
  setRobotEnabled(robotId: number, enabled: boolean): Promise<ApiResponse> {
    return request.put(`/admin/robots/${robotId}/status`, { enabled })
  },
  deleteRobot(robotId: number): Promise<ApiResponse> {
    return request.delete(`/admin/robots/${robotId}`)
  }
}
