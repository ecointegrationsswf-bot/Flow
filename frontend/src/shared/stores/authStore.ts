import { create } from 'zustand'
import { api } from '@/shared/api/client'
import type { UserRole } from '@/shared/types'

export interface AuthUser {
  id: string
  fullName: string
  email: string
  role: UserRole
  avatarUrl?: string | null
  permissions: string[]
}

interface AuthState {
  token: string | null
  tenantId: string | null
  user: AuthUser | null
  isAuthenticated: boolean
  login: (email: string, password: string) => Promise<void>
  logout: () => void
  hydrate: () => void
  refreshMe: () => Promise<void>
}

// Decodifica el payload de un JWT.
//
// CRÍTICO: los JWT estándar (RFC 7519) usan **base64url** para codificar
// header y payload, no base64 estándar. base64url usa `-` en vez de `+` y
// `_` en vez de `/`, y omite el padding `=`. `atob()` solo acepta base64
// estándar y lanza `InvalidCharacterError` si encuentra `-` o `_`.
//
// El backend .NET (`JwtSecurityTokenHandler.WriteToken`) emite tokens en
// base64url. Eso significaba que cualquier JWT cuyo payload contuviera un
// caracter URL-safe — y la mayoría los tienen, sobre todo cuando el array
// de permissions es largo — disparaba el catch en `bootInitialState`,
// devolvía estado vacío, y forzaba a re-loguear cada vez que se abría el
// browser.
//
// Esta función convierte base64url → base64 estándar y agrega el padding
// faltante antes de invocar `atob()`.
function decodeJwtPayload(token: string): { exp?: number } | null {
  try {
    const part = token.split('.')[1]
    if (!part) return null
    let b64 = part.replace(/-/g, '+').replace(/_/g, '/')
    while (b64.length % 4 !== 0) b64 += '='
    const json = atob(b64)
    return JSON.parse(json)
  } catch {
    return null
  }
}

// Hidratación SÍNCRONA al cargar el módulo. Si esperamos al useEffect del App,
// el primer render del ProtectedRoute ve isAuthenticated=false y redirige a
// /login antes de que hydrate() corra → forzaba al user a re-loguear cada
// vez que abría el browser, aunque el JWT estuviera válido en localStorage.
function bootInitialState(): {
  token: string | null
  tenantId: string | null
  user: AuthUser | null
  isAuthenticated: boolean
} {
  if (typeof window === 'undefined') {
    return { token: null, tenantId: null, user: null, isAuthenticated: false }
  }
  const token = localStorage.getItem('token')
  const tenantId = localStorage.getItem('tenantId')
  const userJson = localStorage.getItem('user')
  if (!token || !tenantId || !userJson) {
    return { token: null, tenantId: null, user: null, isAuthenticated: false }
  }

  // Si podemos leer el `exp` y está vencido, limpiamos. Si NO podemos
  // decodificar (token corrupto, codificación rara, etc.), preferimos
  // hidratar igual y que el backend decida en la primera request — antes
  // disparábamos catch y mandábamos al user al login aunque tuviera un
  // JWT perfectamente válido.
  const payload = decodeJwtPayload(token)
  if (payload && typeof payload.exp === 'number' && payload.exp * 1000 < Date.now()) {
    localStorage.removeItem('token')
    localStorage.removeItem('tenantId')
    localStorage.removeItem('user')
    return { token: null, tenantId: null, user: null, isAuthenticated: false }
  }

  let user: AuthUser
  try {
    const raw = JSON.parse(userJson)
    user = { ...raw, permissions: raw.permissions ?? [] }
  } catch {
    // user JSON corrupto — limpiamos solo eso y devolvemos vacío.
    localStorage.removeItem('user')
    return { token: null, tenantId: null, user: null, isAuthenticated: false }
  }

  return { token, tenantId, user, isAuthenticated: true }
}

// Tag visible en window para diagnóstico — permite verificar en consola
// del browser que el bundle nuevo está cargado: window.__AUTH_BUILD__
if (typeof window !== 'undefined') {
  ;(window as unknown as { __AUTH_BUILD__: string }).__AUTH_BUILD__ = '2026-05-06-base64url-fix'
}

const initial = bootInitialState()

export const useAuthStore = create<AuthState>((set) => ({
  token: initial.token,
  tenantId: initial.tenantId,
  user: initial.user,
  isAuthenticated: initial.isAuthenticated,

  login: async (email: string, password: string) => {
    const { data } = await api.post<{
      token: string
      tenantId: string
      user: AuthUser
    }>('/auth/login', { email, password })

    const user: AuthUser = { ...data.user, permissions: data.user.permissions ?? [] }
    localStorage.setItem('token', data.token)
    localStorage.setItem('tenantId', data.tenantId)
    localStorage.setItem('user', JSON.stringify(user))

    set({ token: data.token, tenantId: data.tenantId, user, isAuthenticated: true })
  },

  logout: () => {
    localStorage.removeItem('token')
    localStorage.removeItem('tenantId')
    localStorage.removeItem('user')
    set({ token: null, tenantId: null, user: null, isAuthenticated: false })
  },

  hydrate: () => {
    const token = localStorage.getItem('token')
    const tenantId = localStorage.getItem('tenantId')
    const userJson = localStorage.getItem('user')

    if (token && tenantId && userJson) {
      try {
        const raw = JSON.parse(userJson)
        const user: AuthUser = { ...raw, permissions: raw.permissions ?? [] }
        set({ token, tenantId, user, isAuthenticated: true })
      } catch {
        set({ token: null, tenantId: null, user: null, isAuthenticated: false })
      }
    }
  },

  refreshMe: async () => {
    const token = localStorage.getItem('token')
    if (!token) return
    try {
      const { data } = await api.get('/auth/me')
      const user: AuthUser = {
        id: data.id,
        fullName: data.fullName,
        email: data.email,
        role: data.role,
        avatarUrl: data.avatarUrl ?? null,
        permissions: Array.isArray(data.permissions) ? data.permissions : [],
      }
      localStorage.setItem('user', JSON.stringify(user))
      set((state) => ({ ...state, user }))
    } catch {
      // Si falla el refresh, mantener datos existentes (no cerrar sesión)
    }
  },
}))
