import { format } from 'date-fns'
import { X, CheckCircle2, XCircle, AlertCircle, Loader2, Clock } from 'lucide-react'
import { useScheduledJobExecutions } from '@/modules/admin/hooks/useScheduledJobs'

interface Props {
  jobId: string
  onClose: () => void
}

export function ScheduledJobHistoryModal({ jobId, onClose }: Props) {
  const { data: execs, isLoading } = useScheduledJobExecutions(jobId)

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
      <div className="flex w-full max-w-2xl flex-col rounded-lg border border-gray-700 bg-gray-900">
        <div className="flex items-center justify-between border-b border-gray-800 p-4">
          <h2 className="text-lg font-semibold text-gray-100">Historial de ejecuciones</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-white">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="max-h-[60vh] overflow-auto p-4">
          {isLoading && (
            <div className="flex items-center gap-2 text-sm text-gray-400">
              <Loader2 className="h-4 w-4 animate-spin" />
              Cargando historial...
            </div>
          )}

          {!isLoading && (execs?.length ?? 0) === 0 && (
            <div className="rounded-lg border border-dashed border-gray-700 p-6 text-center text-gray-500">
              <Clock className="mx-auto mb-2 h-8 w-8" />
              <p>Sin ejecuciones todavía.</p>
            </div>
          )}

          {!isLoading && execs && execs.length > 0 && (
            <table className="w-full text-sm">
              <thead className="text-xs uppercase tracking-wide text-gray-500">
                <tr>
                  <th className="px-2 py-1 text-left">Inicio (UTC)</th>
                  <th className="px-2 py-1 text-left">Status</th>
                  <th className="px-2 py-1 text-left">Resultados</th>
                  <th className="px-2 py-1 text-left">Origen</th>
                  <th className="px-2 py-1 text-left">Contexto</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-800 text-gray-200">
                {execs.map((e) => (
                  <tr key={e.id}>
                    <td className="px-2 py-1.5 font-mono text-xs text-gray-400">
                      {new Date(e.startedAt).toLocaleString('es-PA', { timeZone: 'America/Panama', hour12: false, day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                    </td>
                    <td className="px-2 py-1.5">
                      <ExecStatus status={e.status} />
                    </td>
                    <td className="px-2 py-1.5 text-xs text-gray-400">
                      {e.totalRecords > 0
                        ? <>{e.successCount}/{e.totalRecords} OK · {e.failureCount} fallos</>
                        : <span className="text-gray-600">—</span>}
                    </td>
                    <td className="px-2 py-1.5 text-xs">
                      <span className="rounded bg-gray-800 px-1.5 py-0.5 text-gray-300">
                        {e.triggeredBy ?? '—'}
                      </span>
                    </td>
                    <td className="px-2 py-1.5 font-mono text-xs text-gray-500">
                      {e.contextId ?? '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {!isLoading && execs?.some((e) => e.errorDetail) && (
            <div className="mt-4 space-y-2">
              <h3 className="text-xs uppercase tracking-wide text-gray-500">Detalles de error</h3>
              {execs.filter((e) => e.errorDetail).map((e) => (
                <pre
                  key={e.id}
                  className="overflow-x-auto rounded border border-red-900/40 bg-red-950/30 p-2 text-xs text-red-300"
                >
                  [{new Date(e.startedAt).toLocaleTimeString('es-PA', { timeZone: 'America/Panama', hour12: false })}] {e.errorDetail}
                </pre>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

function ExecStatus({ status }: { status: string }) {
  const Icon = status === 'Success' ? CheckCircle2
    : status === 'Failed' ? XCircle
    : status === 'Running' ? Loader2
    : status === 'Pending' ? Clock
    : AlertCircle
  const color = status === 'Success' ? 'text-green-400'
    : status === 'Failed' ? 'text-red-400'
    : status === 'Running' ? 'text-blue-400'
    : status === 'Pending' ? 'text-amber-400'
    : 'text-amber-400'
  return (
    <span className={`inline-flex items-center gap-1 text-xs ${color}`}>
      <Icon className={`h-3.5 w-3.5 ${status === 'Running' ? 'animate-spin' : ''}`} />
      {status}
    </span>
  )
}
