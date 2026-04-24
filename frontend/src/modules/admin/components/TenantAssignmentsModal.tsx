import { useEffect, useMemo, useState } from 'react'
import { X, Loader2, FileText, Zap, Check, Search, CircleDot } from 'lucide-react'
import { useAdminPrompts } from '@/modules/admin/hooks/useAdminPrompts'
import {
  useAdminTenantAssignments,
  useSetTenantAssignedPrompts,
} from '@/modules/admin/hooks/useAdminTenantAssignments'
import type { AdminTenant } from '@/modules/admin/hooks/useAdminTenants'

interface Props {
  tenant: AdminTenant
  onClose: () => void
}

type Tab = 'prompts' | 'actions'

export function TenantAssignmentsModal({ tenant, onClose }: Props) {
  const [tab, setTab] = useState<Tab>('prompts')
  const [search, setSearch] = useState('')
  const [selectedPromptIds, setSelectedPromptIds] = useState<Set<string>>(new Set())

  const { data: allPrompts, isLoading: loadingPrompts } = useAdminPrompts()
  const { data: assignments, isLoading: loadingAssignments } = useAdminTenantAssignments(tenant.id)
  const setAssignedPrompts = useSetTenantAssignedPrompts()

  // Cuando llegan las asignaciones existentes, poblamos la selección inicial.
  useEffect(() => {
    if (assignments?.assignedPromptIds) {
      setSelectedPromptIds(new Set(assignments.assignedPromptIds))
    }
  }, [assignments?.assignedPromptIds])

  const filteredPrompts = useMemo(() => {
    const active = (allPrompts ?? []).filter((p) => p.isActive)
    const q = search.trim().toLowerCase()
    if (!q) return active
    return active.filter(
      (p) =>
        p.name.toLowerCase().includes(q) ||
        (p.description ?? '').toLowerCase().includes(q) ||
        (p.categoryName ?? '').toLowerCase().includes(q),
    )
  }, [allPrompts, search])

  const filteredActions = useMemo(() => {
    const actions = assignments?.actions ?? []
    const q = search.trim().toLowerCase()
    if (!q) return actions
    return actions.filter(
      (a) =>
        a.name.toLowerCase().includes(q) ||
        (a.description ?? '').toLowerCase().includes(q),
    )
  }, [assignments?.actions, search])

  const togglePrompt = (id: string) => {
    setSelectedPromptIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const selectAll = () => setSelectedPromptIds(new Set(filteredPrompts.map((p) => p.id)))
  const clearAll = () => setSelectedPromptIds(new Set())

  const initialSet = new Set(assignments?.assignedPromptIds ?? [])
  const hasChanges =
    tab === 'prompts' &&
    (selectedPromptIds.size !== initialSet.size ||
      [...selectedPromptIds].some((id) => !initialSet.has(id)))

  const handleSave = async () => {
    await setAssignedPrompts.mutateAsync({
      tenantId: tenant.id,
      promptIds: [...selectedPromptIds],
    })
    onClose()
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <div className="flex h-[88vh] w-full max-w-3xl flex-col overflow-hidden rounded-xl bg-white shadow-2xl">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
          <div>
            <h2 className="text-lg font-semibold text-gray-900">Asignaciones — {tenant.name}</h2>
            <p className="text-xs text-gray-500">
              Controla qué prompts globales y qué acciones están disponibles para este cliente.
            </p>
          </div>
          <button
            onClick={onClose}
            className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Tabs */}
        <div className="flex border-b border-gray-200 px-6">
          <button
            type="button"
            onClick={() => setTab('prompts')}
            className={`flex items-center gap-2 px-4 py-3 text-sm font-medium border-b-2 ${
              tab === 'prompts'
                ? 'border-blue-600 text-blue-700'
                : 'border-transparent text-gray-600 hover:text-gray-900'
            }`}
          >
            <FileText className="h-4 w-4" /> Prompts
            <span className="ml-1 rounded-full bg-gray-100 px-1.5 py-0.5 text-[10px] font-semibold text-gray-700">
              {selectedPromptIds.size}
            </span>
          </button>
          <button
            type="button"
            onClick={() => setTab('actions')}
            className={`flex items-center gap-2 px-4 py-3 text-sm font-medium border-b-2 ${
              tab === 'actions'
                ? 'border-blue-600 text-blue-700'
                : 'border-transparent text-gray-600 hover:text-gray-900'
            }`}
          >
            <Zap className="h-4 w-4" /> Acciones
            <span className="ml-1 rounded-full bg-gray-100 px-1.5 py-0.5 text-[10px] font-semibold text-gray-700">
              {(assignments?.actions ?? []).length}
            </span>
          </button>
        </div>

        {/* Search bar */}
        <div className="border-b border-gray-200 bg-gray-50 px-6 py-2">
          <div className="relative">
            <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-gray-400" />
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={tab === 'prompts' ? 'Buscar prompts...' : 'Buscar acciones...'}
              className="w-full rounded-lg border border-gray-300 bg-white py-1.5 pl-9 pr-3 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            />
          </div>
          {tab === 'prompts' && (
            <div className="mt-2 flex items-center gap-3 text-xs">
              <button
                type="button"
                onClick={selectAll}
                className="font-medium text-blue-600 hover:underline"
              >
                Seleccionar todos
              </button>
              <span className="text-gray-300">|</span>
              <button
                type="button"
                onClick={clearAll}
                className="font-medium text-gray-600 hover:underline"
              >
                Limpiar selección
              </button>
              <span className="ml-auto text-gray-500">
                {selectedPromptIds.size === 0
                  ? 'Sin selección — el cliente verá todos los prompts.'
                  : `${selectedPromptIds.size} prompt(s) seleccionados.`}
              </span>
            </div>
          )}
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto px-6 py-4">
          {tab === 'prompts' ? (
            loadingPrompts || loadingAssignments ? (
              <div className="flex items-center justify-center py-12">
                <Loader2 className="h-5 w-5 animate-spin text-gray-400" />
              </div>
            ) : filteredPrompts.length === 0 ? (
              <p className="py-12 text-center text-sm text-gray-400">
                No hay prompts que coincidan con la búsqueda.
              </p>
            ) : (
              <ul className="space-y-2">
                {filteredPrompts.map((p) => {
                  const checked = selectedPromptIds.has(p.id)
                  return (
                    <li key={p.id}>
                      <label
                        className={`flex cursor-pointer items-start gap-3 rounded-lg border p-3 transition-colors ${
                          checked
                            ? 'border-blue-500 bg-blue-50'
                            : 'border-gray-200 hover:bg-gray-50'
                        }`}
                      >
                        <input
                          type="checkbox"
                          checked={checked}
                          onChange={() => togglePrompt(p.id)}
                          className="mt-0.5 h-4 w-4 rounded border-gray-300 text-blue-600"
                        />
                        <FileText className="mt-0.5 h-4 w-4 shrink-0 text-indigo-500" />
                        <div className="min-w-0 flex-1">
                          <p className="text-sm font-medium text-gray-900">{p.name}</p>
                          {p.categoryName && (
                            <span className="mr-2 inline-block rounded-full bg-indigo-100 px-2 py-0.5 text-[10px] font-medium text-indigo-700">
                              {p.categoryName}
                            </span>
                          )}
                          {p.description && (
                            <p className="text-xs text-gray-500">{p.description}</p>
                          )}
                        </div>
                        {checked && <Check className="mt-0.5 h-4 w-4 shrink-0 text-blue-600" />}
                      </label>
                    </li>
                  )
                })}
              </ul>
            )
          ) : loadingAssignments ? (
            <div className="flex items-center justify-center py-12">
              <Loader2 className="h-5 w-5 animate-spin text-gray-400" />
            </div>
          ) : filteredActions.length === 0 ? (
            <div className="py-12 text-center">
              <p className="text-sm text-gray-400">
                Este cliente no tiene acciones configuradas.
              </p>
              <p className="mt-1 text-xs text-gray-400">
                Las acciones se crean desde la sección "Acciones" del panel admin y se
                asocian automáticamente al tenant seleccionado al crearlas.
              </p>
            </div>
          ) : (
            <ul className="space-y-2">
              {filteredActions.map((a) => (
                <li
                  key={a.id}
                  className="flex items-start gap-3 rounded-lg border border-gray-200 p-3"
                >
                  <Zap className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                      <p className="text-sm font-medium text-gray-900">{a.name}</p>
                      <span
                        className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-medium ${
                          a.isActive
                            ? 'bg-green-100 text-green-700'
                            : 'bg-gray-100 text-gray-600'
                        }`}
                      >
                        <CircleDot className="h-2.5 w-2.5" />
                        {a.isActive ? 'Activa' : 'Inactiva'}
                      </span>
                    </div>
                    {a.description && (
                      <p className="text-xs text-gray-500">{a.description}</p>
                    )}
                    <div className="mt-1 flex flex-wrap gap-1">
                      {a.requiresWebhook && (
                        <span className="rounded bg-purple-100 px-1.5 py-0.5 text-[10px] font-medium text-purple-700">
                          webhook
                        </span>
                      )}
                      {a.sendsEmail && (
                        <span className="rounded bg-blue-100 px-1.5 py-0.5 text-[10px] font-medium text-blue-700">
                          email
                        </span>
                      )}
                      {a.sendsSms && (
                        <span className="rounded bg-emerald-100 px-1.5 py-0.5 text-[10px] font-medium text-emerald-700">
                          sms
                        </span>
                      )}
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between border-t border-gray-200 px-6 py-3">
          <p className="text-xs text-gray-500">
            {tab === 'prompts'
              ? 'Los cambios se aplican al guardar.'
              : 'Vista solo-lectura. Administra acciones desde la sección "Acciones".'}
          </p>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={onClose}
              className="rounded-lg border border-gray-300 px-4 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50"
            >
              Cerrar
            </button>
            {tab === 'prompts' && (
              <button
                type="button"
                disabled={!hasChanges || setAssignedPrompts.isPending}
                onClick={handleSave}
                className="flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
              >
                {setAssignedPrompts.isPending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                Guardar asignaciones
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
