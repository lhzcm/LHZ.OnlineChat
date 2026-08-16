import request from './request'
import type { ApiResponse, LoginRequest, RegisterRequest, LoginResponse, UserInfo } from '@/types'

export const authApi = {
  register(data: RegisterRequest): Promise<ApiResponse> {
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
