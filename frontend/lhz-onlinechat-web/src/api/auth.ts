import request from './request'
import type { ApiResponse, LoginRequest, RegisterRequest, RegisterResponse, SendCodeRequest, SendCodeResponse, LoginResponse, UserInfo, ForgotPasswordRequest, SessionInfoDto } from '@/types'

export const authApi = {
  sendCode(data: SendCodeRequest): Promise<ApiResponse<SendCodeResponse>> {
    return request.post('/auth/send-code', data)
  },
  register(data: RegisterRequest): Promise<ApiResponse<RegisterResponse>> {
    return request.post('/auth/register', data)
  },
  login(data: LoginRequest): Promise<ApiResponse<LoginResponse>> {
    return request.post('/auth/login', data)
  },
  refreshToken(refreshToken: string): Promise<ApiResponse<LoginResponse>> {
    return request.post('/auth/refresh', { refreshToken })
  },
  forgotPassword(data: ForgotPasswordRequest): Promise<ApiResponse> {
    return request.post('/auth/forgot-password', data)
  },
  getSessions(): Promise<ApiResponse<SessionInfoDto[]>> {
    return request.get('/auth/sessions')
  },
  kickSession(sessionId: string): Promise<ApiResponse> {
    return request.delete(`/auth/sessions/${sessionId}`)
  },
  logoutOthers(): Promise<ApiResponse> {
    return request.post('/auth/sessions/logout-others')
  },
  getMe(): Promise<ApiResponse<UserInfo>> {
    return request.get('/auth/me')
  },
  updateProfile(nickname: string): Promise<ApiResponse> {
    return request.put('/auth/profile', { nickname })
  },
  uploadAvatar(file: File): Promise<ApiResponse<{ avatar: string }>> {
    const form = new FormData()
    form.append('file', file)
    // 显式清除 Content-Type，由浏览器生成 multipart boundary
    return request.post('/auth/avatar', form, { headers: { 'Content-Type': undefined } })
  },
  updateEmail(newEmail: string, code: string): Promise<ApiResponse> {
    return request.put('/auth/email', { newEmail, code })
  },
  changePassword(oldPassword: string, newPassword: string): Promise<ApiResponse> {
    return request.put('/auth/password', { oldPassword, newPassword })
  }
}
