import { useEffect, useState } from 'react'
import { Loader2, Save, Layers, Timer, ShieldAlert, CheckCircle, AlertCircle } from 'lucide-react'
import {
  useAdminTenantConfig,
  useAdminUpdateTenantCampaignBatching,
} from '@/modules/admin/hooks/useAdminTenantConfig'

interface Props {
  tenantId: string
}

/**
 * Tab "Campañas" del modal Editar Cliente — configuración anti-restricción
 * de WhatsApp a nivel tenant. Estos 3 valores controlan cómo el dispatcher
 * fracciona el envío de una campaña grande:
 *
 *   • Batch size: cuántos contactos manda por "tanda".
 *   • Cool-down: pausa entre tandas (durante esa pausa la campaña no se toca).
 *   • Auto-pause %: si una tanda tiene más fallos que este umbral,
 *     se pausa la campaña automáticamente y se notifica al admin del tenant.
 *
 * Lección Somos Seguros 18/05: una campaña de 95 cold contacts en bloque
 * gatilló restricción de la cuenta UltraMsg. Estos defaults (15 / 20 / 30)
 * la habrían frenado al 4to batch en lugar de quemar 73 envíos al vacío.
 */
export function AdminTenantCampaignsTab({ tenantId }: Props) {
  const { data: tenant, isLoading, error } = useAdminTenantConfig(tenantId)
  const update = useAdminUpdateTenantCampaignBatching()

  // Defaults sensatos para el form si el tenant aún no los tiene.
  const [batchSize, setBatchSize] = useState(15)
  const [coolDown, setCoolDown] = useState(20)
  const [autoPauseRate, setAutoPauseRate] = useState(30)
  const [saved, setSaved] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    if (!tenant) return
    setBatchSize(tenant.campaignBatchSize ?? 15)
    setCoolDown(tenant.campaignBatchCoolDownMinutes ?? 20)
    setAutoPauseRate(tenant.campaignAutoPauseFailureRate ?? 30)
  }, [tenant])

  const handleSave = async () => {
    setErr(null)
    try {
      await update.mutateAsync({
        tenantId,
        batchSize,
        coolDownMinutes: coolDown,
        autoPauseFailureRate: autoPauseRate,
      })
      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    } catch (e: unknown) {
      const ax = e as { response?: { data?: { error?: string } } }
      setErr(ax.response?.data?.error ?? 'Error al guardar.')
    }
  }

  // Cálculo informativo: con N contactos y los valores actuales, ¿en
  // cuánto termina la campaña? Ayuda al admin a calibrar los números.
  const calcHours = (contactCount: number) => {
    const batches = Math.ceil(contactCount / Math.max(1, batchSize))
    // Cada batch: ~5 min de envío (15 msg × ~20s) + coolDown min
    // Aproximación: contactCount × 20s + (batches - 1) × coolDown
    const totalMin = (contactCount * 20) / 60 + (batches - 1) * coolDown
    const hours = totalMin / 60
    return { batches, hours: hours.toFixed(1) }
  }

  if (isLoading) {
    return <div className="flex items-center justify-center py-12"><Loader2 className="h-6 w-6 animate-spin text-gray-400" /></div>
  }
  if (error || !tenant) {
    return <div className="m-4 rounded-lg bg-red-50 p-4 text-sm text-red-600">Error al cargar la configuración.</div>
  }

  const inputClass = 'mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500'
  const sample95  = calcHours(95)
  const sample500 = calcHours(500)

  return (
    <div className="space-y-6 overflow-y-auto px-6 py-4">
      <div className="rounded-md border border-blue-200 bg-blue-50 px-4 py-3">
        <div className="flex items-start gap-2">
          <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0 text-blue-600" />
          <div className="text-xs text-blue-900">
            <p className="font-semibold">Protección anti-restricción de WhatsApp</p>
            <p className="mt-1 text-blue-800/80">
              Estos valores controlan cómo el sistema fracciona campañas grandes para evitar que WhatsApp
              detecte ráfagas de bot. Aplican a TODAS las campañas de este cliente. Los defaults son
              conservadores y vienen del aprendizaje del incidente Somos Seguros 18/05.
            </p>
          </div>
        </div>
      </div>

      {/* Batch size */}
      <div className="rounded-lg border border-gray-200 bg-white p-5">
        <div className="flex items-start gap-3">
          <Layers className="mt-0.5 h-5 w-5 shrink-0 text-purple-500" />
          <div className="flex-1">
            <label className="block text-sm font-semibold text-gray-900">Tamaño de batch (contactos por tanda)</label>
            <p className="mt-0.5 text-xs text-gray-500">
              Cuántos contactos procesa el dispatcher en una sola tanda antes de aplicar el cool-down.
              Valor recomendado: <strong>10–20</strong>. Más alto = más rápido pero más riesgo de detección.
            </p>
            <input
              type="number" min={5} max={100}
              value={batchSize}
              onChange={(e) => setBatchSize(parseInt(e.target.value) || 15)}
              className={inputClass + ' max-w-[140px]'}
            />
            <p className="mt-1 text-[11px] text-gray-400">Rango: 5–100. Default: 15.</p>
          </div>
        </div>
      </div>

      {/* Cool-down */}
      <div className="rounded-lg border border-gray-200 bg-white p-5">
        <div className="flex items-start gap-3">
          <Timer className="mt-0.5 h-5 w-5 shrink-0 text-amber-500" />
          <div className="flex-1">
            <label className="block text-sm font-semibold text-gray-900">Pausa entre batches (minutos)</label>
            <p className="mt-0.5 text-xs text-gray-500">
              Cuánto tiempo el dispatcher deja "respirar" la campaña entre tandas. Durante este lapso
              la campaña NO se procesa aunque haya contactos pendientes. Recomendado: <strong>15–30 min</strong>.
              0 = sin pausa (las tandas corren consecutivas, solo cambia el batch size).
            </p>
            <input
              type="number" min={0} max={120}
              value={coolDown}
              onChange={(e) => setCoolDown(parseInt(e.target.value) || 0)}
              className={inputClass + ' max-w-[140px]'}
            />
            <p className="mt-1 text-[11px] text-gray-400">Rango: 0–120. Default: 20.</p>
          </div>
        </div>
      </div>

      {/* Auto-pause */}
      <div className="rounded-lg border border-gray-200 bg-white p-5">
        <div className="flex items-start gap-3">
          <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0 text-red-500" />
          <div className="flex-1">
            <label className="block text-sm font-semibold text-gray-900">
              Auto-pausa si % de fallos por batch supera
            </label>
            <p className="mt-0.5 text-xs text-gray-500">
              Si un batch termina con más de este porcentaje de fallos (mensajes que UltraMsg confirmó
              como NO entregados), el sistema pausa la campaña automáticamente y notifica por email a
              los admins del tenant. Default <strong>30%</strong>. 0 = deshabilitado (no auto-pausa nunca).
            </p>
            <div className="mt-1 flex items-center gap-2">
              <input
                type="number" min={0} max={100} step={5}
                value={autoPauseRate}
                onChange={(e) => setAutoPauseRate(parseFloat(e.target.value) || 0)}
                className={inputClass + ' max-w-[120px]'}
              />
              <span className="text-sm text-gray-500">%</span>
            </div>
            <p className="mt-1 text-[11px] text-gray-400">Rango: 0–100. Default: 30.</p>
          </div>
        </div>
      </div>

      {/* Simulador */}
      <div className="rounded-lg border border-gray-200 bg-gray-50 p-4">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-gray-600">
          Simulación con tus valores actuales
        </h3>
        <div className="mt-2 grid grid-cols-2 gap-3">
          <div className="rounded-md border border-gray-200 bg-white p-3">
            <p className="text-[11px] uppercase tracking-wide text-gray-500">95 contactos</p>
            <p className="mt-0.5 text-lg font-bold text-gray-900">{sample95.batches} batches</p>
            <p className="text-xs text-gray-500">~{sample95.hours}h en total</p>
          </div>
          <div className="rounded-md border border-gray-200 bg-white p-3">
            <p className="text-[11px] uppercase tracking-wide text-gray-500">500 contactos</p>
            <p className="mt-0.5 text-lg font-bold text-gray-900">{sample500.batches} batches</p>
            <p className="text-xs text-gray-500">~{sample500.hours}h en total</p>
          </div>
        </div>
        <p className="mt-2 text-[11px] text-gray-500">
          Estimaciones aproximadas: cada envío tarda ~20s y se suma la pausa entre batches.
          El horario laboral del tenant ({tenant.businessHoursStart}–{tenant.businessHoursEnd}) limita el
          tiempo total disponible por día.
        </p>
      </div>

      {/* Save */}
      <div className="flex items-center justify-end gap-3 pt-2">
        {saved && (
          <span className="flex items-center gap-1 text-xs text-emerald-600">
            <CheckCircle className="h-3.5 w-3.5" /> Guardado
          </span>
        )}
        {err && (
          <span className="flex items-center gap-1 text-xs text-red-600">
            <AlertCircle className="h-3.5 w-3.5" /> {err}
          </span>
        )}
        <button
          type="button"
          onClick={handleSave}
          disabled={update.isPending}
          className="flex items-center gap-1.5 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50 transition-colors"
        >
          {update.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
          Guardar
        </button>
      </div>
    </div>
  )
}
