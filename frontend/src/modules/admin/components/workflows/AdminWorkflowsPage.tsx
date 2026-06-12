import { useState } from 'react'
import { Workflow, Plus, Trash2, Pencil, CircleDot, Link2, X } from 'lucide-react'
import { useAdminTenants } from '../../hooks/useAdminTenants'
import {
  useAdminFlows, useAdminFlow, useCreateFlow, useDeleteFlow,
  useAdminFlowTemplates, useBindFlow, type FlowListItem,
} from '../../hooks/useAdminFlows'
import { FlowCanvas } from './FlowCanvas'
import { promptDialog, confirmDialog, toast } from '@/shared/components/dialog'

export function AdminWorkflowsPage() {
  const { data: tenants } = useAdminTenants()
  const [tenantId, setTenantId] = useState<string>('')
  const [openFlowId, setOpenFlowId] = useState<string | null>(null)

  const flows = useAdminFlows(tenantId || undefined)
  const openFlow = useAdminFlow(openFlowId || undefined)
  const createFlow = useCreateFlow()
  const deleteFlow = useDeleteFlow()
  const templates = useAdminFlowTemplates(tenantId || undefined)
  const bindFlow = useBindFlow()

  // Editor abierto: ocupa toda el área.
  if (openFlowId && openFlow.data) {
    return <FlowCanvas flow={openFlow.data} onBack={() => setOpenFlowId(null)} />
  }
  if (openFlowId && openFlow.isLoading) {
    return <div className="p-6 text-sm text-gray-500">Cargando flujo…</div>
  }

  const handleCreate = async () => {
    if (!tenantId) {
      toast.info('Elegí un tenant primero')
      return
    }
    const name = await promptDialog({ title: 'Nuevo flujo', description: 'Nombre del flujo', defaultValue: '' })
    if (!name || !name.trim()) return
    try {
      const created = await createFlow.mutateAsync({ tenantId, name: name.trim() })
      setOpenFlowId(created.id)
    } catch {
      /* interceptor global */
    }
  }

  const handleDelete = async (f: FlowListItem) => {
    const inUse = f.boundTemplates.length > 0
    const ok = await confirmDialog({
      title: 'Eliminar flujo',
      description: inUse
        ? `"${f.name}" está EN USO por: ${f.boundTemplates.map((b) => b.name).join(', ')}. Si lo eliminás, esas conversaciones vuelven al comportamiento sin flujo. ¿Eliminar igual?`
        : `¿Eliminar "${f.name}"? Esta acción no se puede deshacer.`,
      variant: 'danger',
    })
    if (!ok) return
    try {
      // Desvincular maestros primero para no dejar punteros colgando.
      for (const b of f.boundTemplates)
        await bindFlow.mutateAsync({ campaignTemplateId: b.id, flowId: null })
      await deleteFlow.mutateAsync(f.id)
      toast.success('Flujo eliminado')
    } catch {
      /* interceptor global */
    }
  }

  const handleBind = async (f: FlowListItem, campaignTemplateId: string) => {
    const t = (templates.data ?? []).find((x) => x.id === campaignTemplateId)
    if (!t) return
    if (t.activeFlowId && t.activeFlowId !== f.id) {
      const otherFlow = (flows.data ?? []).find((x) => x.id === t.activeFlowId)
      const ok = await confirmDialog({
        title: 'Cambiar flujo del maestro',
        description: `El maestro "${t.name}" ya usa el flujo "${otherFlow?.name ?? '(otro)'}". ¿Cambiarlo a "${f.name}"?`,
      })
      if (!ok) return
    }
    try {
      await bindFlow.mutateAsync({ campaignTemplateId, flowId: f.id })
      toast.success(`Flujo vinculado a "${t.name}" — activo desde el próximo mensaje`)
    } catch {
      /* interceptor global */
    }
  }

  const handleUnbind = async (f: FlowListItem, templateId: string, templateName: string) => {
    const ok = await confirmDialog({
      title: 'Desvincular flujo',
      description: `Las conversaciones del maestro "${templateName}" volverán al comportamiento clásico (sin guion de flujo). ¿Desvincular?`,
    })
    if (!ok) return
    try {
      await bindFlow.mutateAsync({ campaignTemplateId: templateId, flowId: null })
      toast.success('Flujo desvinculado')
    } catch {
      /* interceptor global */
    }
  }

  return (
    <div>
      <div className="mb-5 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Workflow className="h-6 w-6 text-amber-500" />
          <h1 className="text-xl font-bold text-gray-800">Workflows</h1>
        </div>
        <div className="flex items-center gap-2">
          <select
            value={tenantId}
            onChange={(e) => { setTenantId(e.target.value); setOpenFlowId(null) }}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm"
          >
            <option value="">Tenant…</option>
            {(tenants ?? []).map((t) => (
              <option key={t.id} value={t.id}>{t.name}</option>
            ))}
          </select>
          <button
            onClick={handleCreate}
            disabled={!tenantId || createFlow.isPending}
            className="flex items-center gap-1.5 rounded-md bg-amber-500 px-3 py-1.5 text-sm font-semibold text-white hover:bg-amber-600 disabled:opacity-50"
          >
            <Plus className="h-4 w-4" /> Nuevo flujo
          </button>
        </div>
      </div>

      {!tenantId && (
        <div className="rounded-lg border border-dashed border-gray-300 bg-white p-10 text-center text-sm text-gray-500">
          Elegí un tenant para ver y diseñar sus flujos.
        </div>
      )}

      {tenantId && flows.isLoading && <p className="text-sm text-gray-500">Cargando flujos…</p>}

      {tenantId && flows.data && flows.data.length === 0 && (
        <div className="rounded-lg border border-dashed border-gray-300 bg-white p-10 text-center text-sm text-gray-500">
          Este tenant no tiene flujos todavía. Creá el primero con “Nuevo flujo”.
        </div>
      )}

      {tenantId && flows.data && flows.data.length > 0 && (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {flows.data.map((f) => {
            const inUse = f.boundTemplates.length > 0
            // Maestros disponibles para vincular a ESTE flujo (los que no lo usan ya).
            const bindable = (templates.data ?? []).filter(
              (t) => !f.boundTemplates.some((b) => b.id === t.id),
            )
            return (
              <div key={f.id} className="flex flex-col rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
                <div className="mb-1 flex items-start justify-between gap-2">
                  <h3 className="font-semibold text-gray-800">{f.name}</h3>
                  <div className="flex shrink-0 items-center gap-2">
                    {inUse ? (
                      <span className="flex items-center gap-1 rounded-full bg-blue-50 px-2 py-0.5 text-[11px] font-semibold text-blue-700">
                        <Link2 className="h-3 w-3" /> EN USO
                      </span>
                    ) : (
                      <span className="rounded-full bg-gray-100 px-2 py-0.5 text-[11px] font-medium text-gray-500">
                        Sin vincular
                      </span>
                    )}
                    <span className={`flex items-center gap-1 text-[11px] font-medium ${f.isActive ? 'text-emerald-600' : 'text-gray-400'}`}>
                      <CircleDot className="h-3 w-3" /> {f.isActive ? 'Activo' : 'Inactivo'}
                    </span>
                  </div>
                </div>

                {/* Vínculos: el motor SOLO ejecuta los flujos que algún maestro apunta. */}
                <div className="mb-2">
                  <div className="mb-1 text-[10px] font-bold uppercase tracking-wide text-gray-400">
                    Usado por (maestros)
                  </div>
                  {f.boundTemplates.length > 0 ? (
                    <div className="flex flex-wrap gap-1.5">
                      {f.boundTemplates.map((b) => (
                        <span key={b.id} className="flex items-center gap-1 rounded-full border border-blue-200 bg-blue-50 px-2 py-0.5 text-[11px] font-medium text-blue-800">
                          {b.name}
                          <button
                            onClick={() => handleUnbind(f, b.id, b.name)}
                            title={`Desvincular de ${b.name}`}
                            className="rounded-full p-0.5 hover:bg-blue-100"
                          >
                            <X className="h-3 w-3" />
                          </button>
                        </span>
                      ))}
                    </div>
                  ) : (
                    <p className="text-[11px] text-gray-400">
                      Ningún maestro lo usa — el bot lo ignora hasta vincularlo.
                    </p>
                  )}
                  {bindable.length > 0 ? (
                    <select
                      value=""
                      onChange={(e) => { if (e.target.value) void handleBind(f, e.target.value) }}
                      className="mt-1.5 w-full rounded-md border border-gray-300 px-2 py-1 text-xs text-gray-700"
                    >
                      <option value="">+ Vincular a maestro…</option>
                      {bindable.map((t) => (
                        <option key={t.id} value={t.id}>
                          {t.name}{t.activeFlowId ? ' (ya usa otro flujo)' : ''}
                        </option>
                      ))}
                    </select>
                  ) : (
                    // Sin maestros disponibles: mostrar el control igual (deshabilitado) para
                    // que se entienda DÓNDE se vincula — antes desaparecía y parecía que no existía.
                    <select
                      disabled
                      className="mt-1.5 w-full cursor-not-allowed rounded-md border border-gray-200 bg-gray-50 px-2 py-1 text-xs text-gray-400"
                    >
                      <option>
                        {(templates.data ?? []).length === 0
                          ? 'Este tenant no tiene maestros'
                          : 'Todos los maestros ya usan este flujo'}
                      </option>
                    </select>
                  )}
                </div>

                {f.description && <p className="mb-2 text-xs text-gray-500 line-clamp-3" title={f.description}>{f.description}</p>}
                <p className="mb-3 mt-auto text-[11px] text-gray-400">
                  Actualizado: {new Date(f.updatedAt ?? f.createdAt).toLocaleString()}
                </p>
                <div className="flex items-center gap-2">
                  <button
                    onClick={() => setOpenFlowId(f.id)}
                    className="flex items-center gap-1.5 rounded-md bg-gray-800 px-3 py-1.5 text-xs font-semibold text-white hover:bg-gray-700"
                  >
                    <Pencil className="h-3.5 w-3.5" /> Abrir
                  </button>
                  <button
                    onClick={() => handleDelete(f)}
                    className="flex items-center gap-1.5 rounded-md border border-gray-200 px-3 py-1.5 text-xs font-medium text-red-600 hover:bg-red-50"
                  >
                    <Trash2 className="h-3.5 w-3.5" /> Eliminar
                  </button>
                </div>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
