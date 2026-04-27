import { useEffect, useState } from 'react'
import { X, AlertCircle, CheckCircle2 } from 'lucide-react'
import {
  useCreateScheduledJob, useUpdateScheduledJob, useAdminActions,
  previewCron, TRIGGER_EVENTS,
  type ScheduledJob, type TriggerType, type JobScope, type CronPreview,
} from '@/modules/admin/hooks/useScheduledJobs'
import { getActionFriendlyName } from '@/shared/actionLabels'

interface Props {
  job: ScheduledJob | null
  onClose: () => void
}

export function ScheduledJobFormModal({ job, onClose }: Props) {
  const { data: actions } = useAdminActions()
  const create = useCreateScheduledJob()
  const update = useUpdateScheduledJob()

  const [actionDefinitionId, setActionId] = useState(job?.actionDefinitionId ?? '')
  const [triggerType, setTriggerType] = useState<TriggerType>(job?.triggerType ?? 'Cron')
  const [cronExpression, setCron] = useState(job?.cronExpression ?? '0 18 * * *')
  const [triggerEvent, setEvent] = useState(job?.triggerEvent ?? 'CampaignStarted')
  const [delayMinutes, setDelay] = useState<number>(job?.delayMinutes ?? 60)
  const [scope, setScope] = useState<JobScope>(job?.scope ?? 'AllTenants')
  const [isActive, setActive] = useState(job?.isActive ?? true)
  const [error, setError] = useState<string | null>(null)
  const [cronPreview, setCronPreview] = useState<CronPreview | null>(null)

  useEffect(() => {
    if (triggerType !== 'Cron' || !cronExpression.trim()) {
      setCronPreview(null)
      return
    }
    let cancelled = false
    const t = setTimeout(async () => {
      try {
        const r = await previewCron(cronExpression)
        if (!cancelled) setCronPreview(r)
      } catch {
        if (!cancelled) setCronPreview({ valid: false, error: 'No se pudo validar' })
      }
    }, 400)
    return () => { cancelled = true; clearTimeout(t) }
  }, [cronExpression, triggerType])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    if (!actionDefinitionId) {
      setError('Selecciona una acción.')
      return
    }
    const payload = {
      actionDefinitionId,
      triggerType,
      cronExpression: triggerType === 'Cron' ? cronExpression : null,
      triggerEvent: triggerType !== 'Cron' ? triggerEvent : null,
      delayMinutes: triggerType === 'DelayFromEvent' ? delayMinutes : null,
      scope,
      isActive,
    }
    try {
      if (job) await update.mutateAsync({ id: job.id, ...payload })
      else await create.mutateAsync(payload)
      onClose()
    } catch (err: any) {
      setError(err.response?.data?.error ?? err.message ?? 'Error al guardar')
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
      <form
        onSubmit={handleSubmit}
        className="w-full max-w-xl space-y-4 rounded-lg border border-gray-700 bg-gray-900 p-5"
      >
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-semibold text-gray-100">
            {job ? 'Editar job' : 'Nuevo scheduled job'}
          </h2>
          <button type="button" onClick={onClose} className="text-gray-400 hover:text-white">
            <X className="h-5 w-5" />
          </button>
        </div>

        <Field label="Acción">
          <select
            value={actionDefinitionId}
            onChange={(e) => setActionId(e.target.value)}
            className="w-full rounded-md border border-gray-700 bg-gray-800 px-2 py-1.5 text-sm text-gray-100"
            required
          >
            <option value="">— Selecciona —</option>
            {actions?.map((a) => (
              <option key={a.id} value={a.id}>
                {getActionFriendlyName(a.name)} {!a.isActive && '(inactiva)'}
              </option>
            ))}
          </select>
        </Field>

        <Field label="Tipo de trigger">
          <select
            value={triggerType}
            onChange={(e) => setTriggerType(e.target.value as TriggerType)}
            className="w-full rounded-md border border-gray-700 bg-gray-800 px-2 py-1.5 text-sm text-gray-100"
          >
            <option value="Cron">Cron — horario fijo</option>
            <option value="EventBased">EventBased — al ocurrir un evento</option>
            <option value="DelayFromEvent">DelayFromEvent — N minutos tras un evento</option>
          </select>
        </Field>

        {triggerType === 'Cron' && (
          <>
            <Field label="Expresión cron (hora Panamá, formato 5 campos)">
              <input
                type="text"
                value={cronExpression}
                onChange={(e) => setCron(e.target.value)}
                placeholder="0 23 * * *"
                className="w-full rounded-md border border-gray-700 bg-gray-800 px-2 py-1.5 font-mono text-sm text-amber-300"
              />
            </Field>
            <CronPreviewBox preview={cronPreview} />
          </>
        )}

        {triggerType !== 'Cron' && (
          <Field label="Evento que dispara el job">
            <select
              value={triggerEvent}
              onChange={(e) => setEvent(e.target.value)}
              className="w-full rounded-md border border-gray-700 bg-gray-800 px-2 py-1.5 text-sm text-gray-100"
            >
              {TRIGGER_EVENTS.map((e) => (
                <option key={e} value={e}>{e}</option>
              ))}
            </select>
          </Field>
        )}

        {triggerType === 'DelayFromEvent' && (
          <Field label="Delay (minutos)">
            <input
              type="number"
              min={0}
              value={delayMinutes}
              onChange={(e) => setDelay(Number(e.target.value))}
              className="w-32 rounded-md border border-gray-700 bg-gray-800 px-2 py-1.5 text-sm text-gray-100"
            />
          </Field>
        )}

        <Field label="Scope">
          <select
            value={scope}
            onChange={(e) => setScope(e.target.value as JobScope)}
            className="w-full rounded-md border border-gray-700 bg-gray-800 px-2 py-1.5 text-sm text-gray-100"
          >
            <option value="AllTenants">AllTenants — global</option>
            <option value="PerCampaign">PerCampaign — uno por campaña</option>
            <option value="PerConversation">PerConversation — uno por conversación</option>
          </select>
        </Field>

        <label className="flex items-center gap-2 text-sm text-gray-300">
          <input
            type="checkbox"
            checked={isActive}
            onChange={(e) => setActive(e.target.checked)}
            className="rounded border-gray-700 bg-gray-800"
          />
          Activo (el Worker lo recogerá en el próximo ciclo)
        </label>

        {error && (
          <div className="flex items-start gap-2 rounded-md bg-red-950/50 p-2 text-sm text-red-300">
            <AlertCircle className="mt-0.5 h-4 w-4 flex-shrink-0" />
            <span>{error}</span>
          </div>
        )}

        <div className="flex justify-end gap-2 border-t border-gray-800 pt-3">
          <button
            type="button"
            onClick={onClose}
            className="rounded-md border border-gray-700 px-3 py-1.5 text-sm text-gray-300 hover:bg-gray-800"
          >
            Cancelar
          </button>
          <button
            type="submit"
            disabled={create.isPending || update.isPending}
            className="rounded-md bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
          >
            {job ? 'Guardar cambios' : 'Crear job'}
          </button>
        </div>
      </form>
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-gray-400">
        {label}
      </span>
      {children}
    </label>
  )
}

function CronPreviewBox({ preview }: { preview: CronPreview | null }) {
  if (!preview) return null
  if (!preview.valid) {
    return (
      <div className="flex items-center gap-2 rounded-md bg-red-950/40 px-3 py-2 text-xs text-red-300">
        <AlertCircle className="h-4 w-4" />
        Expresión inválida: {preview.error}
      </div>
    )
  }
  // El backend devuelve los próximos runs en UTC. Los convertimos a hora Panamá
  // para que el admin vea la hora real en que va a correr (la cron expression
  // ya se interpreta en zona Panamá del lado del backend).
  const fmtPanama = (utcIso: string) => {
    const d = new Date(utcIso)
    const date = d.toLocaleDateString('es-PA', { timeZone: 'America/Panama', year: 'numeric', month: '2-digit', day: '2-digit' })
    const time = d.toLocaleTimeString('es-PA', { timeZone: 'America/Panama', hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' })
    return `${date} ${time}`
  }
  return (
    <div className="rounded-md border border-gray-800 bg-gray-950 p-2 text-xs">
      <div className="mb-1 flex items-center gap-1.5 text-green-400">
        <CheckCircle2 className="h-3.5 w-3.5" />
        <span>Próximas 5 ejecuciones (hora Panamá):</span>
      </div>
      <ul className="space-y-0.5 text-gray-400">
        {preview.nextOccurrencesUtc?.map((d) => (
          <li key={d} className="font-mono">· {fmtPanama(d)}</li>
        ))}
      </ul>
    </div>
  )
}
