import { useMemo, useState } from 'react'
import { X, CheckCircle2, XCircle, AlertCircle, Loader2, Clock, ChevronDown, ChevronRight, Building2 } from 'lucide-react'
import {
  useScheduledJobExecutions,
  useScheduledJobExecutionItems,
  type JobExecution,
  type JobExecutionItem,
} from '@/modules/admin/hooks/useScheduledJobs'
import { useTenantTime } from '@/shared/hooks/useTenantTime'
import { useAdminTenants } from '@/modules/admin/hooks/useAdminTenants'

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
  const tt = useTenantTime()
  return (
    <>
      <tr className={expandable ? 'cursor-pointer hover:bg-gray-800/50' : ''} onClick={expandable ? onToggle : undefined}>
        <td className="px-2 py-1.5 text-gray-500">
          {expandable ? (
            isExpanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />
          ) : null}
        </td>
        <td className="px-2 py-1.5 font-mono text-xs text-gray-400">
          {tt.dateTimeShort(execution.startedAt)}
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

interface TenantBreakdown {
  tenantId: string | null
  tenantName: string
  ok: number
  failed: number
  skipped: number
  /** Mensajes únicos de error agrupados (ej: "El servicio está temporalmente no disponible." x3) */
  errors: { message: string; count: number }[]
}

/**
 * Agrupa la lista de items por tenant para mostrar UN renglón por tenant
 * en lugar de un renglón por contacto. Los mensajes de error idénticos
 * se cuentan; al admin le interesa "qué pasó por tenant", no quién falló.
 */
function aggregateByTenant(
  items: JobExecutionItem[],
  tenantNameById: Map<string, string>,
): TenantBreakdown[] {
  const map = new Map<string, TenantBreakdown>()
  for (const it of items) {
    const key = it.tenantId ?? '__null__'
    let entry = map.get(key)
    if (!entry) {
      entry = {
        tenantId: it.tenantId,
        tenantName: it.tenantId
          ? tenantNameById.get(it.tenantId) ?? `Tenant ${it.tenantId.slice(0, 8)}…`
          : '— sin tenant —',
        ok: 0, failed: 0, skipped: 0,
        errors: [],
      }
      map.set(key, entry)
    }
    if (it.status === 'Success') entry.ok++
    else if (it.status === 'Skipped') entry.skipped++
    else entry.failed++

    if (it.errorMessage) {
      const existing = entry.errors.find(e => e.message === it.errorMessage)
      if (existing) existing.count++
      else entry.errors.push({ message: it.errorMessage, count: 1 })
    }
  }
  return Array.from(map.values()).sort((a, b) => b.failed - a.failed)
}

function ExecutionDetail({ execution }: { execution: JobExecution }) {
  const { data: items, isLoading } = useScheduledJobExecutionItems(execution.id)
  const { data: tenants } = useAdminTenants()

  const tenantNameById = useMemo(() => {
    const m = new Map<string, string>()
    for (const t of tenants ?? []) m.set(t.id, t.name)
    return m
  }, [tenants])

  const breakdown = useMemo(
    () => aggregateByTenant(items ?? [], tenantNameById),
    [items, tenantNameById],
  )

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

      {!isLoading && breakdown.length > 0 && (
        <div className="overflow-hidden rounded border border-gray-800">
          <table className="w-full text-xs">
            <thead className="bg-gray-900 text-[10px] uppercase tracking-wider text-gray-500">
              <tr>
                <th className="px-3 py-2 text-left">Tenant</th>
                <th className="px-3 py-2 text-center w-24">Resultado</th>
                <th className="px-3 py-2 text-left">Causa de fallos</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-800">
              {breakdown.map((row) => (
                <tr key={row.tenantId ?? '__null__'} className="align-top">
                  <td className="px-3 py-2">
                    <div className="flex items-center gap-1.5 text-gray-200 font-medium">
                      <Building2 className="h-3.5 w-3.5 text-gray-500" />
                      {row.tenantName}
                    </div>
                  </td>
                  <td className="px-3 py-2 text-center">
                    <div className="flex items-center justify-center gap-2 text-[11px]">
                      {row.ok > 0 && <span className="text-green-400">{row.ok} OK</span>}
                      {row.failed > 0 && <span className="text-red-400">{row.failed} fallos</span>}
                      {row.skipped > 0 && <span className="text-amber-400">{row.skipped} skip</span>}
                    </div>
                  </td>
                  <td className="px-3 py-2">
                    {row.errors.length === 0 ? (
                      <span className="text-gray-600">— sin errores —</span>
                    ) : (
                      <ul className="space-y-1">
                        {row.errors.map((e, i) => (
                          <li key={i} className="rounded bg-red-950/30 px-2 py-1 text-[11px] text-red-300">
                            {e.count > 1 && <span className="mr-1.5 rounded bg-red-900/60 px-1.5 py-0.5 font-mono text-[10px]">×{e.count}</span>}
                            {e.message}
                          </li>
                        ))}
                      </ul>
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
