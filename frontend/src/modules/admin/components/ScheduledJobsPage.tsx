import { useState } from 'react'
import { format } from 'date-fns'
import {
  Calendar, Clock, Play, Pause, Trash2, RefreshCw, Plus,
  AlertCircle, CheckCircle2, XCircle, Loader2,
} from 'lucide-react'
import {
  useScheduledJobs, useDeleteScheduledJob, useRunScheduledJobNow,
  type ScheduledJob,
} from '@/modules/admin/hooks/useScheduledJobs'
import { getActionFriendlyName } from '@/shared/actionLabels'
import { ScheduledJobFormModal } from './ScheduledJobFormModal'
import { ScheduledJobHistoryModal } from './ScheduledJobHistoryModal'

export function ScheduledJobsPage() {
  const { data: jobs, isLoading, refetch } = useScheduledJobs()
  const deleteJob = useDeleteScheduledJob()
  const runNow = useRunScheduledJobNow()

  const [editing, setEditing] = useState<ScheduledJob | null>(null)
  const [creating, setCreating] = useState(false)
  const [historyJobId, setHistoryJobId] = useState<string | null>(null)

  const handleDelete = async (job: ScheduledJob) => {
    if (!confirm(`¿Eliminar el job "${getActionFriendlyName(job.actionName) || job.id}"? El historial también se borrará.`)) return
    await deleteJob.mutateAsync(job.id)
  }

  const handleRunNow = async (job: ScheduledJob) => {
    await runNow.mutateAsync(job.id)
    refetch()
  }

  return (
    <div className="p-6 space-y-4">
      <header className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-100">Scheduled Jobs</h1>
          <p className="text-sm text-gray-400">
            Trabajos programados que el ScheduledWebhookWorker monitorea cada 60s.
          </p>
        </div>
        <div className="flex gap-2">
          <button
            onClick={() => refetch()}
            className="flex items-center gap-1.5 rounded-md border border-gray-700 px-3 py-1.5 text-sm text-gray-300 hover:bg-gray-800"
          >
            <RefreshCw className="h-4 w-4" />
            Actualizar
          </button>
          <button
            onClick={() => setCreating(true)}
            className="flex items-center gap-1.5 rounded-md bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-700"
          >
            <Plus className="h-4 w-4" />
            Nuevo job
          </button>
        </div>
      </header>

      {isLoading && (
        <div className="flex items-center gap-2 text-gray-400">
          <Loader2 className="h-4 w-4 animate-spin" />
          Cargando jobs...
        </div>
      )}

      {!isLoading && (jobs?.length ?? 0) === 0 && (
        <div className="rounded-lg border border-dashed border-gray-700 bg-gray-900/40 p-8 text-center">
          <Calendar className="mx-auto mb-2 h-10 w-10 text-gray-600" />
          <p className="text-gray-400">Aún no hay jobs programados.</p>
          <button
            onClick={() => setCreating(true)}
            className="mt-3 text-sm text-blue-400 hover:text-blue-300"
          >
            Crea el primero
          </button>
        </div>
      )}

      {!isLoading && (jobs?.length ?? 0) > 0 && (
        <div className="overflow-hidden rounded-lg border border-gray-700">
          <table className="w-full">
            <thead className="bg-gray-900">
              <tr className="text-left text-xs uppercase tracking-wide text-gray-400">
                <th className="px-3 py-2">Acción</th>
                <th className="px-3 py-2">Trigger</th>
                <th className="px-3 py-2">Scope</th>
                <th className="px-3 py-2">Próxima ejecución</th>
                <th className="px-3 py-2">Último resultado</th>
                <th className="px-3 py-2">Estado</th>
                <th className="px-3 py-2 text-right">Acciones</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-800 text-sm text-gray-200">
              {jobs!.map((j) => (
                <tr key={j.id} className="bg-gray-950 hover:bg-gray-900">
                  <td className="px-3 py-2 font-medium">
                    <div>{getActionFriendlyName(j.actionName) || j.actionDefinitionId.slice(0, 8)}</div>
                    {j.actionName && (
                      <div className="text-[10px] text-gray-500 font-mono">{j.actionName}</div>
                    )}
                  </td>
                  <td className="px-3 py-2">
                    <TriggerCell job={j} />
                  </td>
                  <td className="px-3 py-2">
                    <ScopeBadge scope={j.scope} />
                  </td>
                  <td className="px-3 py-2 text-gray-400">
                    {j.nextRunAt
                      ? new Date(j.nextRunAt).toLocaleString('es-PA', { timeZone: 'America/Panama', hour12: false, day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })
                      : '—'}
                  </td>
                  <td className="px-3 py-2">
                    <StatusCell job={j} onClickHistory={() => setHistoryJobId(j.id)} />
                  </td>
                  <td className="px-3 py-2">
                    {j.isActive ? (
                      <span className="inline-flex items-center gap-1 text-xs text-green-400">
                        <CheckCircle2 className="h-3 w-3" /> Activo
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1 text-xs text-gray-500">
                        <Pause className="h-3 w-3" /> Pausado
                      </span>
                    )}
                  </td>
                  <td className="px-3 py-2">
                    <div className="flex items-center justify-end gap-1.5">
                      <button
                        onClick={() => handleRunNow(j)}
                        title="Ejecutar ahora"
                        className="rounded p-1.5 text-gray-400 hover:bg-gray-800 hover:text-white"
                      >
                        <Play className="h-4 w-4" />
                      </button>
                      <button
                        onClick={() => setEditing(j)}
                        title="Editar"
                        className="rounded p-1.5 text-gray-400 hover:bg-gray-800 hover:text-white"
                      >
                        <Clock className="h-4 w-4" />
                      </button>
                      <button
                        onClick={() => handleDelete(j)}
                        title="Eliminar"
                        className="rounded p-1.5 text-gray-400 hover:bg-gray-800 hover:text-red-400"
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {(creating || editing) && (
        <ScheduledJobFormModal
          job={editing}
          onClose={() => {
            setCreating(false)
            setEditing(null)
          }}
        />
      )}

      {historyJobId && (
        <ScheduledJobHistoryModal
          jobId={historyJobId}
          onClose={() => setHistoryJobId(null)}
        />
      )}
    </div>
  )
}

function TriggerCell({ job }: { job: ScheduledJob }) {
  if (job.triggerType === 'Cron')
    return <code className="rounded bg-gray-800 px-1.5 py-0.5 text-xs text-amber-300">{job.cronExpression}</code>
  if (job.triggerType === 'EventBased')
    return <span className="text-xs text-purple-300">▶ {job.triggerEvent}</span>
  return (
    <span className="text-xs text-cyan-300">
      ⏱ {job.triggerEvent} · +{job.delayMinutes}min
    </span>
  )
}

function ScopeBadge({ scope }: { scope: string }) {
  const cls = scope === 'AllTenants' ? 'bg-blue-900/50 text-blue-300'
    : scope === 'PerCampaign' ? 'bg-amber-900/50 text-amber-300'
    : 'bg-emerald-900/50 text-emerald-300'
  return <span className={`rounded px-1.5 py-0.5 text-xs ${cls}`}>{scope}</span>
}

function StatusCell({ job, onClickHistory }: { job: ScheduledJob; onClickHistory: () => void }) {
  if (!job.lastRunStatus) return <span className="text-xs text-gray-500">Nunca ejecutado</span>
  const Icon = job.lastRunStatus === 'Success' ? CheckCircle2
    : job.lastRunStatus === 'Failed' ? XCircle
    : job.lastRunStatus === 'Running' ? Loader2
    : AlertCircle
  const color = job.lastRunStatus === 'Success' ? 'text-green-400'
    : job.lastRunStatus === 'Failed' ? 'text-red-400'
    : job.lastRunStatus === 'Running' ? 'text-blue-400'
    : 'text-amber-400'
  return (
    <button
      onClick={onClickHistory}
      className="flex items-center gap-1 text-xs hover:underline"
    >
      <Icon className={`h-3.5 w-3.5 ${color} ${job.lastRunStatus === 'Running' ? 'animate-spin' : ''}`} />
      <span className={color}>{job.lastRunStatus}</span>
      {job.consecutiveFailures > 0 && (
        <span className="ml-1 text-red-400">×{job.consecutiveFailures}</span>
      )}
    </button>
  )
}
