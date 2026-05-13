import { useAuthStore } from '@/shared/stores/authStore'

/**
 * Hook para verificar permisos del usuario autenticado.
 * Los usuarios con rol Admin siempre tienen acceso total.
 */
export function usePermissions() {
  const user = useAuthStore(s => s.user)
  const isAdmin = user?.role === 'Admin'

  const hasPermission = (permissionId: string): boolean => {
    if (!user) return false
    if (isAdmin) return true
    return (user.permissions ?? []).includes(permissionId)
  }

  const hasAnyPermission = (ids: string[]): boolean => {
    if (!user) return false
    if (isAdmin) return true
    return ids.some(id => (user.permissions ?? []).includes(id))
  }

  const hasAllPermissions = (ids: string[]): boolean => {
    if (!user) return false
    if (isAdmin) return true
    return ids.every(id => (user.permissions ?? []).includes(id))
  }

  return { hasPermission, hasAnyPermission, hasAllPermissions, isAdmin, permissions: user?.permissions ?? [] }
}
