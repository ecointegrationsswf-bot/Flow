import { useState, useMemo, useEffect, useRef, useLayoutEffect } from 'react'
import { createPortal } from 'react-dom'
import { Link } from 'react-router-dom'
import { Megaphone, Plus, Loader2, Search, X, Rocket, Eye, Pause, Play, Ban, MoreVertical } from 'lucide-react'
import { PageHeader } from '@/shared/components/PageHeader'
import { Badge } from '@/shared/components/Badge'
import { EmptyState } from '@/shared/components/EmptyState'
import { LoadingSpinner } from '@/shared/components/LoadingSpinner'
import {
  useCampaigns, useLaunchCampaign, usePauseCampaign, useResumeCampaign,
  useCancelCampaign,
} from '@/shared/hooks/useCampaigns'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { useTenantTime } from '@/shared/hooks/useTenantTime'
import { confirmDialog } from '@/shared/components/dialog'
import { useToast, ToastContainer } from '@/shared/components/Toast'

// Estados desde los cuales se permite re-disparar una campaña.
// Pendiente = nunca lanzada. Pausada = pausada manualmente.
// Failed = falló el lanzamiento, el cliente puede reintentar.
const LAUNCHABLE_STATUSES = new Set(['Pending', 'Paused', 'Failed'])

const triggerLabels: Record<string, string> = {
  FileUpload: 'Archivo',
  PolicyEvent: 'Evento poliza',
  DelinquencyEvent: 'Automática',
  Manual: 'Manual',
}

const statusConfig: Record<string, { label: string; className: string }> = {
  Pending:    { label: 'Pendiente',  className: 'bg-gray-100 text-gray-600' },
  Launching:  { label: 'Lanzando',   className: 'bg-yellow-100 text-yellow-700' },
  Running:    { label: 'En curso',   className: 'bg-green-100 text-green-700' },
  Paused:     { label: 'Pausada',    className: 'bg-orange-100 text-orange-700' },
  Completed:  { label: 'Completada', className: 'bg-blue-100 text-blue-700' },
  Failed:     { label: 'Error',      className: 'bg-red-100 text-red-700' },
  Cancelled:  { label: 'Cancelada',  className: 'bg-gray-200 text-gray-600 line-through' },
  // Status final del ciclo de vida — la setea AutoCloseSweep al cumplirse
  // CampaignTemplate.AutoCloseHours desde CompletedAt. No más follow-ups.
  Closed:     { label: 'Cerrada',    className: 'bg-slate-200 text-slate-700' },
}

// Estados desde los que se puede CANCELAR (terminal — irreversible).
const CANCELLABLE_STATUSES = new Set(['Pending', 'Running', 'Paused', 'Launching'])

export function CampaignsPage() {
  const { hasPermission } = usePermissions()
  const canCreate = hasPermission('create_campaigns')
  const canLaunch = hasPermission('launch_campaigns') || canCreate
  const { data: campaigns, isLoading, isError } = useCampaigns()
  const launchMutation = useLaunchCampaign()
  const pauseMutation = usePauseCampaign()
  const resumeMutation = useResumeCampaign()
  const cancelMutation = useCancelCampaign()
  const tt = useTenantTime()
  const { toasts, remove, toast } = useToast()
  const [launchError, setLaunchError] = useState<string | null>(null)
  const [launchingId, setLaunchingId] = useState<string | null>(null)
  const [togglingId, setTogglingId] = useState<string | null>(null)
  const [cancellingId, setCancellingId] = useState<string | null>(null)
  // Id de la campaña cuyo menú "..." está abierto. null = todos cerrados.
  const [openMenuId, setOpenMenuId] = useState<string | null>(null)
  const menuRef = useRef<HTMLDivElement | null>(null)
  const menuBtnRefs = useRef<Map<string, HTMLButtonElement>>(new Map())
  // Coordenadas absolutas en viewport para el dropdown (rendea via Portal).
  // Necesario porque el wrapper de la tabla tiene overflow-x-auto que clipa
  // el menú si se rendea dentro del flujo normal del DOM.
  const [menuPos, setMenuPos] = useState<{ top: number; right: number } | null>(null)

  const placeMenu = (id: string) => {
    const btn = menuBtnRefs.current.get(id)
    if (!btn) return
    const r = btn.getBoundingClientRect()
    setMenuPos({ top: r.bottom + 4, right: window.innerWidth - r.right })
  }

  useLayoutEffect(() => {
    if (openMenuId) placeMenu(openMenuId)
    else setMenuPos(null)
  }, [openMenuId])

  // Si la ventana cambia tamaño / se hace scroll mientras el menú está abierto,
  // recalculamos posición para que siga al botón.
  useEffect(() => {
    if (!openMenuId) return
    const reflow = () => placeMenu(openMenuId)
    window.addEventListener('resize', reflow)
    window.addEventListener('scroll', reflow, true)
    return () => {
      window.removeEventListener('resize', reflow)
      window.removeEventListener('scroll', reflow, true)
    }
  }, [openMenuId])

  // Cierra el menú al hacer click fuera o ESC
  useEffect(() => {
    if (!openMenuId) return
    const onDown = (e: MouseEvent) => {
      const target = e.target as Node
      const btn = menuBtnRefs.current.get(openMenuId)
      // Click sobre el botón disparador → no cerrar (su onClick lo maneja)
      if (btn && btn.contains(target)) return
      if (menuRef.current && !menuRef.current.contains(target)) setOpenMenuId(null)
    }
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpenMenuId(null) }
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onEsc)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onEsc)
    }
  }, [openMenuId])

  const handlePause = async (campaignId: string, name: string) => {
    const ok = await confirmDialog({
      title: 'Pausar campaña',
      description: `¿Pausar el envío de "${name}"? Los mensajes pendientes se detienen y la puedes reanudar luego.`,
      confirmLabel: 'Pausar',
    })
    if (!ok) return
    setTogglingId(campaignId)
    try {
      await pauseMutation.mutateAsync(campaignId)
      toast.success('Campaña pausada.')
    } catch (err: unknown) {
      const e = err as { response?: { data?: { error?: string } }; message?: string }
      toast.error(e.response?.data?.error ?? e.message ?? 'No se pudo pausar.')
    } finally {
      setTogglingId(null)
    }
  }

  const handleResume = async (campaignId: string, name: string) => {
    const ok = await confirmDialog({
      title: 'Reanudar campaña',
      description: `¿Reanudar el envío de "${name}"? El Worker volverá a procesar los contactos pendientes.`,
      confirmLabel: 'Reanudar',
    })
    if (!ok) return
    setTogglingId(campaignId)
    try {
      await resumeMutation.mutateAsync(campaignId)
      toast.success('Campaña reanudada.')
    } catch (err: unknown) {
      const e = err as { response?: { data?: { error?: string } }; message?: string }
      toast.error(e.response?.data?.error ?? e.message ?? 'No se pudo reanudar.')
    } finally {
      setTogglingId(null)
    }
  }

  const handleCancel = async (campaignId: string, name: string) => {
    const ok = await confirmDialog({
      title: '¿Cancelar campaña?',
      description:
        `Vas a CANCELAR la campaña "${name}". Esta acción es IRREVERSIBLE:\n\n` +
        `• Los contactos pendientes pasarán a "Descartado".\n` +
        `• Los mensajes ya enviados NO se eliminan.\n` +
        `• Las conversaciones abiertas con clientes siguen activas — el agente los seguirá atendiendo.\n` +
        `• La campaña no se podrá reanudar después.`,
      confirmLabel: 'Sí, cancelar definitivamente',
      variant: 'danger',
    })
    if (!ok) return
    setCancellingId(campaignId)
    try {
      const r = await cancelMutation.mutateAsync(campaignId)
      toast.success(r.message ?? 'Campaña cancelada.')
    } catch (err: unknown) {
      const e = err as { response?: { data?: { error?: string } }; message?: string }
      toast.error(e.response?.data?.error ?? e.message ?? 'No se pudo cancelar.')
    } finally {
      setCancellingId(null)
    }
  }

  const handleLaunch = async (campaignId: string, name: string) => {
    const ok = await confirmDialog({
      title: 'Lanzar campaña',
      description: `¿Lanzar la campaña "${name}"? Se enviarán los mensajes a los contactos.`,
      confirmLabel: 'Lanzar',
    })
    if (!ok) return
    setLaunchError(null)
    setLaunchingId(campaignId)
    try {
      await launchMutation.mutateAsync(campaignId)
    } catch (err: unknown) {
      const e = err as { response?: { data?: { error?: string; message?: string } }; message?: string }
      setLaunchError(
        e.response?.data?.error ?? e.response?.data?.message ?? e.message ?? 'No se pudo lanzar la campaña.',
      )
    } finally {
      setLaunchingId(null)
    }
  }

  // Filtros
  const [search, setSearch] = useState('')
  const [filterStatus, setFilterStatus] = useState('')
  const [filterChannel, setFilterChannel] = useState('')

  const filtered = useMemo(() => {
    if (!campaigns) return []
    const q = search.toLowerCase().trim()
    return campaigns.filter((c) => {
      const status = c.status ?? (c.completedAt ? 'Completed' : c.isActive ? 'Running' : 'Pending')
      const statusLabel = statusConfig[status]?.label ?? status

      if (filterStatus && status !== filterStatus) return false
      if (filterChannel && c.channel !== filterChannel) return false

      if (!q) return true
      return (
        c.name.toLowerCase().includes(q) ||
        (c.sourceFileName ?? '').toLowerCase().includes(q) ||
        (c.createdByUserId ?? '').toLowerCase().includes(q) ||
        (triggerLabels[c.trigger] ?? c.trigger).toLowerCase().includes(q) ||
        c.channel.toLowerCase().includes(q) ||
        statusLabel.toLowerCase().includes(q) ||
        tt.date(c.createdAt).includes(q)
      )
    })
  }, [campaigns, search, filterStatus, filterChannel, tt.timeZone])

  const hasFilters = search || filterStatus || filterChannel

  const clearFilters = () => {
    setSearch('')
    setFilterStatus('')
    setFilterChannel('')
  }

  if (isLoading) return <LoadingSpinner />

  return (
    <div>
      <PageHeader
        title="Campañas"
        subtitle="Gestiona campañas de cobros, reclamos y renovaciones"
        action={canCreate ? (
          <Link
            to="/campaigns/new"
            className="flex items-center gap-1.5 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 transition-colors"
          >
            <Plus className="h-4 w-4" /> Nueva campaña
          </Link>
        ) : undefined}
      />

      {launchError && (
        <div className="mb-4 flex items-center justify-between rounded-lg bg-red-50 px-4 py-3 text-sm text-red-700">
          <span>{launchError}</span>
          <button onClick={() => setLaunchError(null)} className="ml-3 text-red-400 hover:text-red-600">✕</button>
        </div>
      )}

      {isError || !campaigns || campaigns.length === 0 ? (
        <EmptyState
          icon={Megaphone}
          title="Sin campañas"
          description="Crea tu primera campaña subiendo un archivo de contactos"
          action={canCreate ? (
            <Link
              to="/campaigns/new"
              className="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 transition-colors"
            >
              Crear campaña
            </Link>
          ) : undefined}
        />
      ) : (
        <div className="space-y-3">
          {/* Barra de filtros */}
          <div className="flex flex-wrap items-center gap-2">
            {/* Búsqueda por texto */}
            <div className="relative flex-1 min-w-48">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400 pointer-events-none" />
              <input
                type="text"
                placeholder="Buscar por nombre, archivo, usuario..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="w-full rounded-lg border border-gray-300 py-2 pl-9 pr-3 text-sm text-gray-900 placeholder-gray-400 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
              />
            </div>

            {/* Filtro Estado */}
            <select
              value={filterStatus}
              onChange={(e) => setFilterStatus(e.target.value)}
              className="rounded-lg border border-gray-300 py-2 px-3 text-sm text-gray-700 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            >
              <option value="">Todos los estados</option>
              {Object.entries(statusConfig).map(([key, { label }]) => (
                <option key={key} value={key}>{label}</option>
              ))}
            </select>

            {/* Filtro Canal */}
            <select
              value={filterChannel}
              onChange={(e) => setFilterChannel(e.target.value)}
              className="rounded-lg border border-gray-300 py-2 px-3 text-sm text-gray-700 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            >
              <option value="">Todos los canales</option>
              <option value="WhatsApp">WhatsApp</option>
              <option value="Email">Email</option>
              <option value="Sms">SMS</option>
            </select>

            {/* Limpiar filtros */}
            {hasFilters && (
              <button
                onClick={clearFilters}
                className="flex items-center gap-1 rounded-lg border border-gray-300 px-3 py-2 text-sm text-gray-600 hover:bg-gray-50 transition-colors"
              >
                <X className="h-4 w-4" /> Limpiar
              </button>
            )}

            <span className="ml-auto text-xs text-gray-400">
              {filtered.length} de {campaigns.length} campaña{campaigns.length !== 1 ? 's' : ''}
            </span>
          </div>

          {/* Tabla compacta: padding reducido, anchos justos. Scroll horizontal
              solo si el viewport es muy chico — en monitor estándar entra todo. */}
          <div className="overflow-x-auto rounded-lg bg-white shadow-sm">
            <table className="w-full divide-y divide-gray-200 text-xs">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-2 py-2 text-left text-[10px] font-medium uppercase text-gray-500">Nombre</th>
                  <th className="whitespace-nowrap px-2 py-2 text-left text-[10px] font-medium uppercase text-gray-500">Tipo</th>
                  <th className="whitespace-nowrap px-2 py-2 text-left text-[10px] font-medium uppercase text-gray-500">Canal</th>
                  <th className="whitespace-nowrap px-2 py-2 text-left text-[10px] font-medium uppercase text-gray-500">Progreso</th>
                  <th className="whitespace-nowrap px-2 py-2 text-left text-[10px] font-medium uppercase text-gray-500">Estado</th>
                  <th className="whitespace-nowrap px-2 py-2 text-left text-[10px] font-medium uppercase text-gray-500">Fecha</th>
                  <th className="whitespace-nowrap px-2 py-2 text-left text-[10px] font-medium uppercase text-gray-500">Creada por</th>
                  <th className="whitespace-nowrap px-2 py-2 text-right text-[10px] font-medium uppercase text-gray-500">Acciones</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200">
                {filtered.length === 0 ? (
                  <tr>
                    <td colSpan={8} className="px-4 py-10 text-center text-sm text-gray-400">
                      No se encontraron campañas con los filtros aplicados.
                    </td>
                  </tr>
                ) : (
                  filtered.map((c) => {
                    const pct = c.totalContacts > 0 ? Math.round((c.processedContacts / c.totalContacts) * 100) : 0
                    let status = c.status ?? (c.completedAt ? 'Completed' : c.isActive ? 'Running' : 'Pending')
                    // Una campaña "Running" + IsActive=false equivale visualmente a "Pausada":
                    // el endpoint /pause solo cambia IsActive y deja Status. Reflejarlo en el
                    // badge para que sea coherente con el botón ▶ Reanudar que aparece.
                    if (status === 'Running' && !c.isActive) status = 'Paused'
                    const stCfg = statusConfig[status] ?? statusConfig.Pending
                    const isRunning = status === 'Running' || status === 'Launching'
                    const isLaunchable = LAUNCHABLE_STATUSES.has(status)
                    const isThisLaunching = launchingId === c.id

                    return (
                      <tr key={c.id} className="hover:bg-gray-50">
                        {/* Nombre: ancho máximo + truncate. El title nativo del browser
                            muestra el tooltip con el texto completo al pasar el mouse. */}
                        <td className="max-w-[180px] px-2 py-1.5">
                          <p className="truncate text-xs font-normal text-gray-900" title={c.name}>
                            {c.name}
                          </p>
                          {c.sourceFileName && (
                            <p className="truncate text-[10px] text-gray-500" title={c.sourceFileName}>
                              {c.sourceFileName}
                            </p>
                          )}
                        </td>
                        <td className="whitespace-nowrap px-2 py-1.5">
                          <span className="text-xs text-gray-600">{triggerLabels[c.trigger] ?? c.trigger}</span>
                        </td>
                        <td className="whitespace-nowrap px-2 py-1.5">
                          <Badge variant="General">{c.channel}</Badge>
                        </td>
                        <td className="whitespace-nowrap px-2 py-1.5">
                          <div className="flex items-center gap-1.5">
                            <div className="h-1.5 w-16 overflow-hidden rounded-full bg-gray-200">
                              <div
                                className={`h-full rounded-full transition-all duration-500 ${isRunning ? 'bg-green-500' : 'bg-blue-600'}`}
                                style={{ width: `${pct}%` }}
                              />
                            </div>
                            <span className="text-[10px] text-gray-500">{c.processedContacts}/{c.totalContacts}</span>
                          </div>
                        </td>
                        <td className="whitespace-nowrap px-2 py-1.5">
                          <span className={`inline-flex items-center gap-1 rounded-full px-1.5 py-0.5 text-[10px] font-medium ${stCfg.className}`}>
                            {isRunning && <Loader2 className="h-2.5 w-2.5 animate-spin" />}
                            {stCfg.label}
                          </span>
                        </td>
                        <td className="whitespace-nowrap px-2 py-1.5 text-[10px] text-gray-500">
                          {tt.date(c.createdAt)}
                        </td>
                        <td className="max-w-[100px] truncate whitespace-nowrap px-2 py-1.5 text-[10px] text-gray-600" title={c.createdByUserId ?? undefined}>
                          {c.createdByUserId || '—'}
                        </td>
                        <td className="whitespace-nowrap px-2 py-1.5 text-right">
                          <div className="relative inline-block">
                            <button
                              ref={(el) => {
                                if (el) menuBtnRefs.current.set(c.id, el)
                                else menuBtnRefs.current.delete(c.id)
                              }}
                              onClick={(e) => {
                                e.stopPropagation()
                                setOpenMenuId(openMenuId === c.id ? null : c.id)
                              }}
                              title="Acciones"
                              className="inline-flex items-center justify-center rounded-md p-1 text-gray-500 hover:bg-gray-100 hover:text-gray-700 transition-colors"
                            >
                              <MoreVertical className="h-4 w-4" />
                            </button>

                            {openMenuId === c.id && menuPos && createPortal(
                              <div
                                ref={menuRef}
                                style={{ position: 'fixed', top: menuPos.top, right: menuPos.right, zIndex: 60 }}
                                className="w-44 overflow-hidden rounded-md border border-gray-200 bg-white shadow-lg ring-1 ring-black/5"
                              >
                                {/* Ver contactos — siempre disponible */}
                                <Link
                                  to={`/campaigns/${c.id}/contacts`}
                                  onClick={() => setOpenMenuId(null)}
                                  className="flex items-center gap-2 px-3 py-2 text-xs text-gray-700 hover:bg-gray-50"
                                >
                                  <Eye className="h-3.5 w-3.5 text-gray-500" />
                                  Ver contactos
                                </Link>

                                {/* Pausar */}
                                {canLaunch && status === 'Running' && c.isActive && (
                                  <button
                                    onClick={() => { setOpenMenuId(null); handlePause(c.id, c.name) }}
                                    disabled={togglingId === c.id}
                                    className="flex w-full items-center gap-2 px-3 py-2 text-xs text-gray-700 hover:bg-orange-50 hover:text-orange-700 disabled:opacity-40"
                                  >
                                    {togglingId === c.id
                                      ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
                                      : <Pause className="h-3.5 w-3.5 text-orange-500" />}
                                    Pausar
                                  </button>
                                )}

                                {/* Reanudar */}
                                {canLaunch && (status === 'Paused' || (status === 'Running' && !c.isActive)) && (
                                  <button
                                    onClick={() => { setOpenMenuId(null); handleResume(c.id, c.name) }}
                                    disabled={togglingId === c.id}
                                    className="flex w-full items-center gap-2 px-3 py-2 text-xs text-gray-700 hover:bg-emerald-50 hover:text-emerald-700 disabled:opacity-40"
                                  >
                                    {togglingId === c.id
                                      ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
                                      : <Play className="h-3.5 w-3.5 text-emerald-500" />}
                                    Reanudar
                                  </button>
                                )}

                                {/* Lanzar */}
                                {canLaunch && isLaunchable && (
                                  <button
                                    onClick={() => { setOpenMenuId(null); handleLaunch(c.id, c.name) }}
                                    disabled={isThisLaunching}
                                    className="flex w-full items-center gap-2 px-3 py-2 text-xs text-gray-700 hover:bg-blue-50 hover:text-blue-700 disabled:opacity-40"
                                  >
                                    {isThisLaunching
                                      ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
                                      : <Rocket className="h-3.5 w-3.5 text-blue-600" />}
                                    Lanzar
                                  </button>
                                )}

                                {/* Cancelar — siempre al final por destructivo */}
                                {canLaunch && CANCELLABLE_STATUSES.has(status) && (
                                  <>
                                    <div className="border-t border-gray-100" />
                                    <button
                                      onClick={() => { setOpenMenuId(null); handleCancel(c.id, c.name) }}
                                      disabled={cancellingId === c.id}
                                      className="flex w-full items-center gap-2 px-3 py-2 text-xs text-red-600 hover:bg-red-50 disabled:opacity-40"
                                    >
                                      {cancellingId === c.id
                                        ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
                                        : <Ban className="h-3.5 w-3.5" />}
                                      Cancelar
                                    </button>
                                  </>
                                )}
                              </div>,
                              document.body
                            )}
                          </div>
                        </td>
                      </tr>
                    )
                  })
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}
      <ToastContainer toasts={toasts} onRemove={remove} />
    </div>
  )
}
