import axios from 'axios'
import type { AxiosError, InternalAxiosRequestConfig } from 'axios'
import { authApi } from './auth'
import { useAuthStore } from '@/stores/auth'

const request = axios.create({
  baseURL: '/api',
  timeout: 15000
  // 不设置全局 Content-Type：
  // - JSON body 请求，axios 自动添加 application/json
  // - FormData 上传（头像等），由浏览器自动生成 multipart/form-data; boundary，
  //   避免显式 application/json 导致 415 Unsupported Media Type
})

/** 请求配置 + 重试标记：_retry 保证同一请求最多因 401 重放一次，避免无限循环 */
type RetryableConfig = InternalAxiosRequestConfig & { _retry?: boolean }

// 请求拦截器：注入 Token
request.interceptors.request.use(config => {
  const token = localStorage.getItem('token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

/**
 * 单飞刷新：并发 401 共享同一个刷新 Promise。
 * RefreshToken 是一次性的（刷新后服务端立即轮换、旧值失效），
 * 若并发请求各自刷新，只有一个能成功、其余必然把会话打成失效态。
 */
let refreshPromise: Promise<boolean> | null = null

function refreshAccessToken(): Promise<boolean> {
  if (!refreshPromise) {
    refreshPromise = performRefresh().finally(() => { refreshPromise = null })
  }
  return refreshPromise
}

/** 真正执行刷新；返回是否拿到新的令牌 */
async function performRefresh(): Promise<boolean> {
  const rt = localStorage.getItem('refreshToken')
  if (!rt) return false
  try {
    const res = await authApi.refreshToken(rt)
    if (!res?.success || !res.data?.token) return false
    persistTokens(res.data.token, res.data.refreshToken || rt)
    return true
  } catch {
    // 网络错误 / RefreshToken 失效：交由调用方统一清理登录态
    return false
  }
}

/** 刷新成功后同步新的访问 + 刷新令牌（内存态与 localStorage 双写） */
function persistTokens(token: string, refreshToken: string) {
  localStorage.setItem('token', token)
  localStorage.setItem('refreshToken', refreshToken)
  try {
    useAuthStore().setTokens(token, refreshToken)
  } catch {
    // Pinia 尚未初始化（极早期请求）：localStorage 已更新，store 初始化时会读到新值
  }
}

/** 清除登录态并跳转登录页（与既有行为一致） */
function clearAuthAndRedirect() {
  localStorage.removeItem('token')
  localStorage.removeItem('refreshToken')
  try {
    useAuthStore().logout()
  } catch {
    // Pinia 未初始化：localStorage 已清理，跳转后由应用重新初始化
  }
  window.location.href = '/login'
}

/** 登录/刷新接口自身返回 401 时不能再触发刷新，否则会递归刷新 */
function isAuthEntryPoint(url?: string): boolean {
  return !!url && (url.includes('/auth/login') || url.includes('/auth/refresh'))
}

// 响应拦截器：统一错误处理
request.interceptors.response.use(
  response => response.data,
  async (error: AxiosError) => {
    // 后端错误体统一为 ApiResponse（unknown 需要窄化后才能取 success）
    const data = error.response?.data as { success?: boolean } | undefined
    // 401：先静默刷新一次并重放原请求（保留 SPA 状态），刷新失败才清理登录态回登录页
    if (error.response?.status === 401) {
      const config = error.config as RetryableConfig | undefined
      if (config && !config._retry && !isAuthEntryPoint(config.url)) {
        // 先打标记再刷新：重放后的请求若仍 401 也不会再次刷新
        config._retry = true
        if (await refreshAccessToken()) {
          return request(config)
        }
      }
      clearAuthAndRedirect()
      return Promise.reject(data || error)
    }
    // 业务失败（HTTP 4xx + ApiResponse 结构）：正常返回，
    // 由调用方统一根据 res.success 处理，避免调用点各自 try/catch 裸 400
    if (data && typeof data === 'object' && typeof data.success === 'boolean') {
      return data
    }
    return Promise.reject(data || error)
  }
)

export default request
