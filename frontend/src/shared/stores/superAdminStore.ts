import { create } from 'zustand'
import { adminClient } from '@/shared/api/adminClient'

interface SuperAdminUser {
  id: string
  fullName: string
  email: string
  role: string
}

interface SuperAdminState {
  token: string | null
  user: SuperAdminUser | null
  isAuthenticated: boolean
  login: (email: string, password: string) => Promise<void>
  logout: () => void
  hydrate: () => void
}

// Hydrate initial state synchronously from localStorage
function getInitialState() {
  try {
    const token = localStorage.getItem('sa_token')
    const userJson = localStorage.getItem('sa_user')
    if (token && userJson) {
      const payload = JSON.parse(atob(token.split('.')[1]))
      if (payload.exp * 1000 > Date.now()) {
        const user = JSON.parse(userJson) as SuperAdminUser
        return { token, user, isAuthenticated: true }
      }
      localStorage.removeItem('sa_token')
      localStorage.removeItem('sa_user')
    }
  } catch { /* ignore */ }
  return { token: null, user: null, isAuthenticated: false }
}

const initial = getInitialState()

export const useSuperAdminStore = create<SuperAdminState>((set) => ({
  token: initial.token,
  user: initial.user,
  isAuthenticated: initial.isAuthenticated,

  login: async (email: string, password: string) => {
    const { data } = await adminClient.post<{
      token: string
      user: SuperAdminUser
    }>('/admin/login', { email, password })

    localStorage.setItem('sa_token', data.token)
    localStorage.setItem('sa_user', JSON.stringify(data.user))

    set({
      token: data.token,
      user: data.user,
      isAuthenticated: true,
    })
  },

  logout: () => {
    localStorage.removeItem('sa_token')
    localStorage.removeItem('sa_user')
    set({ token: null, user: null, isAuthenticated: false })
  },

  hydrate: () => {
    const token = localStorage.getItem('sa_token')
    const userJson = localStorage.getItem('sa_user')

    if (token && userJson) {
      try {
        // Verificar que el token no esté expirado
        const payload = JSON.parse(atob(token.split('.')[1]))
        if (payload.exp * 1000 < Date.now()) {
          localStorage.removeItem('sa_token')
          localStorage.removeItem('sa_user')
          set({ token: null, user: null, isAuthenticated: false })
          return
        }
        const user = JSON.parse(userJson) as SuperAdminUser
        set({ token, user, isAuthenticated: true })
      } catch {
        set({ token: null, user: null, isAuthenticated: false })
      }
    }
  },
}))
