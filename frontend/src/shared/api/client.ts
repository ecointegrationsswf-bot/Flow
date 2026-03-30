import axios from 'axios'

export const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api',
})

api.interceptors.request.use(cfg => {
  const token = localStorage.getItem('token')
  if (token) cfg.headers.Authorization = `Bearer ${token}`
  const tenantId = localStorage.getItem('tenantId')
  if (tenantId) cfg.headers['X-Tenant-Id'] = tenantId
  return cfg
})

// Auto-logout on 401 responses (expired/invalid token)
api.interceptors.response.use(
  res => res,
  err => {
    if (err.response?.status === 401 && localStorage.getItem('token')) {
      localStorage.removeItem('token')
      localStorage.removeItem('tenantId')
      localStorage.removeItem('user')
      window.location.href = '/login'
    }
    return Promise.reject(err)
  }
)
