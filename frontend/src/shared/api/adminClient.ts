import axios from 'axios'

export const adminClient = axios.create({ baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api' })

adminClient.interceptors.request.use(cfg => {
  const token = localStorage.getItem('sa_token')
  if (token) cfg.headers.Authorization = `Bearer ${token}`
  return cfg
})

// Auto-logout on 401 responses (expired/invalid token)
adminClient.interceptors.response.use(
  res => res,
  err => {
    if (err.response?.status === 401 && localStorage.getItem('sa_token')) {
      localStorage.removeItem('sa_token')
      localStorage.removeItem('sa_user')
      window.location.href = '/admin/login'
    }
    return Promise.reject(err)
  }
)
