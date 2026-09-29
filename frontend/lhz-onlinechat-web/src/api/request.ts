import axios from 'axios'

const request = axios.create({
  baseURL: '/api',
  timeout: 15000
  // 不设置全局 Content-Type：
  // - JSON body 请求，axios 自动添加 application/json
  // - FormData 上传（头像等），由浏览器自动生成 multipart/form-data; boundary，
  //   避免显式 application/json 导致 415 Unsupported Media Type
})

// 请求拦截器：注入 Token
request.interceptors.request.use(config => {
  const token = localStorage.getItem('token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// 响应拦截器：统一错误处理
request.interceptors.response.use(
  response => response.data,
  error => {
    const data = error.response?.data
    // 401：清除登录态并跳转登录页
    if (error.response?.status === 401) {
      localStorage.removeItem('token')
      localStorage.removeItem('refreshToken')
      window.location.href = '/login'
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
