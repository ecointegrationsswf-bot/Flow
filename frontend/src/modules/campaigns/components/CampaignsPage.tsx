import { useState, useMemo } from 'react'
import { Link } from 'react-router-dom'
import { Megaphone, Plus, Loader2, Search, X, Rocket, Eye, Pause, Play } from 'lucide-react'
import { PageHeader } from '@/shared/components/PageHeader'
import { Badge } from '@/shared/components/Badge'
import { EmptyState } from '@/shared/components/EmptyState'
import { LoadingSpinner } from '@/shared/components/LoadingSpinner'
import {
  useCampaigns, useLaunchCampaign, usePauseCampaign, useResumeCampaign,
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
}

export function CampaignsPage() {
  const { hasPermission } = usePermissions()
  const canCreate = hasPermission('create_campaigns')
  const canLaunch = hasPermission('launch_campaigns') || canCreate
  const { data: campaigns, isLoading, isError } = useCampaigns()
  const launchMutation = useLaunchCampaign()
  const pauseMutation = usePauseCampaign()
  const resumeMutation = useResumeCampaign()
  const tt = useTenantTime()
  const { toasts, remove, toast } = useToast()
  const [launchError, setLaunchError] = useState<string | null>(null)
  const [launchingId, setLaunchingId] = useState<string | null>(null)
  const [togglingId, setTogglingId] = useState<string | null>(null)

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
        title="Campanas"
        subtitle="Gestiona campanas de cobros, reclamos y renovaciones"
        action={canCreate ? (
          <Link
            to="/campaigns/new"
            className="flex items-center gap-1.5 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 transition-colors"
          >
            <Plus className="h-4 w-4" /> Nueva campana
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
          title="Sin campanas"
          description="Crea tu primera campana subiendo un archivo de contactos"
          action={canCreate ? (
            <Link
              to="/campaigns/new"
              className="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 transition-colors"
            >
              Crear campana
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

          {/* Tabla */}
          <div className="overflow-hidden rounded-lg bg-white shadow-sm">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Nombre</th>
                  <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Tipo</th>
                  <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Canal</th>
                  <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Progreso</th>
                  <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Estado</th>
                  <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Fecha</th>
                  <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Creada por</th>
                  <th className="px-2 py-3 text-right text-xs font-medium uppercase text-gray-500">Acciones</th>
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
                    const status = c.status ?? (c.completedAt ? 'Completed' : c.isActive ? 'Running' : 'Pending')
                    const stCfg = statusConfig[status] ?? statusConfig.Pending
                    const isRunning = status === 'Running' || status === 'Launching'
                    const isLaunchable = LAUNCHABLE_STATUSES.has(status)
                    const isThisLaunching = launchingId === c.id

                    return (
                      <tr key={c.id} className="hover:bg-gray-50">
                        <td className="px-4 py-3">
                          <p className="text-xs font-normal text-gray-900">{c.name}</p>
                          {c.sourceFileName && <p className="text-xs text-gray-500">{c.sourceFileName}</p>}
                        </td>
                        <td className="px-4 py-3">
                          <span className="text-xs text-gray-600">{triggerLabels[c.trigger] ?? c.trigger}</span>
                        </td>
                        <td className="px-4 py-3">
                          <Badge variant="General">{c.channel}</Badge>
                        </td>
                        <td className="px-4 py-3">
                          <div className="flex items-center gap-2">
                            <div className="h-2 w-24 overflow-hidden rounded-full bg-gray-200">
                              <div
                                className={`h-full rounded-full transition-all duration-500 ${isRunning ? 'bg-green-500' : 'bg-blue-600'}`}
                                style={{ width: `${pct}%` }}
                              />
                            </div>
                            <span className="text-xs text-gray-500">{c.processedContacts}/{c.totalContacts}</span>
                          </div>
                        </td>
                        <td className="px-4 py-3">
                          <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${stCfg.className}`}>
                            {isRunning && <Loader2 className="h-3 w-3 animate-spin" />}
                            {stCfg.label}
                          </span>
                        </td>
                        <td className="px-4 py-3 text-xs text-gray-500">
                          {tt.date(c.createdAt)}
                        </td>
                        <td className="px-4 py-3 text-xs text-gray-600">
                          {c.createdByUserId || '—'}
                        </td>
                        <td className="px-2 py-3 text-right">
                          <div className="inline-flex items-center gap-1">
                            {/* Ver contactos — siempre disponible */}
                            <Link
                              to={`/campaigns/${c.id}/contacts`}
                              title="Ver contactos"
                              className="inline-flex items-center justify-center rounded-md p-1.5 text-gray-500 hover:bg-gray-100 hover:text-blue-600 transition-colors"
                            >
                              <Eye className="h-4 w-4" />
                            </Link>

                            {/* Pausar — solo si está corriendo y activa */}
                            {canLaunch && status === 'Running' && c.isActive && (
                              <button
                                onClick={() => handlePause(c.id, c.name)}
                                disabled={togglingId === c.id}
                                title={`Pausar "${c.name}"`}
                                className="inline-flex items-center justify-center rounded-md p-1.5 text-gray-500 hover:bg-orange-50 hover:text-orange-600 disabled:opacity-40 transition-colors"
                              >
                                {togglingId === c.id
                                  ? <Loader2 className="h-4 w-4 animate-spin" />
                                  : <Pause className="h-4 w-4" />}
                              </button>
                            )}

                            {/* Reanudar — running pero pausada manualmente, o status=Paused */}
                            {canLaunch && (status === 'Paused' || (status === 'Running' && !c.isActive)) && (
                              <button
                                onClick={() => handleResume(c.id, c.name)}
                                disabled={togglingId === c.id}
                                title={`Reanudar "${c.name}"`}
                                className="inline-flex items-center justify-center rounded-md p-1.5 text-gray-500 hover:bg-emerald-50 hover:text-emerald-600 disabled:opacity-40 transition-colors"
                              >
                                {togglingId === c.id
                                  ? <Loader2 className="h-4 w-4 animate-spin" />
                                  : <Play className="h-4 w-4" />}
                              </button>
                            )}

                            {/* Lanzar — para campañas pendientes/falladas */}
                            {canLaunch && isLaunchable && (
                              <button
                                onClick={() => handleLaunch(c.id, c.name)}
                                disabled={isThisLaunching}
                                title={`Lanzar "${c.name}"`}
                                className="inline-flex items-center gap-1 rounded-md bg-blue-600 px-2 py-1 text-xs font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
                              >
                                {isThisLaunching
                                  ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
                                  : <Rocket className="h-3.5 w-3.5" />}
                                Lanzar
                              </button>
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
