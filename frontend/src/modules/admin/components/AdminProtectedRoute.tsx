import { Navigate, Outlet } from 'react-router-dom'
import { useSuperAdminStore } from '@/shared/stores/superAdminStore'

export function AdminProtectedRoute() {
  const isAuthenticated = useSuperAdminStore((s) => s.isAuthenticated)
  return isAuthenticated ? <Outlet /> : <Navigate to="/admin/login" replace />
}
