import axios from 'axios'

// 管理后台 API 客户端：/api 由 nginx 反代到主后端（/api/admin/**）
const request = axios.create({
  baseURL: '/api',
  timeout: 20000
})

request.interceptors.request.use(config => {
  const token = localStorage.getItem('adminToken')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

request.interceptors.response.use(
  response => response.data,
  error => {
    const data = error.response?.data
    if (error.response?.status === 401) {
      localStorage.removeItem('adminToken')
      if (!location.pathname.includes('/login')) {
        location.href = '/admin/login'
      }
    }
    if (data && typeof data === 'object' && typeof data.success === 'boolean') {
      return data
    }
    return Promise.reject(data || error)
  }
)

export default request
