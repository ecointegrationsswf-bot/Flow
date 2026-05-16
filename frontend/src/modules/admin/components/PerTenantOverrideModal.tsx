import { useState } from 'react'
import { X, Loader2, Pin, Info } from 'lucide-react'
import {
  useCreateScheduledJob, previewCron,
  type ScheduledJob,
} from '@/modules/admin/hooks/useScheduledJobs'
import { useTenantsLite } from '@/modules/admin/hooks/useInboxMonitor'
import { getActionFriendlyName } from '@/shared/actionLabels'

/**
 * Modal para crear un override de cron por tenant para una acción específica.
 * El cron AllTenants existente sigue corriendo para los demás tenants; el
 * executor excluye automáticamente al tenant que tenga su propio cron activo,
 * para evitar doble ejecución.
 *
 * Se abre desde el icono ⚙️ junto a la fila de DOWNLOAD_DELINQUENCY_DATA en la
 * tabla Scheduled Jobs. Diseño minimalista: tenant + cron + atajos rápidos.
 */
interface Props {
  baseJob: ScheduledJob          // El job AllTenants original (para heredar action + cron por default)
  onClose: () => void
}

// Atajos de cron comunes para morosidad — el operador rara vez escribe cron a mano.
const SHORTCUTS: { label: string; cron: string; hint: string }[] = [
  { label: 'Cada día 7am',         cron: '0 7 * * *',  hint: 'Diario a las 7:00 AM Panamá' },
  { label: 'Cada lunes 7am',       cron: '0 7 * * 1',  hint: 'Cada semana, lunes 7:00 AM' },
  { label: 'Lun y jue 7am',        cron: '0 7 * * 1,4', hint: 'Dos veces por semana' },
  { label: 'Día 1 del mes 8am',    cron: '0 8 1 * *',  hint: 'Mensual, primer día del mes' },
]

export function PerTenantOverrideModal({ baseJob, onClose }: Props) {
  const { data: tenants } = useTenantsLite()
  const createJob = useCreateScheduledJob()

  const [tenantId, setTenantId] = useState<string>('')
  const [cron, setCron] = useState<string>(baseJob.cronExpression ?? '0 7 * * *')
  const [error, setError] = useState<string | null>(null)
  const [preview, setPreview] = useState<string | null>(null)
  const [previewing, setPreviewing] = useState(false)

  const onCronPreview = async (expr: string) => {
    setPreviewing(true); setPreview(null)
    try {
      const res = await previewCron(expr)
      if (!res.valid) { setPreview('⚠ Cron inválido: ' + (res.error ?? '')); return }
      const next = (res.nextOccurrencesUtc ?? []).slice(0, 3)
      setPreview(next.length === 0
        ? 'Cron válido pero sin próximas ejecuciones.'
        : 'Próximas: ' + next.map(t => new Date(t).toLocaleString('es-PA', { timeZone: 'America/Panama' })).join(' · '))
    } catch {
      setPreview('No se pudo validar el cron.')
    } finally {
      setPreviewing(false)
    }
  }

  const submit = async () => {
    setError(null)
    if (!tenantId) { setError('Selecciona un tenant.'); return }
    if (!cron.trim()) { setError('Cron es requerido.'); return }
    try {
      await createJob.mutateAsync({
        actionDefinitionId: baseJob.actionDefinitionId,
        triggerType: 'Cron',
        cronExpression: cron.trim(),
        triggerEvent: null,
        delayMinutes: null,
        scope: 'SingleTenant',
        isActive: true,
        contextId: tenantId,
      })
      onClose()
    } catch (e: unknown) {
      const ax = e as { response?: { data?: { error?: string } } }
      setError(ax.response?.data?.error ?? 'Error al crear el override.')
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onClose}>
      <div
        className="w-full max-w-xl rounded-lg border border-gray-700 bg-gray-950 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-start justify-between border-b border-gray-800 px-5 py-4">
          <div>
            <h2 className="text-lg font-semibold text-gray-100 flex items-center gap-2">
              <Pin className="h-4 w-4 text-emerald-400" />
              Personalizar por tenant
            </h2>
            <p className="mt-1 text-xs text-gray-400">
              Crea un cron específico para un tenant de la acción <span className="font-mono text-amber-300">{baseJob.actionName}</span> ({getActionFriendlyName(baseJob.actionName) || ''}).
            </p>
          </div>
          <button onClick={onClose} className="rounded p-1 text-gray-400 hover:bg-gray-800 hover:text-white">
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Body */}
        <div className="space-y-4 p-5">
          {/* Tenant selector */}
          <div>
            <label className="mb-1 block text-xs font-medium text-gray-300">Tenant *</label>
            <select
              value={tenantId}
              onChange={(e) => setTenantId(e.target.value)}
              className="w-full rounded-md border border-gray-700 bg-gray-900 px-3 py-2 text-sm text-gray-100 focus:border-emerald-500 focus:outline-none"
            >
              <option value="">— Selecciona un tenant —</option>
              {(tenants ?? []).map(t => (
                <option key={t.id} value={t.id}>{t.name}</option>
              ))}
            </select>
          </div>

          {/* Cron */}
          <div>
            <label className="mb-1 block text-xs font-medium text-gray-300">Expresión cron *</label>
            <div className="flex gap-2">
              <input
                value={cron}
                onChange={(e) => { setCron(e.target.value); setPreview(null) }}
                onBlur={() => cron.trim() && onCronPreview(cron.trim())}
                placeholder="0 7 * * 1"
                className="flex-1 rounded-md border border-gray-700 bg-gray-900 px-3 py-2 font-mono text-sm text-amber-200 focus:border-emerald-500 focus:outline-none"
              />
              <button
                type="button"
                onClick={() => onCronPreview(cron.trim())}
                disabled={previewing || !cron.trim()}
                className="rounded-md border border-gray-700 px-3 py-2 text-xs text-gray-300 hover:bg-gray-800 disabled:opacity-50"
              >
                {previewing ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : 'Validar'}
              </button>
            </div>
            {preview && (
              <p className={`mt-1.5 text-xs ${preview.startsWith('⚠') ? 'text-red-400' : 'text-gray-400'}`}>
                {preview}
              </p>
            )}
            <p className="mt-1.5 text-[11px] text-gray-500">
              Formato: <code className="text-amber-300">minuto hora dia-mes mes dia-semana</code> · hora Panamá.
            </p>
          </div>

          {/* Shortcuts */}
          <div>
            <label className="mb-1.5 block text-xs font-medium text-gray-400">Atajos comunes</label>
            <div className="flex flex-wrap gap-1.5">
              {SHORTCUTS.map(s => (
                <button
                  key={s.cron}
                  type="button"
                  onClick={() => { setCron(s.cron); onCronPreview(s.cron) }}
                  title={s.hint}
                  className={`rounded-md border px-2 py-1 text-xs transition-colors ${
                    cron === s.cron
                      ? 'border-emerald-500 bg-emerald-900/30 text-emerald-300'
                      : 'border-gray-700 text-gray-400 hover:border-gray-500 hover:text-gray-200'
                  }`}
                >
                  {s.label}
                </button>
              ))}
            </div>
          </div>

          {/* Info banner */}
          <div className="rounded-md border border-blue-900/50 bg-blue-950/40 px-3 py-2">
            <div className="flex gap-2">
              <Info className="mt-0.5 h-4 w-4 shrink-0 text-blue-400" />
              <p className="text-xs leading-relaxed text-blue-200/80">
                Cuando creas este override, el cron <span className="font-mono text-blue-300">AllTenants</span> existente
                <strong> ignorará a este tenant</strong> para evitar doble ejecución.
                Para volver al cron global, elimina este override desde la lista.
              </p>
            </div>
          </div>

          {error && (
            <div className="rounded-md border border-red-900/50 bg-red-950/40 px-3 py-2 text-sm text-red-300">
              {error}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-2 border-t border-gray-800 px-5 py-3">
          <button
            onClick={onClose}
            className="rounded-md border border-gray-700 px-3 py-1.5 text-sm text-gray-300 hover:bg-gray-800"
          >
            Cancelar
          </button>
          <button
            onClick={submit}
            disabled={createJob.isPending || !tenantId || !cron.trim()}
            className="flex items-center gap-1.5 rounded-md bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-50"
          >
            {createJob.isPending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            Crear cron para tenant
          </button>
        </div>
      </div>
    </div>
  )
}
