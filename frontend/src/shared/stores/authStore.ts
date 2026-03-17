import { create } from 'zustand'
import { api } from '@/shared/api/client'
import type { UserRole } from '@/shared/types'

interface AuthUser {
  id: string
  fullName: string
  email: string
  role: UserRole
}

interface AuthState {
  token: string | null
  tenantId: string | null
  user: AuthUser | null
  isAuthenticated: boolean
  login: (email: string, password: string) => Promise<void>
  logout: () => void
  hydrate: () => void
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
      user: { id: string; fullName: string; email: string; role: UserRole }
    }>('/auth/login', { email, password })

    localStorage.setItem('token', data.token)
    localStorage.setItem('tenantId', data.tenantId)
    localStorage.setItem('user', JSON.stringify(data.user))

    set({
      token: data.token,
      tenantId: data.tenantId,
      user: data.user,
      isAuthenticated: true,
    })
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
        const user = JSON.parse(userJson) as AuthUser
        set({ token, tenantId, user, isAuthenticated: true })
      } catch {
        set({ token: null, tenantId: null, user: null, isAuthenticated: false })
      }
    }
  },
}))
