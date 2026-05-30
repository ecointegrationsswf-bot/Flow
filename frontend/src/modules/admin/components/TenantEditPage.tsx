import { useMemo } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { Loader2 } from 'lucide-react'
import { useAdminTenants } from '@/modules/admin/hooks/useAdminTenants'
import { TenantFormModal, type TenantFormTab } from './TenantFormModal'

const VALID_TABS: ReadonlyArray<TenantFormTab> = [
  'general', 'config', 'prompts', 'actions', 'webhooks', 'labeling', 'campaigns',
]

/**
 * Página completa para crear/editar un cliente (tenant).
 *
 * Rutas que la usan:
 *   /admin/tenants/new                       → modo creación (solo tab General)
 *   /admin/tenants/:id/edit                  → modo edición (tab 'general' por default)
 *   /admin/tenants/:id/edit/:tab             → modo edición con tab seleccionado
 *
 * Reusa <TenantFormModal mode="page" ... /> para no duplicar la lógica del
 * formulario (886 líneas) — el modal y la página comparten el mismo cuerpo y
 * solo difieren en el chrome externo. La tab se sincroniza con la URL para
 * que las pestañas sean linkeables y refrescables.
 */
export function TenantEditPage() {
  const navigate = useNavigate()
  const { id, tab: tabParam } = useParams<{ id?: string; tab?: string }>()
  const isNew = id === 'new' || id === undefined
  const { data: tenants, isLoading } = useAdminTenants()

  // Resolver el tenant editado a partir del listado (evita un hook nuevo).
  // En modo "nuevo" no buscamos nada y el modal arranca con valores vacíos.
  const tenant = useMemo(() => {
    if (isNew) return undefined
    return tenants?.find((t) => t.id === id)
  }, [tenants, id, isNew])

  // Validar el tab del URL. Si el path trae algo inválido, default a 'general'.
  const activeTab: TenantFormTab = useMemo(() => {
    if (!tabParam) return 'general'
    return (VALID_TABS as readonly string[]).includes(tabParam)
      ? (tabParam as TenantFormTab)
      : 'general'
  }, [tabParam])

  const handleTabChange = (next: TenantFormTab) => {
    if (isNew) return // en modo nuevo solo existe General
    navigate(`/admin/tenants/${id}/edit/${next}`, { replace: false })
  }

  const handleClose = () => {
    navigate('/admin/tenants')
  }

  // Loader mientras la lista de tenants carga (solo en edición).
  if (!isNew && isLoading) {
    return (
      <div className="flex h-[calc(100vh-64px)] items-center justify-center">
        <Loader2 className="h-6 w-6 animate-spin text-gray-400" />
      </div>
    )
  }

  // Edición de un id que no existe en la lista.
  if (!isNew && !tenant) {
    return (
      <div className="flex h-[calc(100vh-64px)] flex-col items-center justify-center gap-3 px-6 text-center">
        <p className="text-sm text-gray-600">Cliente no encontrado.</p>
        <button
          onClick={handleClose}
          className="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
        >
          Volver al listado
        </button>
      </div>
    )
  }

  return (
    <TenantFormModal
      tenant={tenant}
      onClose={handleClose}
      mode="page"
      tab={activeTab}
      onTabChange={handleTabChange}
    />
  )
}
