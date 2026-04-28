import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { X, Loader2, Building2, FileText, Zap, Check, Search, CircleDot, Globe, CheckCircle2, Settings, Webhook, Tag } from 'lucide-react'
import {
  useCreateTenant,
  useUpdateTenant,
  type AdminTenant,
} from '@/modules/admin/hooks/useAdminTenants'
import { useAdminPrompts } from '@/modules/admin/hooks/useAdminPrompts'
import {
  useAdminTenantAssignments,
  useSetTenantAssignedPrompts,
  useSetTenantAssignedActions,
  type AssignmentConflict,
} from '@/modules/admin/hooks/useAdminTenantAssignments'
import { AssignmentConflictModal } from './AssignmentConflictModal'
import { AdminTenantConfigTab } from './AdminTenantConfigTab'
import { TenantActionsConfigTab } from './TenantActionsConfigTab'
import { TenantLabelingPromptsTab } from './TenantLabelingPromptsTab'

const tenantSchema = z.object({
  name: z.string().min(1, 'El nombre es requerido'),
  slug: z.string().min(1, 'El slug es requerido').regex(/^[a-z0-9-]+$/, 'Solo letras minusculas, numeros y guiones'),
  country: z.string().min(1, 'El pais es requerido'),
  monthlyBillingAmount: z.coerce.number().min(0, 'El monto debe ser mayor o igual a 0'),
})

type TenantForm = z.infer<typeof tenantSchema>

type Tab = 'general' | 'config' | 'prompts' | 'actions' | 'webhooks' | 'labeling'

interface TenantFormModalProps {
  tenant?: AdminTenant
  onClose: () => void
}

function slugify(text: string): string {
  return text
    .toLowerCase()
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

export function TenantFormModal({ tenant, onClose }: TenantFormModalProps) {
  const isEdit = !!tenant
  const createTenant = useCreateTenant()
  const updateTenant = useUpdateTenant()
  const [error, setError] = useState<string | null>(null)
  const [tab, setTab] = useState<Tab>('general')
  const [search, setSearch] = useState('')
  const [selectedPromptIds, setSelectedPromptIds] = useState<Set<string>>(new Set())
  const [selectedActionIds, setSelectedActionIds] = useState<Set<string>>(new Set())
  const [conflict, setConflict] = useState<{
    kind: 'prompts' | 'actions'
    items: AssignmentConflict[]
  } | null>(null)
  const [successMessage, setSuccessMessage] = useState<string | null>(null)

  useEffect(() => {
    if (!successMessage) return
    const id = setTimeout(() => setSuccessMessage(null), 3500)
    return () => clearTimeout(id)
  }, [successMessage])

  const { data: allPrompts, isLoading: loadingPrompts } = useAdminPrompts()
  const { data: assignments, isLoading: loadingAssignments } = useAdminTenantAssignments(
    isEdit ? tenant!.id : null,
  )
  const setAssignedPrompts = useSetTenantAssignedPrompts()
  const setAssignedActions = useSetTenantAssignedActions()

  const {
    register,
    handleSubmit,
    setValue,
    formState: { errors },
  } = useForm<TenantForm>({
    resolver: zodResolver(tenantSchema),
    defaultValues: isEdit
      ? {
          name: tenant.name,
          slug: tenant.slug,
          country: tenant.country,
          monthlyBillingAmount: tenant.monthlyBillingAmount,
        }
      : {
          name: '',
          slug: '',
          country: 'Panama',
          monthlyBillingAmount: 0,
        },
  })

  // Sincroniza el estado local con el servidor solo cuando el contenido cambia.
  // Usar la referencia del array como dependencia provoca resets en cada refetch
  // de React Query (focus, stale time), borrando selecciones no guardadas.
  const assignedPromptIdsKey = (assignments?.assignedPromptIds ?? []).slice().sort().join(',')
  const assignedActionIdsKey = (assignments?.assignedActionIds ?? []).slice().sort().join(',')

  useEffect(() => {
    if (assignments?.assignedPromptIds) {
      setSelectedPromptIds(new Set(assignments.assignedPromptIds))
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [assignedPromptIdsKey])

  useEffect(() => {
    if (assignments?.assignedActionIds) {
      setSelectedActionIds(new Set(assignments.assignedActionIds))
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [assignedActionIdsKey])

  useEffect(() => {
    setSearch('')
  }, [tab])

  const handleNameChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const name = e.target.value
    if (!isEdit) {
      setValue('slug', slugify(name))
    }
  }

  const onSubmit = async (data: TenantForm) => {
    setError(null)
    try {
      if (isEdit) {
        await updateTenant.mutateAsync({
          id: tenant.id,
          name: data.name,
          country: data.country,
          monthlyBillingAmount: data.monthlyBillingAmount,
        })
      } else {
        await createTenant.mutateAsync({
          name: data.name,
          slug: data.slug,
          country: data.country,
          monthlyBillingAmount: data.monthlyBillingAmount,
        })
      }
      onClose()
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { error?: string } } }
      setError(axiosErr.response?.data?.error ?? 'Error al guardar. Intenta nuevamente.')
    }
  }

  const filteredPrompts = useMemo(() => {
    const active = (allPrompts ?? []).filter((p) => p.isActive)
    const q = search.trim().toLowerCase()
    const matched = q
      ? active.filter(
          (p) =>
            p.name.toLowerCase().includes(q) ||
            (p.description ?? '').toLowerCase().includes(q) ||
            (p.categoryName ?? '').toLowerCase().includes(q),
        )
      : active
    // Asignados primero, luego no asignados; alfabético dentro de cada grupo.
    return [...matched].sort((a, b) => {
      const aAssigned = selectedPromptIds.has(a.id) ? 0 : 1
      const bAssigned = selectedPromptIds.has(b.id) ? 0 : 1
      if (aAssigned !== bAssigned) return aAssigned - bAssigned
      return a.name.localeCompare(b.name, 'es', { sensitivity: 'base' })
    })
  }, [allPrompts, search, selectedPromptIds])

  const filteredActions = useMemo(() => {
    const actions = assignments?.actions ?? []
    const q = search.trim().toLowerCase()
    const matched = q
      ? actions.filter(
          (a) =>
            a.name.toLowerCase().includes(q) ||
            (a.description ?? '').toLowerCase().includes(q),
        )
      : actions
    return [...matched].sort((a, b) => {
      const aAssigned = selectedActionIds.has(a.id) ? 0 : 1
      const bAssigned = selectedActionIds.has(b.id) ? 0 : 1
      if (aAssigned !== bAssigned) return aAssigned - bAssigned
      return a.name.localeCompare(b.name, 'es', { sensitivity: 'base' })
    })
  }, [assignments?.actions, search, selectedActionIds])

  const togglePrompt = (id: string) => {
    setSelectedPromptIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const toggleAction = (id: string) => {
    setSelectedActionIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const initialPromptSet = new Set(assignments?.assignedPromptIds ?? [])
  const initialActionSet = new Set(assignments?.assignedActionIds ?? [])

  const promptsChanged =
    selectedPromptIds.size !== initialPromptSet.size ||
    [...selectedPromptIds].some((id) => !initialPromptSet.has(id))

  const actionsChanged =
    selectedActionIds.size !== initialActionSet.size ||
    [...selectedActionIds].some((id) => !initialActionSet.has(id))

  const handleSavePrompts = async () => {
    if (!isEdit) return
    try {
      const count = selectedPromptIds.size
      await setAssignedPrompts.mutateAsync({
        tenantId: tenant!.id,
        promptIds: [...selectedPromptIds],
      })
      setSuccessMessage(
        count === 0
          ? `Prompts actualizados — ${tenant!.name} verá todos los prompts.`
          : `Prompts actualizados — ${count} asignado(s) a ${tenant!.name}.`,
      )
    } catch (err: unknown) {
      const axiosErr = err as { response?: { status?: number; data?: { conflicts?: AssignmentConflict[] } } }
      if (axiosErr.response?.status === 409 && axiosErr.response.data?.conflicts) {
        // Revertir la selección local al estado de BD: el cambio NO se aplicó.
        setSelectedPromptIds(new Set(assignments?.assignedPromptIds ?? []))
        setConflict({ kind: 'prompts', items: axiosErr.response.data.conflicts })
        return
      }
      throw err
    }
  }

  const handleSaveActions = async () => {
    if (!isEdit) return
    try {
      const count = selectedActionIds.size
      await setAssignedActions.mutateAsync({
        tenantId: tenant!.id,
        actionIds: [...selectedActionIds],
      })
      setSuccessMessage(
        count === 0
          ? `Acciones actualizadas — ${tenant!.name} verá todas sus acciones activas.`
          : `Acciones actualizadas — ${count} asignada(s) a ${tenant!.name}.`,
      )
    } catch (err: unknown) {
      const axiosErr = err as { response?: { status?: number; data?: { conflicts?: AssignmentConflict[] } } }
      if (axiosErr.response?.status === 409 && axiosErr.response.data?.conflicts) {
        setSelectedActionIds(new Set(assignments?.assignedActionIds ?? []))
        setConflict({ kind: 'actions', items: axiosErr.response.data.conflicts })
        return
      }
      throw err
    }
  }

  const isPending = createTenant.isPending || updateTenant.isPending

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-2">
      <div className="flex h-[96vh] w-[98vw] flex-col overflow-hidden rounded-xl bg-white shadow-2xl">
        <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
          <h2 className="text-lg font-semibold text-gray-900">
            {isEdit ? `Editar Cliente — ${tenant.name}` : 'Nuevo Cliente'}
          </h2>
          <button onClick={onClose} className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-600">
            <X className="h-5 w-5" />
          </button>
        </div>

        {isEdit && (
          <div className="flex border-b border-gray-200 px-6">
            <TabButton active={tab === 'general'} onClick={() => setTab('general')} icon={<Building2 className="h-4 w-4" />}>
              General
            </TabButton>
            <TabButton active={tab === 'config'} onClick={() => setTab('config')} icon={<Settings className="h-4 w-4" />}>
              Configuración
            </TabButton>
            <TabButton
              active={tab === 'prompts'}
              onClick={() => setTab('prompts')}
              icon={<FileText className="h-4 w-4" />}
              badge={selectedPromptIds.size}
            >
              Prompts asignados
            </TabButton>
            <TabButton
              active={tab === 'actions'}
              onClick={() => setTab('actions')}
              icon={<Zap className="h-4 w-4" />}
              badge={selectedActionIds.size}
            >
              Acciones asignadas
            </TabButton>
            <TabButton
              active={tab === 'webhooks'}
              onClick={() => setTab('webhooks')}
              icon={<Webhook className="h-4 w-4" />}
            >
              Webhooks
            </TabButton>
            <TabButton
              active={tab === 'labeling'}
              onClick={() => setTab('labeling')}
              icon={<Tag className="h-4 w-4" />}
            >
              Etiquetado
            </TabButton>
          </div>
        )}

        {successMessage && (
          <div className="flex items-start gap-2 border-b border-emerald-200 bg-emerald-50 px-6 py-2.5">
            <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" />
            <p className="flex-1 text-sm text-emerald-800">{successMessage}</p>
            <button
              onClick={() => setSuccessMessage(null)}
              className="rounded p-0.5 text-emerald-600 hover:bg-emerald-100"
              aria-label="Cerrar"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          </div>
        )}

        {tab === 'general' && (
          <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col overflow-hidden">
            <div className="space-y-4 overflow-y-auto px-6 py-4">
              {error && (
                <div className="rounded-md bg-red-50 p-3 text-sm text-red-600">{error}</div>
              )}

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700">Nombre</label>
                  <input
                    {...register('name', { onChange: handleNameChange })}
                    className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  />
                  {errors.name && (
                    <p className="mt-1 text-xs text-red-600">{errors.name.message}</p>
                  )}
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700">Slug</label>
                  <input
                    {...register('slug')}
                    disabled={isEdit}
                    className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-100 disabled:text-gray-500"
                  />
                  {errors.slug && (
                    <p className="mt-1 text-xs text-red-600">{errors.slug.message}</p>
                  )}
                </div>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700">Pais</label>
                  <input
                    {...register('country')}
                    className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  />
                  {errors.country && (
                    <p className="mt-1 text-xs text-red-600">{errors.country.message}</p>
                  )}
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700">Monto Mensual</label>
                  <input
                    type="number"
                    step="0.01"
                    {...register('monthlyBillingAmount')}
                    className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  />
                  {errors.monthlyBillingAmount && (
                    <p className="mt-1 text-xs text-red-600">
                      {errors.monthlyBillingAmount.message}
                    </p>
                  )}
                </div>
              </div>
            </div>

            <div className="flex justify-end gap-3 border-t border-gray-200 px-6 py-3">
              <button
                type="button"
                onClick={onClose}
                className="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
              >
                Cancelar
              </button>
              <button
                type="submit"
                disabled={isPending}
                className="flex items-center gap-2 rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
              >
                {isPending && <Loader2 className="h-4 w-4 animate-spin" />}
                {isEdit ? 'Guardar cambios' : 'Crear cliente'}
              </button>
            </div>
          </form>
        )}

        {isEdit && tab === 'config' && (
          <div className="flex-1 overflow-y-auto">
            <AdminTenantConfigTab tenantId={tenant!.id} />
          </div>
        )}

        {isEdit && tab === 'prompts' && (
          <div className="flex flex-1 flex-col overflow-hidden">
            <AssignSearchBar
              search={search}
              setSearch={setSearch}
              placeholder="Buscar prompts..."
              onSelectAll={() => setSelectedPromptIds(new Set(filteredPrompts.map((p) => p.id)))}
              onClear={() => setSelectedPromptIds(new Set())}
              summary={
                selectedPromptIds.size === 0
                  ? 'Sin selección — el cliente verá todos los prompts.'
                  : `${selectedPromptIds.size} prompt(s) seleccionados.`
              }
            />
            <div className="flex-1 overflow-y-auto px-6 py-4">
              {loadingPrompts || loadingAssignments ? (
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
              )}
            </div>
            <AssignFooter
              message="Los cambios se aplican al guardar. Lista vacía = el cliente ve todos los prompts."
              disabled={!promptsChanged || setAssignedPrompts.isPending}
              loading={setAssignedPrompts.isPending}
              onSave={handleSavePrompts}
              onClose={onClose}
            />
          </div>
        )}

        {isEdit && tab === 'actions' && (
          <div className="flex flex-1 flex-col overflow-hidden">
            <AssignSearchBar
              search={search}
              setSearch={setSearch}
              placeholder="Buscar acciones..."
              onSelectAll={() =>
                setSelectedActionIds(new Set(filteredActions.filter((a) => a.isActive).map((a) => a.id)))
              }
              onClear={() => setSelectedActionIds(new Set())}
              summary={
                selectedActionIds.size === 0
                  ? 'Sin selección — el cliente verá todas sus acciones activas.'
                  : `${selectedActionIds.size} acción(es) seleccionadas.`
              }
            />
            <div className="flex-1 overflow-y-auto px-6 py-4">
              {loadingAssignments ? (
                <div className="flex items-center justify-center py-12">
                  <Loader2 className="h-5 w-5 animate-spin text-gray-400" />
                </div>
              ) : filteredActions.length === 0 ? (
                <div className="py-12 text-center">
                  <p className="text-sm text-gray-400">Este cliente no tiene acciones configuradas.</p>
                  <p className="mt-1 text-xs text-gray-400">
                    Las acciones se crean desde la sección "Acciones" del panel admin.
                  </p>
                </div>
              ) : (
                <ul className="space-y-2">
                  {filteredActions.map((a) => {
                    const checked = selectedActionIds.has(a.id)
                    return (
                      <li key={a.id}>
                        <label
                          className={`flex cursor-pointer items-start gap-3 rounded-lg border p-3 transition-colors ${
                            checked
                              ? 'border-amber-500 bg-amber-50'
                              : 'border-gray-200 hover:bg-gray-50'
                          } ${!a.isActive ? 'opacity-60' : ''}`}
                        >
                          <input
                            type="checkbox"
                            checked={checked}
                            onChange={() => toggleAction(a.id)}
                            className="mt-0.5 h-4 w-4 rounded border-gray-300 text-amber-600"
                          />
                          <Zap className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />
                          <div className="min-w-0 flex-1">
                            <div className="flex items-center gap-2">
                              <p className="text-sm font-medium text-gray-900">{a.name}</p>
                              {a.isGlobal ? (
                                <span className="inline-flex items-center gap-1 rounded-full bg-indigo-100 px-2 py-0.5 text-[10px] font-medium text-indigo-700">
                                  <Globe className="h-2.5 w-2.5" />
                                  Global
                                </span>
                              ) : a.originTenantName ? (
                                <span
                                  className="inline-flex items-center rounded-full bg-gray-100 px-2 py-0.5 text-[10px] font-medium text-gray-700"
                                  title={`Acción legacy scopada a ${a.originTenantName}`}
                                >
                                  {a.originTenantName}
                                </span>
                              ) : null}
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
                          {checked && <Check className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />}
                        </label>
                      </li>
                    )
                  })}
                </ul>
              )}
            </div>
            <AssignFooter
              message="Los cambios se aplican al guardar. Lista vacía = el cliente ve todas sus acciones activas."
              disabled={!actionsChanged || setAssignedActions.isPending}
              loading={setAssignedActions.isPending}
              onSave={handleSaveActions}
              onClose={onClose}
            />
          </div>
        )}

        {isEdit && tab === 'webhooks' && tenant && (
          <div className="flex flex-1 flex-col overflow-hidden">
            <TenantActionsConfigTab tenantId={tenant.id} />
          </div>
        )}

        {isEdit && tab === 'labeling' && tenant && (
          <TenantLabelingPromptsTab tenantId={tenant.id} />
        )}
      </div>

      {conflict && (
        <AssignmentConflictModal
          kind={conflict.kind}
          conflicts={conflict.items}
          tenantName={tenant?.name ?? ''}
          onClose={() => setConflict(null)}
        />
      )}
    </div>
  )
}

function TabButton({
  active,
  onClick,
  icon,
  badge,
  children,
}: {
  active: boolean
  onClick: () => void
  icon: React.ReactNode
  badge?: number
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`flex items-center gap-2 px-4 py-3 text-sm font-medium border-b-2 ${
        active
          ? 'border-indigo-600 text-indigo-700'
          : 'border-transparent text-gray-600 hover:text-gray-900'
      }`}
    >
      {icon} {children}
      {badge !== undefined && (
        <span className="ml-1 rounded-full bg-gray-100 px-1.5 py-0.5 text-[10px] font-semibold text-gray-700">
          {badge}
        </span>
      )}
    </button>
  )
}

function AssignSearchBar({
  search,
  setSearch,
  placeholder,
  onSelectAll,
  onClear,
  summary,
}: {
  search: string
  setSearch: (v: string) => void
  placeholder: string
  onSelectAll: () => void
  onClear: () => void
  summary: string
}) {
  return (
    <div className="border-b border-gray-200 bg-gray-50 px-6 py-2">
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-gray-400" />
        <input
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={placeholder}
          className="w-full rounded-lg border border-gray-300 bg-white py-1.5 pl-9 pr-3 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
        />
      </div>
      <div className="mt-2 flex items-center gap-3 text-xs">
        <button type="button" onClick={onSelectAll} className="font-medium text-indigo-600 hover:underline">
          Seleccionar todos
        </button>
        <span className="text-gray-300">|</span>
        <button type="button" onClick={onClear} className="font-medium text-gray-600 hover:underline">
          Limpiar selección
        </button>
        <span className="ml-auto text-gray-500">{summary}</span>
      </div>
    </div>
  )
}

function AssignFooter({
  message,
  disabled,
  loading,
  onSave,
  onClose,
}: {
  message: string
  disabled: boolean
  loading: boolean
  onSave: () => void
  onClose: () => void
}) {
  return (
    <div className="flex items-center justify-between border-t border-gray-200 px-6 py-3">
      <p className="text-xs text-gray-500">{message}</p>
      <div className="flex gap-2">
        <button
          type="button"
          onClick={onClose}
          className="rounded-lg border border-gray-300 px-4 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50"
        >
          Cerrar
        </button>
        <button
          type="button"
          disabled={disabled}
          onClick={onSave}
          className="flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
        >
          {loading && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
          Guardar asignaciones
        </button>
      </div>
    </div>
  )
}
