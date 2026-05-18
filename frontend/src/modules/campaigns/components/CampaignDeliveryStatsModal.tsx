import { useEffect, useState } from 'react'
import { X, Loader2, BarChart3, CheckCheck, Eye, AlertTriangle, Clock, MailX, Send, RefreshCw, AlertCircle } from 'lucide-react'
import {
  useSyncCampaignDelivery,
  type CampaignDeliverySyncResult,
} from '@/shared/hooks/useCampaigns'

interface Props {
  campaignId: string
  campaignName: string
  onClose: () => void
}

/**
 * Modal de estadísticas de delivery por campaña.
 *
 * Al abrirse, dispara automáticamente POST /api/campaigns/{id}/sync-delivery-status
 * que cruza los CampaignContacts con la API de UltraMsg en tiempo real. El
 * endpoint devuelve un summary agregado + el detalle de los contactos que
 * cambiaron de estado en este sync.
 *
 * Lo importante visualmente:
 *   • Tarjetas grandes con totales por estado (read / delivered / queue / invalid / ...)
 *   • Tasa real de entrega = (read + delivered) / total
 *   • Lista de los que NO se entregaron (queue/invalid) — los que el equipo
 *     debe recontactar por otra vía (llamada, email, SMS)
 */
export function CampaignDeliveryStatsModal({ campaignId, campaignName, onClose }: Props) {
  const sync = useSyncCampaignDelivery()
  const [result, setResult] = useState<CampaignDeliverySyncResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  // Al abrir el modal, sincronizar automáticamente.
  useEffect(() => {
    sync.mutateAsync(campaignId)
      .then((r) => { setResult(r); setError(null) })
      .catch((e: unknown) => {
        const ax = e as { response?: { data?: { error?: string } }; message?: string }
        setError(ax.response?.data?.error ?? ax.message ?? 'Error al sincronizar con UltraMsg.')
      })
  // sync.mutateAsync es estable, no incluirlo en deps evita re-runs.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [campaignId])

  const summary = result?.summary

  // Tasa real de entrega: contactos con ✓✓ (delivered + read) sobre el total.
  const deliveredOk = (summary?.delivered ?? 0) + (summary?.read ?? 0)
  const notDelivered = (summary?.queue ?? 0) + (summary?.invalid ?? 0)
                     + (summary?.failed ?? 0) + (summary?.expired ?? 0)
                     + (summary?.unsent ?? 0)
  const total = summary?.total ?? 0
  const deliveryRate = total > 0 ? Math.round((deliveredOk / total) * 100) : 0
  const lossRate     = total > 0 ? Math.round((notDelivered / total) * 100) : 0

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4" onClick={onClose}>
      <div
        className="w-full max-w-3xl rounded-lg border border-gray-200 bg-white shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-start justify-between border-b border-gray-200 px-5 py-4">
          <div>
            <h2 className="flex items-center gap-2 text-base font-semibold text-gray-900">
              <BarChart3 className="h-5 w-5 text-blue-600" />
              Estadísticas de entrega
            </h2>
            <p className="mt-0.5 line-clamp-1 text-xs text-gray-500">{campaignName}</p>
          </div>
          <button onClick={onClose} className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-700">
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Body */}
        <div className="max-h-[70vh] overflow-y-auto px-5 py-4">
          {sync.isPending && !result && (
            <div className="flex flex-col items-center justify-center py-12">
              <Loader2 className="h-8 w-8 animate-spin text-blue-500" />
              <p className="mt-3 text-sm text-gray-500">Consultando UltraMsg…</p>
              <p className="text-xs text-gray-400">
                Cruzando {total > 0 ? `${total} contactos` : 'contactos'} con el estado real de WhatsApp.
              </p>
            </div>
          )}

          {error && (
            <div className="rounded-md border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
              <div className="flex items-start gap-2">
                <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
                <div>
                  <p className="font-medium">No se pudo sincronizar</p>
                  <p className="mt-1 text-xs">{error}</p>
                  <p className="mt-1 text-xs text-red-700/70">
                    Verifica que el tenant tenga una línea WhatsApp UltraMsg activa con InstanceId y Token configurados.
                  </p>
                </div>
              </div>
            </div>
          )}

          {summary && !error && (
            <>
              {/* Tasas grandes */}
              <div className="grid grid-cols-2 gap-3">
                <div className="rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3">
                  <div className="flex items-center gap-2 text-xs font-medium uppercase tracking-wide text-emerald-700">
                    <CheckCheck className="h-3.5 w-3.5" /> Tasa de entrega
                  </div>
                  <div className="mt-1 text-3xl font-bold text-emerald-700">{deliveryRate}%</div>
                  <p className="text-xs text-emerald-800/70">
                    {deliveredOk} entregados de {total} contactos
                  </p>
                </div>
                <div className={`rounded-lg border px-4 py-3 ${lossRate >= 10 ? 'border-red-200 bg-red-50' : 'border-amber-200 bg-amber-50'}`}>
                  <div className={`flex items-center gap-2 text-xs font-medium uppercase tracking-wide ${lossRate >= 10 ? 'text-red-700' : 'text-amber-700'}`}>
                    <AlertTriangle className="h-3.5 w-3.5" /> Tasa de pérdida
                  </div>
                  <div className={`mt-1 text-3xl font-bold ${lossRate >= 10 ? 'text-red-700' : 'text-amber-700'}`}>
                    {lossRate}%
                  </div>
                  <p className={`text-xs ${lossRate >= 10 ? 'text-red-800/70' : 'text-amber-800/70'}`}>
                    {notDelivered} mensajes no entregados
                  </p>
                </div>
              </div>

              {/* Desglose detallado por status */}
              <h3 className="mt-5 text-xs font-semibold uppercase tracking-wide text-gray-500">Desglose por estado</h3>
              <div className="mt-2 grid grid-cols-2 gap-2 sm:grid-cols-3">
                <StatCard icon={<Eye className="h-3.5 w-3.5 text-sky-500" />}      label="Leídos"           value={summary.read}           tone="sky"     hint="Cliente abrió el chat" />
                <StatCard icon={<CheckCheck className="h-3.5 w-3.5 text-gray-500" />} label="Entregados"      value={summary.delivered}      tone="gray"    hint="Llegó al teléfono" />
                <StatCard icon={<Send className="h-3.5 w-3.5 text-blue-400" />}    label="Sin tracking"     value={summary.sentNoTracking} tone="blue"    hint="Enviado antes del webhook on_ack" />
                <StatCard icon={<Clock className="h-3.5 w-3.5 text-amber-500" />}  label="En cola"          value={summary.queue}          tone="amber"   hint="UltraMsg no lo despachó" />
                <StatCard icon={<MailX className="h-3.5 w-3.5 text-red-500" />}    label="Inválidos"        value={summary.invalid}        tone="red"     hint="Cuenta restringida o nro inexistente" />
                <StatCard icon={<MailX className="h-3.5 w-3.5 text-red-500" />}    label="Fallidos"         value={summary.failed}         tone="red"     hint="Falló al despacharse" />
                {summary.expired > 0 && (
                  <StatCard icon={<MailX className="h-3.5 w-3.5 text-red-500" />}  label="Expirados"        value={summary.expired}        tone="red"     hint="Expiró antes de enviarse" />
                )}
                {summary.unsent > 0 && (
                  <StatCard icon={<MailX className="h-3.5 w-3.5 text-red-500" />}  label="No enviados"      value={summary.unsent}         tone="red"     hint="Cuenta desconectada" />
                )}
                {summary.pending > 0 && (
                  <StatCard icon={<Clock className="h-3.5 w-3.5 text-blue-400" />} label="Pendientes"       value={summary.pending}        tone="blue"    hint="Aún en cola del dispatcher" />
                )}
                {summary.error > 0 && (
                  <StatCard icon={<AlertCircle className="h-3.5 w-3.5 text-red-500" />} label="Marcados error" value={summary.error}      tone="red"     hint="Listos para reintento" />
                )}
              </div>

              {/* Detalle de cambios del último sync */}
              {result && result.updatedCount > 0 && (
                <>
                  <h3 className="mt-5 flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-gray-500">
                    <RefreshCw className="h-3.5 w-3.5" />
                    Actualizados en este sync ({result.updatedCount})
                  </h3>
                  <div className="mt-2 overflow-hidden rounded-lg border border-gray-200">
                    <table className="w-full text-xs">
                      <thead className="bg-gray-50 text-left text-[10px] uppercase tracking-wide text-gray-500">
                        <tr>
                          <th className="px-3 py-2">Cliente</th>
                          <th className="px-3 py-2">Teléfono</th>
                          <th className="px-3 py-2">Antes</th>
                          <th className="px-3 py-2">Ahora</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-100">
                        {result.details.map((d) => (
                          <tr key={d.contactId} className="bg-white">
                            <td className="px-3 py-2 font-medium text-gray-900">{d.clientName ?? '—'}</td>
                            <td className="px-3 py-2 font-mono text-gray-600">{d.phoneNumber}</td>
                            <td className="px-3 py-2 text-gray-500">{d.previous ?? '(sin info)'}</td>
                            <td className="px-3 py-2">
                              <span className="rounded-full bg-red-100 px-2 py-0.5 text-[10px] font-semibold text-red-700">
                                {d.newStatus}
                              </span>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                    {result.updatedCount > result.details.length && (
                      <div className="border-t border-gray-200 bg-gray-50 px-3 py-2 text-[11px] text-gray-500">
                        Mostrando {result.details.length} de {result.updatedCount} cambios. Los restantes están en BD.
                      </div>
                    )}
                  </div>
                </>
              )}

              {result && result.updatedCount === 0 && (
                <div className="mt-4 rounded-md border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs text-emerald-700">
                  Todo sincronizado — no se detectaron mensajes pendientes ni inválidos para reclasificar.
                </div>
              )}
            </>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between border-t border-gray-200 bg-gray-50 px-5 py-3">
          <div className="text-[11px] text-gray-500">
            {result && (
              <>Última sincronización: {new Date(result.syncedAt).toLocaleString('es-PA', { timeZone: 'America/Panama' })}</>
            )}
          </div>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => {
                setResult(null); setError(null)
                sync.mutateAsync(campaignId)
                  .then((r) => setResult(r))
                  .catch((e: unknown) => {
                    const ax = e as { response?: { data?: { error?: string } } }
                    setError(ax.response?.data?.error ?? 'Error al re-sincronizar.')
                  })
              }}
              disabled={sync.isPending}
              className="flex items-center gap-1.5 rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
            >
              {sync.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <RefreshCw className="h-3.5 w-3.5" />}
              Re-sincronizar
            </button>
            <button
              type="button"
              onClick={onClose}
              className="rounded-md bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700"
            >
              Cerrar
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}

function StatCard({
  icon, label, value, tone, hint,
}: {
  icon: React.ReactNode
  label: string
  value: number
  tone: 'sky' | 'gray' | 'amber' | 'red' | 'blue' | 'emerald'
  hint?: string
}) {
  const palette: Record<string, string> = {
    sky:     'border-sky-200 bg-sky-50',
    gray:    'border-gray-200 bg-gray-50',
    amber:   'border-amber-200 bg-amber-50',
    red:     'border-red-200 bg-red-50',
    blue:    'border-blue-200 bg-blue-50',
    emerald: 'border-emerald-200 bg-emerald-50',
  }
  return (
    <div className={`rounded-lg border px-3 py-2 ${palette[tone]}`} title={hint}>
      <div className="flex items-center gap-1.5 text-[11px] font-medium text-gray-600">
        {icon}
        <span>{label}</span>
      </div>
      <div className="mt-0.5 text-xl font-bold text-gray-900">{value}</div>
    </div>
  )
}
