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

export const useAuthStore = create<AuthState>((set) => ({
  token: null,
  tenantId: null,
  user: null,
  isAuthenticated: false,

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
