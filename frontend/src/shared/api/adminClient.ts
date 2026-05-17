import axios from 'axios'
import { attachGlobalErrorInterceptor } from './errorInterceptor'

export const adminClient = axios.create({ baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api' })

adminClient.interceptors.request.use(cfg => {
  const token = localStorage.getItem('sa_token')
  if (token) cfg.headers.Authorization = `Bearer ${token}`
  return cfg
})

// Interceptor global de errores de validación — muestra modal automática
// para cualquier 400/409/422/500. Logout en 401 (existente).
attachGlobalErrorInterceptor(adminClient, {
  onUnauthorized: () => {
    if (localStorage.getItem('sa_token')) {
      localStorage.removeItem('sa_token')
      localStorage.removeItem('sa_user')
      window.location.href = '/admin/login'
    }
  },
})
