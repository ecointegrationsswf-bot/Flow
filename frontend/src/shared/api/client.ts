import axios from 'axios'

export const api = axios.create({ baseURL: '/api' })

api.interceptors.request.use(cfg => {
  const token = localStorage.getItem('token')
  if (token) cfg.headers.Authorization = `Bearer ${token}`
  const tenantId = localStorage.getItem('tenantId')
  if (tenantId) cfg.headers['X-Tenant-Id'] = tenantId
  return cfg
})
