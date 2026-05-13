import axios from 'axios'

export const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api',
  timeout: 15000, // 15 segundos máximo por request
})

api.interceptors.request.use(cfg => {
  const token = localStorage.getItem('token')
  if (token) cfg.headers.Authorization = `Bearer ${token}`
  const tenantId = localStorage.getItem('tenantId')
  if (tenantId) cfg.headers['X-Tenant-Id'] = tenantId
  return cfg
})

/**
 * Endpoints "silenciosos" — su 401 NO debe disparar logout automático.
 * Sirve para llamadas de hidratación / refresh / polling de estado que
 * no son acciones explícitas del usuario. Si fallan por 401 (p.ej. red
 * intermitente, server reciclando), preferimos dejar al user con sus
 * datos en localStorage en vez de tirarlo al /login.
 */
const SILENT_401_PATHS = [
  '/auth/me',
  '/auth/ping',
  '/auth/tenant',
]

function isSilent401Url(url: string | undefined): boolean {
  if (!url) return false
  return SILENT_401_PATHS.some(p => url.includes(p))
}

// Auto-logout SOLO en 401 de acciones explícitas. El refresh silencioso
// de /auth/me al cargar la app NO debe deslogear — antes esto provocaba
// que cualquier blip de red al abrir el browser (incluso con JWT válido en
// localStorage) tirara al usuario al login y le pidiera 2FA de nuevo.
api.interceptors.response.use(
  res => res,
  err => {
    const url = err.config?.url as string | undefined
    if (err.response?.status === 401 && localStorage.getItem('token') && !isSilent401Url(url)) {
      localStorage.removeItem('token')
      localStorage.removeItem('tenantId')
      localStorage.removeItem('user')
      window.location.href = '/login'
    }
    return Promise.reject(err)
  }
)
