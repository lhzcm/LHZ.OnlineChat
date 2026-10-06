import axios from 'axios'
import type { AxiosError, InternalAxiosRequestConfig } from 'axios'

// 管理后台 API 客户端：/api 由 nginx 反代到主后端（/api/admin/**）
const request = axios.create({
  baseURL: '/api',
  timeout: 20000
})

// 管理后台的令牌存放：访问令牌 adminToken（登录成功后写入），
// 刷新令牌 adminRefreshToken（后端下发时才有；当前 AdminLoginResponse 只返回 token）
const TOKEN_KEY = 'adminToken'
const REFRESH_KEY = 'adminRefreshToken'

/** 请求配置 + 重试标记：_retry 保证同一请求最多因 401 重放一次，避免无限循环 */
type RetryableConfig = InternalAxiosRequestConfig & { _retry?: boolean }

request.interceptors.request.use(config => {
  const token = localStorage.getItem(TOKEN_KEY)
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

// 刷新专用客户端：绕过本文件的响应拦截器，避免刷新请求自身 401 时再次进入刷新逻辑（递归）
const refreshClient = axios.create({
  baseURL: '/api',
  timeout: 20000
})

/**
 * 单飞刷新：并发 401 共享同一个刷新 Promise。
 * 刷新令牌是一次性的（刷新后服务端立即轮换），并发刷新只有一个能成功，
 * 其余会把会话打成失效态。
 */
let refreshPromise: Promise<boolean> | null = null

function refreshAccessToken(): Promise<boolean> {
  if (!refreshPromise) {
    refreshPromise = performRefresh().finally(() => { refreshPromise = null })
  }
  return refreshPromise
}

/** 真正执行刷新；返回是否拿到新的访问令牌 */
async function performRefresh(): Promise<boolean> {
  const rt = localStorage.getItem(REFRESH_KEY)
  // 后端当前未给管理后台下发刷新令牌：拿不到就按"无法刷新"处理，
  // 直接走下面的清态 + 回登录页流程（与既有行为一致）
  if (!rt) return false
  try {
    const res = await refreshClient.post('/admin/auth/refresh', { refreshToken: rt })
    const body = res.data
    if (!body?.success || !body.data?.token) return false
    localStorage.setItem(TOKEN_KEY, body.data.token)
    localStorage.setItem(REFRESH_KEY, body.data.refreshToken || rt)
    return true
  } catch {
    // 网络错误 / 刷新令牌失效：交由调用方清理登录态
    return false
  }
}

/** 并发 401 只跳转一次（避免 N 个请求各自触发一次整页跳转） */
let redirectingToLogin = false

/** 清除登录态并回登录页（与既有行为一致） */
function clearAuthAndRedirect() {
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(REFRESH_KEY)
  if (!location.pathname.includes('/login') && !redirectingToLogin) {
    redirectingToLogin = true
    location.href = '/admin/login'
  }
}

request.interceptors.response.use(
  response => response.data,
  async (error: AxiosError) => {
    // 后端错误体统一为 ApiResponse（unknown 需要窄化后才能取 success）
    const data = error.response?.data as { success?: boolean } | undefined
    if (error.response?.status === 401) {
      const config = error.config as RetryableConfig | undefined
      const url = config?.url || ''
      // 登录/刷新接口自身的 401 不能再触发刷新，否则会递归刷新
      const isAuthEntryPoint = url.includes('/admin/auth/login') || url.includes('/admin/auth/refresh')
      if (config && !config._retry && !isAuthEntryPoint) {
        // 先打标记再刷新：重放后的请求若仍 401 也不会再次刷新
        config._retry = true
        if (await refreshAccessToken()) {
          return request(config)
        }
      }
      clearAuthAndRedirect()
    }
    if (data && typeof data === 'object' && typeof data.success === 'boolean') {
      return data
    }
    return Promise.reject(data || error)
  }
)

export default request
