import { useState } from 'react'
import { X, Loader2, ArrowRightToLine, RefreshCw } from 'lucide-react'
import { useAdminTenants } from '@/modules/admin/hooks/useAdminTenants'
import {
  useMigrateAgentTemplate,
  type AgentTemplate,
} from '@/modules/admin/hooks/useAdminAgentTemplates'

interface Props {
  template: AgentTemplate
  onClose: () => void
}

export function MigrateTemplateModal({ template, onClose }: Props) {
  const { data: tenants, isLoading: loadingTenants } = useAdminTenants()
  const migrateMut = useMigrateAgentTemplate()
  const [selectedTenantId, setSelectedTenantId] = useState('')
  const [mode, setMode] = useState<'migrate' | 'update'>('migrate')
  const [success, setSuccess] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const activeTenants = tenants?.filter((t) => t.isActive) ?? []

  const handleAction = async () => {
    if (!selectedTenantId) return
    setError(null)
    setSuccess(null)
    try {
      const result = await migrateMut.mutateAsync({
        templateId: template.id,
        tenantId: selectedTenantId,
        update: mode === 'update',
      })
      const tenantName = activeTenants.find((t) => t.id === selectedTenantId)?.name
      const actionText = (result as { action?: string }).action === 'updated' ? 'actualizado en' : 'migrado a'
      setSuccess(`Agente "${template.name}" ${actionText} ${tenantName} exitosamente.`)
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { error?: string } } }
      setError(axiosErr.response?.data?.error ?? 'Error al ejecutar.')
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-md rounded-lg bg-white shadow-xl">
        <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
          <h2 className="text-lg font-semibold text-gray-900">Migrar / Actualizar Plantilla</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="space-y-4 px-6 py-4">
          <div className="rounded-md bg-gray-50 p-3">
            <p className="text-sm font-medium text-gray-900">{template.name}</p>
            <p className="text-xs text-gray-500">{template.category}</p>
          </div>

          {/* Mode selector */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">Accion</label>
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => setMode('migrate')}
                className={`flex-1 flex items-center justify-center gap-2 rounded-md px-3 py-2 text-sm font-medium border ${
                  mode === 'migrate'
                    ? 'border-amber-500 bg-amber-50 text-amber-700'
                    : 'border-gray-300 text-gray-600 hover:bg-gray-50'
                }`}
              >
                <ArrowRightToLine className="h-4 w-4" />
                Migrar (nuevo)
              </button>
              <button
                type="button"
                onClick={() => setMode('update')}
                className={`flex-1 flex items-center justify-center gap-2 rounded-md px-3 py-2 text-sm font-medium border ${
                  mode === 'update'
                    ? 'border-blue-500 bg-blue-50 text-blue-700'
                    : 'border-gray-300 text-gray-600 hover:bg-gray-50'
                }`}
              >
                <RefreshCw className="h-4 w-4" />
                Actualizar
              </button>
            </div>
            <p className="mt-1.5 text-xs text-gray-500">
              {mode === 'migrate'
                ? 'Crea un nuevo agente en el tenant con esta plantilla.'
                : 'Actualiza el agente existente creado por esta plantilla en el tenant.'}
            </p>
          </div>

          {error && (
            <div className="rounded-md bg-red-50 p-3 text-sm text-red-600">{error}</div>
          )}

          {success && (
            <div className="rounded-md bg-green-50 p-3 text-sm text-green-700">{success}</div>
          )}

          <div>
            <label className="block text-sm font-medium text-gray-700">Seleccionar Tenant</label>
            {loadingTenants ? (
              <div className="mt-2 flex items-center gap-2 text-sm text-gray-400">
                <Loader2 className="h-4 w-4 animate-spin" /> Cargando tenants...
              </div>
            ) : (
              <select
                value={selectedTenantId}
                onChange={(e) => setSelectedTenantId(e.target.value)}
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              >
                <option value="">-- Seleccionar --</option>
                {activeTenants.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name} ({t.slug})
                  </option>
                ))}
              </select>
            )}
          </div>

          <div className="flex justify-end gap-3 border-t border-gray-200 pt-4">
            <button
              type="button"
              onClick={onClose}
              className="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
            >
              Cerrar
            </button>
            <button
              type="button"
              onClick={handleAction}
              disabled={!selectedTenantId || migrateMut.isPending}
              className={`flex items-center gap-2 rounded-md px-4 py-2 text-sm font-medium disabled:opacity-50 ${
                mode === 'migrate'
                  ? 'bg-amber-500 text-gray-900 hover:bg-amber-400'
                  : 'bg-blue-600 text-white hover:bg-blue-700'
              }`}
            >
              {migrateMut.isPending ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : mode === 'migrate' ? (
                <ArrowRightToLine className="h-4 w-4" />
              ) : (
                <RefreshCw className="h-4 w-4" />
              )}
              {mode === 'migrate' ? 'Migrar' : 'Actualizar'}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
