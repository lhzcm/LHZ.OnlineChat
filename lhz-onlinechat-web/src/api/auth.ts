import request from './request'
import type { ApiResponse, LoginRequest, RegisterRequest, RegisterResponse, SendCodeRequest, SendCodeResponse, LoginResponse, UserInfo } from '@/types'

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
  getMe(): Promise<ApiResponse<UserInfo>> {
    return request.get('/auth/me')
  }
}
