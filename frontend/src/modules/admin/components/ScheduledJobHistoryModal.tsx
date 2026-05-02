import { useState } from 'react'
import { X, CheckCircle2, XCircle, AlertCircle, Loader2, Clock, ChevronDown, ChevronRight } from 'lucide-react'
import {
  useScheduledJobExecutions,
  useScheduledJobExecutionItems,
  type JobExecution,
} from '@/modules/admin/hooks/useScheduledJobs'

interface Props {
  jobId: string
  onClose: () => void
}

export function ScheduledJobHistoryModal({ jobId, onClose }: Props) {
  const { data: execs, isLoading } = useScheduledJobExecutions(jobId)
  const [expandedId, setExpandedId] = useState<string | null>(null)

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
      <div className="flex w-full max-w-3xl flex-col rounded-lg border border-gray-700 bg-gray-900">
        <div className="flex items-center justify-between border-b border-gray-800 p-4">
          <h2 className="text-lg font-semibold text-gray-100">Historial de ejecuciones</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-white">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="max-h-[70vh] overflow-auto p-4">
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
                  <th className="w-6 px-2 py-1"></th>
                  <th className="px-2 py-1 text-left">Inicio (Panamá)</th>
                  <th className="px-2 py-1 text-left">Status</th>
                  <th className="px-2 py-1 text-left">Resultados</th>
                  <th className="px-2 py-1 text-left">Origen</th>
                  <th className="px-2 py-1 text-left">Contexto</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-800 text-gray-200">
                {execs.map((e) => {
                  const expandable = canExpand(e)
                  const isExpanded = expandedId === e.id
                  return (
                    <ExecutionRow
                      key={e.id}
                      execution={e}
                      expandable={expandable}
                      isExpanded={isExpanded}
                      onToggle={() => setExpandedId(isExpanded ? null : e.id)}
                    />
                  )
                })}
              </tbody>
            </table>
          )}
        </div>
      </div>
    </div>
  )
}

function canExpand(e: JobExecution): boolean {
  // Mostramos el toggle cuando hay algo que detallar:
  //   - cualquier ejecución con fallos (atribución por sub-item)
  //   - o un texto de error genérico que abrir
  return e.failureCount > 0 || !!e.errorDetail
}

function ExecutionRow({
  execution,
  expandable,
  isExpanded,
  onToggle,
}: {
  execution: JobExecution
  expandable: boolean
  isExpanded: boolean
  onToggle: () => void
}) {
  return (
    <>
      <tr className={expandable ? 'cursor-pointer hover:bg-gray-800/50' : ''} onClick={expandable ? onToggle : undefined}>
        <td className="px-2 py-1.5 text-gray-500">
          {expandable ? (
            isExpanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />
          ) : null}
        </td>
        <td className="px-2 py-1.5 font-mono text-xs text-gray-400">
          {new Date(execution.startedAt).toLocaleString('es-PA', {
            timeZone: 'America/Panama',
            hour12: false,
            day: '2-digit',
            month: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
          })}
        </td>
        <td className="px-2 py-1.5">
          <ExecStatus status={execution.status} />
        </td>
        <td className="px-2 py-1.5 text-xs text-gray-400">
          {execution.totalRecords > 0 ? (
            <>{execution.successCount}/{execution.totalRecords} OK · {execution.failureCount} fallos</>
          ) : (
            <span className="text-gray-600">—</span>
          )}
        </td>
        <td className="px-2 py-1.5 text-xs">
          <span className="rounded bg-gray-800 px-1.5 py-0.5 text-gray-300">{execution.triggeredBy ?? '—'}</span>
        </td>
        <td className="px-2 py-1.5 font-mono text-xs text-gray-500">{execution.contextId ?? '—'}</td>
      </tr>

      {expandable && isExpanded && (
        <tr className="bg-gray-950/40">
          <td colSpan={6} className="px-3 py-3">
            <ExecutionDetail execution={execution} />
          </td>
        </tr>
      )}
    </>
  )
}

function ExecutionDetail({ execution }: { execution: JobExecution }) {
  const { data: items, isLoading } = useScheduledJobExecutionItems(execution.id)

  return (
    <div className="space-y-3">
      {execution.errorDetail && (
        <div className="rounded border border-red-900/40 bg-red-950/30 p-2 text-xs text-red-300">
          <div className="mb-1 text-[10px] uppercase tracking-wider text-red-400/70">Resumen del error</div>
          <pre className="whitespace-pre-wrap break-words font-mono">{execution.errorDetail}</pre>
        </div>
      )}

      {isLoading && (
        <div className="flex items-center gap-2 text-xs text-gray-500">
          <Loader2 className="h-3 w-3 animate-spin" />
          Cargando detalle…
        </div>
      )}

      {!isLoading && items && items.length === 0 && (
        <div className="text-xs text-gray-500 italic">
          Sin detalle granular registrado para esta ejecución
          {execution.failureCount > 0 && ' (executor anterior a la auditoría por item).'}
        </div>
      )}

      {!isLoading && items && items.length > 0 && (
        <div className="overflow-hidden rounded border border-gray-800">
          <table className="w-full text-xs">
            <thead className="bg-gray-900 text-[10px] uppercase tracking-wider text-gray-500">
              <tr>
                <th className="px-2 py-1.5 text-left">Tipo</th>
                <th className="px-2 py-1.5 text-left">Item</th>
                <th className="px-2 py-1.5 text-left">Status</th>
                <th className="px-2 py-1.5 text-left">Detalle</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-800">
              {items.map((it) => (
                <tr key={it.id} className="align-top">
                  <td className="px-2 py-1.5 text-gray-400">
                    <span className="rounded bg-gray-800 px-1.5 py-0.5 text-[10px]">{it.contextType}</span>
                  </td>
                  <td className="px-2 py-1.5">
                    <div className="text-gray-200">{it.contextLabel ?? '—'}</div>
                    {it.contextId && (
                      <div className="font-mono text-[10px] text-gray-600">{it.contextId}</div>
                    )}
                  </td>
                  <td className="px-2 py-1.5">
                    <ItemStatus status={it.status} />
                  </td>
                  <td className="px-2 py-1.5">
                    {it.errorMessage ? (
                      <pre className="max-h-32 overflow-auto whitespace-pre-wrap break-words rounded bg-red-950/30 p-1.5 text-[11px] text-red-300">
                        {it.errorMessage}
                      </pre>
                    ) : (
                      <span className="text-gray-600">—</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

function ExecStatus({ status }: { status: string }) {
  const Icon =
    status === 'Success'
      ? CheckCircle2
      : status === 'Failed'
        ? XCircle
        : status === 'Running'
          ? Loader2
          : status === 'Pending'
            ? Clock
            : AlertCircle
  const color =
    status === 'Success'
      ? 'text-green-400'
      : status === 'Failed'
        ? 'text-red-400'
        : status === 'Running'
          ? 'text-blue-400'
          : status === 'Pending'
            ? 'text-amber-400'
            : 'text-amber-400'
  return (
    <span className={`inline-flex items-center gap-1 text-xs ${color}`}>
      <Icon className={`h-3.5 w-3.5 ${status === 'Running' ? 'animate-spin' : ''}`} />
      {status}
    </span>
  )
}

function ItemStatus({ status }: { status: string }) {
  const color =
    status === 'Success' ? 'bg-green-900/40 text-green-300'
      : status === 'Failed' ? 'bg-red-900/40 text-red-300'
      : status === 'Skipped' ? 'bg-gray-800 text-gray-400'
      : 'bg-amber-900/40 text-amber-300'
  return <span className={`rounded px-1.5 py-0.5 text-[10px] ${color}`}>{status}</span>
}
