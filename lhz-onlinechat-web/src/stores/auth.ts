import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi } from '@/api/auth'
import type { UserInfo, LoginRequest, RegisterRequest } from '@/types'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<UserInfo | null>(null)
  const token = ref<string>(localStorage.getItem('token') || '')
  const refreshToken = ref<string>(localStorage.getItem('refreshToken') || '')

  const isLoggedIn = computed(() => !!token.value)

  async function register(data: RegisterRequest) {
    const res = await authApi.register(data)
    return res
  }

  async function login(data: LoginRequest) {
    const res = await authApi.login(data)
    if (res.success && res.data) {
      token.value = res.data.token
      refreshToken.value = res.data.refreshToken
      user.value = res.data.user
      localStorage.setItem('token', res.data.token)
      localStorage.setItem('refreshToken', res.data.refreshToken)
    }
    return res
  }

  async function fetchUser() {
    if (!token.value) return
    try {
      const res = await authApi.getMe()
      if (res.success && res.data) {
        user.value = res.data
      }
    } catch {
      logout()
    }
  }

  /** 修改昵称 */
  async function updateProfile(nickname: string) {
    const res = await authApi.updateProfile(nickname)
    if (res.success && user.value) {
      user.value.nickname = nickname.trim()
    }
    return res
  }

  /** 上传头像 */
  async function uploadAvatar(file: File) {
    const res = await authApi.uploadAvatar(file)
    if (res.success && res.data && user.value) {
      user.value.avatar = res.data.avatar
    }
    return res
  }

  /** 换绑邮箱 */
  async function updateEmail(newEmail: string, code: string) {
    const res = await authApi.updateEmail(newEmail, code)
    if (res.success && user.value) {
      user.value.email = newEmail.trim().toLowerCase()
    }
    return res
  }

  function logout() {
    token.value = ''
    refreshToken.value = ''
    user.value = null
    localStorage.removeItem('token')
    localStorage.removeItem('refreshToken')
  }

  return { user, token, refreshToken, isLoggedIn, register, login, fetchUser, updateProfile, uploadAvatar, updateEmail, logout }
})
