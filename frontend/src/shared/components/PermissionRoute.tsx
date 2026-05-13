import { Navigate, Outlet } from 'react-router-dom'
import { usePermissions } from '@/shared/hooks/usePermissions'

interface Props {
  permission: string
}

/**
 * Protege rutas según permiso. Admin siempre pasa.
 * Si no tiene acceso, redirige al dashboard.
 */
export function PermissionRoute({ permission }: Props) {
  const { hasPermission } = usePermissions()
  return hasPermission(permission) ? <Outlet /> : <Navigate to="/dashboard" replace />
}
