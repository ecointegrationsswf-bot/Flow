import { Navigate, Outlet } from 'react-router-dom'
import { useAuthStore } from '@/shared/stores/authStore'

// Decodifica el `exp` del JWT manejando base64url correctamente. atob()
// estándar falla con `-` y `_`, así que hay que normalizar antes. Si no
// se puede decodificar (token corrupto), retornamos `null` para que el
// caller decida — antes asumíamos "expirado" y mandábamos al login a
// users con tokens perfectamente válidos.
function getJwtExpMs(token: string): number | null {
  try {
    const part = token.split('.')[1]
    if (!part) return null
    let b64 = part.replace(/-/g, '+').replace(/_/g, '/')
    while (b64.length % 4 !== 0) b64 += '='
    const payload = JSON.parse(atob(b64))
    return typeof payload.exp === 'number' ? payload.exp * 1000 : null
  } catch {
    return null
  }
}

export function ProtectedRoute() {
  const { isAuthenticated, token, logout } = useAuthStore((s) => ({
    isAuthenticated: s.isAuthenticated,
    token: s.token,
    logout: s.logout,
  }))

  if (isAuthenticated && token) {
    const expMs = getJwtExpMs(token)
    // Solo deslogeamos si pudimos verificar que está vencido. Si la
    // decodificación falla, dejamos pasar y que el backend valide.
    if (expMs !== null && expMs < Date.now()) {
      logout()
      return <Navigate to="/login" replace />
    }
  }

  return isAuthenticated ? <Outlet /> : <Navigate to="/login" replace />
}
