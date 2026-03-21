import { Navigate, Outlet } from 'react-router-dom'
import { useAuthStore } from '@/shared/stores/authStore'

function isTokenExpired(token: string): boolean {
  try {
    const payload = JSON.parse(atob(token.split('.')[1]))
    return payload.exp * 1000 < Date.now()
  } catch {
    return true
  }
}

export function ProtectedRoute() {
  const { isAuthenticated, token, logout } = useAuthStore((s) => ({
    isAuthenticated: s.isAuthenticated,
    token: s.token,
    logout: s.logout,
  }))

  if (isAuthenticated && token && isTokenExpired(token)) {
    logout()
    return <Navigate to="/login" replace />
  }

  return isAuthenticated ? <Outlet /> : <Navigate to="/login" replace />
}
