import { Fragment, useState } from 'react'
import {
  ChevronRight, Loader2, AlertCircle, CheckCircle2,
  XCircle, Clock, Users, TrendingUp, ChevronLeft, CalendarRange, X,
} from 'lucide-react'
import {
  useDelinquencyExecutions,
  useContactGroups,
  useGroupItems,
  useFieldMappings,
  type DelinquencyExecution,
  type FieldMapping,
} from '../hooks/useMorosidad'

interface Props {
  actionId: string
  exportButton?: React.ComponentType<{ executionId: string }>
}

const statusIcon: Record<string, JSX.Element> = {
  Completed:       <CheckCircle2 className="h-4 w-4 text-green-500" />,
  PartiallyFailed: <AlertCircle className="h-4 w-4 text-amber-500" />,
  Failed:          <XCircle className="h-4 w-4 text-red-500" />,
  Running:         <Loader2 className="h-4 w-4 animate-spin text-blue-500" />,
  Pending:         <Clock className="h-4 w-4 text-gray-400" />,
}

const statusLabel: Record<string, string> = {
  Completed:       'Completado',
  PartiallyFailed: 'Parcial',
  Failed:          'Fallido',
  Running:         'Ejecutando',
  Pending:         'Pendiente',
}

const groupStatusColor: Record<string, string> = {
  Pending:         'bg-gray-100 text-gray-600',
  CampaignCreated: 'bg-sky-100 text-sky-700',
  MessageSent:     'bg-emerald-100 text-emerald-700',
  ClientReplied:   'bg-emerald-200 text-emerald-800',
  Notified:        'bg-blue-100 text-blue-700',
  Skipped:         'bg-yellow-100 text-yellow-700',
}

const groupStatusLabel: Record<string, string> = {
  Pending:         'Pendiente',
  CampaignCreated: 'Campaña creada',
  MessageSent:     'Enviado',
  ClientReplied:   'Respondió',
  Notified:        'Notificado',
  Skipped:         'Omitido',
}

function fmtDateShort(iso: string | null) {
  if (!iso) return null
  return new Intl.DateTimeFormat('es-PA', {
    timeZone: 'America/Panama',
    day: '2-digit', month: '2-digit',
    hour: '2-digit', minute: '2-digit', hour12: false,
  }).format(new Date(iso))
}

function fmtDate(iso: string) {
  return new Intl.DateTimeFormat('es-PA', {
    timeZone: 'America/Panama',
    day: '2-digit', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit', hour12: false,
  }).format(new Date(iso))
}

function fmtAmount(n: number) {
  return new Intl.NumberFormat('es-PA', { style: 'currency', currency: 'USD' }).format(n)
}

function parseExtracted(json: string | null): Record<string, string | null> {
  if (!json) return {}
  try { return JSON.parse(json) ?? {} } catch { return {} }
}

function fmtCellValue(value: string | null | undefined, dataType: string): string {
  if (value == null || value === '') return '—'
  if (dataType === 'currency') {
    const n = parseFloat(value)
    return Number.isFinite(n) ? fmtAmount(n) : value
  }
  if (dataType === 'number') {
    const n = parseFloat(value)
    return Number.isFinite(n) ? n.toLocaleString('es-PA') : value
  }
  return value
}

// ─── Fila expandible con detalle dinámico ───────────────────────────────────
function GroupItemsRow({
  executionId, groupId, mappings,
}: {
  executionId: string
  groupId: string
  mappings: FieldMapping[]
}) {
  const { data: items, isLoading } = useGroupItems(executionId, groupId)

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 text-xs text-gray-400">
        <Loader2 className="h-4 w-4 animate-spin" /> Cargando pólizas...
      </div>
    )
  }
  if (!items || items.length === 0) {
    return <p className="text-xs text-gray-400">Sin ítems.</p>
  }

  // Columnas de detalle: todas las del mapping ordenadas, excepto Phone (ya está en el grupo)
  const detailCols = mappings
    .filter(m => m.isEnabled && m.role !== 'Phone')
    .sort((a, b) => a.sortOrder - b.sortOrder)

  return (
    <table className="w-full text-xs">
      <thead className="text-[10px] uppercase tracking-wide text-gray-400">
        <tr>
          {detailCols.map(col => (
            <th
              key={col.id ?? col.columnKey}
              className={`pb-1 pr-4 ${col.dataType === 'currency' || col.dataType === 'number' ? 'text-right' : 'text-left'}`}
            >
              {col.displayName}
            </th>
          ))}
          <th className="pb-1 text-left">Estado</th>
        </tr>
      </thead>
      <tbody className="divide-y divide-gray-100">
        {items.map(item => {
          const data = parseExtracted(item.extractedDataJson)
          return (
            <tr key={item.id}>
              {detailCols.map(col => {
                const raw = data[col.columnKey] ?? null
                const align = col.dataType === 'currency' || col.dataType === 'number' ? 'text-right' : 'text-left'
                const mono  = col.dataType === 'phone' || col.dataType === 'number' || col.dataType === 'currency'
                return (
                  <td
                    key={col.id ?? col.columnKey}
                    className={`py-1 pr-4 ${align} ${mono ? 'font-mono' : ''} text-gray-700`}
                  >
                    {fmtCellValue(raw, col.dataType)}
                  </td>
                )
              })}
              <td className="py-1 text-gray-600">
                {item.status}
                {item.discardReason && (
                  <span className="ml-1 text-[10px] text-red-400">({item.discardReason})</span>
                )}
              </td>
            </tr>
          )
        })}
      </tbody>
    </table>
  )
}

// ─── Panel de grupos para una ejecución ──────────────────────────────────────
function GroupsPanel({
  executionId, mappings, onClose,
}: {
  executionId: string
  mappings: FieldMapping[]
  onClose: () => void
}) {
  const [page, setPage] = useState(1)
  const [expandedGroup, setExpandedGroup] = useState<string | null>(null)
  const { data, isLoading } = useContactGroups(executionId, page)

  const groups = data?.groups ?? []
  const total  = data?.total ?? 0

  return (
    <div className="mt-4 rounded-lg border border-blue-200 bg-blue-50/30">
      <div className="flex items-center justify-between border-b border-blue-200 px-4 py-3">
        <h3 className="text-sm font-semibold text-blue-800">
          Grupos de contacto — {total} en total
        </h3>
        <button
          onClick={onClose}
          className="rounded px-2 py-0.5 text-xs text-blue-600 hover:bg-blue-100"
        >
          Cerrar
        </button>
      </div>

      {isLoading ? (
        <div className="flex justify-center py-6">
          <Loader2 className="h-5 w-5 animate-spin text-blue-400" />
        </div>
      ) : groups.length === 0 ? (
        <p className="py-6 text-center text-sm text-gray-400">Sin grupos</p>
      ) : (
        <>
          <table className="w-full text-sm">
            <thead className="bg-blue-50 text-left">
              <tr>
                <th className="w-8 px-3 py-2.5" />
                <th className="px-4 py-2.5 font-medium text-blue-700">Teléfono</th>
                <th className="px-4 py-2.5 font-medium text-blue-700">Nombre</th>
                <th className="px-4 py-2.5 font-medium text-blue-700 text-right">Monto</th>
                <th className="px-4 py-2.5 font-medium text-blue-700 text-center">Registros</th>
                <th className="px-4 py-2.5 font-medium text-blue-700 text-center">Estado</th>
                <th className="px-4 py-2.5 font-medium text-blue-700 text-center">Enviado</th>
                <th className="px-4 py-2.5 font-medium text-blue-700 text-center">Respondió</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-blue-100">
              {groups.map((g) => (
                <Fragment key={g.id}>
                  <tr
                    className="cursor-pointer hover:bg-blue-50/50"
                    onClick={() => setExpandedGroup(expandedGroup === g.id ? null : g.id)}
                  >
                    <td className="px-3 py-2.5 text-center text-blue-400">
                      <ChevronRight
                        className={`inline h-3.5 w-3.5 transition-transform ${
                          expandedGroup === g.id ? 'rotate-90' : ''
                        }`}
                      />
                    </td>
                    <td className="px-4 py-2.5 font-mono text-xs text-gray-700">
                      {g.phoneNormalized}
                    </td>
                    <td className="px-4 py-2.5 text-gray-700">
                      {g.clientName ?? <span className="italic text-gray-400">—</span>}
                    </td>
                    <td className="px-4 py-2.5 text-right font-medium text-gray-800">
                      {fmtAmount(g.totalAmount)}
                    </td>
                    <td className="px-4 py-2.5 text-center text-gray-600">{g.itemCount}</td>
                    <td className="px-4 py-2.5 text-center">
                      <span
                        className={`inline-block rounded-full px-2 py-0.5 text-xs font-medium ${
                          groupStatusColor[g.status] ?? 'bg-gray-100 text-gray-600'
                        }`}
                      >
                        {groupStatusLabel[g.status] ?? g.status}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 text-center font-mono text-xs text-gray-600">
                      {fmtDateShort(g.firstMessageSentAt) ?? <span className="text-gray-300">—</span>}
                    </td>
                    <td className="px-4 py-2.5 text-center font-mono text-xs text-gray-600">
                      {fmtDateShort(g.firstClientReplyAt) ?? <span className="text-gray-300">—</span>}
                    </td>
                  </tr>
                  {expandedGroup === g.id && (
                    <tr>
                      <td colSpan={8} className="bg-gray-50 px-8 py-3">
                        <GroupItemsRow executionId={executionId} groupId={g.id} mappings={mappings} />
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
            </tbody>
          </table>

          {/* Paginación */}
          {total > 50 && (
            <div className="flex items-center justify-between border-t border-blue-200 px-4 py-2.5">
              <button
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                disabled={page === 1}
                className="flex items-center gap-1 rounded px-2 py-1 text-xs text-blue-600 hover:bg-blue-100 disabled:opacity-40"
              >
                <ChevronLeft className="h-3 w-3" /> Anterior
              </button>
              <span className="text-xs text-blue-600">
                Página {page} de {Math.ceil(total / 50)}
              </span>
              <button
                onClick={() => setPage((p) => p + 1)}
                disabled={page >= Math.ceil(total / 50)}
                className="flex items-center gap-1 rounded px-2 py-1 text-xs text-blue-600 hover:bg-blue-100 disabled:opacity-40"
              >
                Siguiente <ChevronRight className="h-3 w-3" />
              </button>
            </div>
          )}
        </>
      )}
    </div>
  )
}

// ─── Tab principal ────────────────────────────────────────────────────────────
export function MorosidadHistoryTab({ actionId, exportButton: ExportButton }: Props) {
  const [page, setPage] = useState(1)
  const [selectedExecution, setSelectedExecution] = useState<string | null>(null)
  const [from, setFrom] = useState('')
  const [to, setTo]     = useState('')

  const { data, isLoading, refetch } = useDelinquencyExecutions(actionId, page, from || undefined, to || undefined)
  const { data: mappings = [] } = useFieldMappings(actionId)

  const clearDates = () => { setFrom(''); setTo(''); setPage(1) }

  const executions: DelinquencyExecution[] = data?.items ?? []
  const total = data?.total ?? 0
  const totalPages = Math.ceil(total / 20)

  const toggleExecution = (id: string) =>
    setSelectedExecution((cur) => (cur === id ? null : id))

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="h-6 w-6 animate-spin text-gray-400" />
      </div>
    )
  }

  if (executions.length === 0) {
    return (
      <div className="rounded-lg border-2 border-dashed border-gray-200 py-14 text-center">
        <TrendingUp className="mx-auto h-8 w-8 text-gray-300" />
        <p className="mt-3 text-sm font-medium text-gray-500">Sin ejecuciones todavía</p>
        <p className="mt-1 text-xs text-gray-400">
          Las ejecuciones aparecerán aquí cuando el job de morosidad se dispare.
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-3">
      {/* Filtro de fechas + contador */}
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex items-center gap-2 rounded-lg border border-gray-200 bg-white px-3 py-2 shadow-sm">
          <CalendarRange className="h-4 w-4 shrink-0 text-gray-400" />
          <span className="text-xs text-gray-500">Desde</span>
          <input
            type="date"
            value={from}
            onChange={e => { setFrom(e.target.value); setPage(1) }}
            className="border-0 bg-transparent text-xs text-gray-700 focus:outline-none"
          />
          <span className="text-xs text-gray-400">—</span>
          <span className="text-xs text-gray-500">Hasta</span>
          <input
            type="date"
            value={to}
            onChange={e => { setTo(e.target.value); setPage(1) }}
            className="border-0 bg-transparent text-xs text-gray-700 focus:outline-none"
          />
          {(from || to) && (
            <button onClick={clearDates} className="ml-1 rounded p-0.5 text-gray-400 hover:text-gray-600">
              <X className="h-3.5 w-3.5" />
            </button>
          )}
        </div>
        <span className="text-sm text-gray-500">{total} ejecución{total !== 1 ? 'es' : ''}</span>
        <button
          onClick={() => refetch()}
          className="ml-auto rounded-md border border-gray-300 px-3 py-1.5 text-xs text-gray-600 hover:bg-gray-50"
        >
          Actualizar
        </button>
      </div>

      {executions.map((ex) => (
        <div
          key={ex.id}
          className="rounded-lg border border-gray-200 bg-white shadow-sm"
        >
          {/* Fila resumen */}
          <button
            onClick={() => toggleExecution(ex.id)}
            className="flex w-full items-center gap-3 px-4 py-3 text-left hover:bg-gray-50 rounded-lg"
          >
            {/* Status icon */}
            {statusIcon[ex.status] ?? <Clock className="h-4 w-4 text-gray-400" />}

            {/* Fecha */}
            <div className="flex-1 min-w-0">
              <p className="text-sm font-medium text-gray-800">
                {fmtDate(ex.startedAt)}
              </p>
              <p className="text-xs text-gray-400">
                {statusLabel[ex.status] ?? ex.status}
                {ex.completedAt &&
                  ` · ${Math.round(
                    (new Date(ex.completedAt).getTime() - new Date(ex.startedAt).getTime()) / 1000,
                  )}s`}
              </p>
            </div>

            {/* Métricas */}
            <div className="flex gap-4 text-xs text-gray-500 shrink-0">
              <div className="text-center">
                <p className="font-semibold text-gray-700">{ex.totalItems}</p>
                <p>ítems</p>
              </div>
              <div className="text-center">
                <p className="font-semibold text-gray-700">{ex.groupsCreated}</p>
                <p className="flex items-center gap-0.5">
                  <Users className="h-3 w-3" /> grupos
                </p>
              </div>
              {ex.campaignsCreated > 0 && (
                <div className="text-center">
                  <p className="font-semibold text-green-600">{ex.campaignsCreated}</p>
                  <p>campañas</p>
                </div>
              )}
              {ex.discardedItems > 0 && (
                <div className="text-center">
                  <p className="font-semibold text-red-500">{ex.discardedItems}</p>
                  <p>descartados</p>
                </div>
              )}
            </div>

            <div className="flex items-center gap-2 shrink-0">
              {ExportButton && <ExportButton executionId={ex.id} />}
              <ChevronRight
                className={`h-4 w-4 text-gray-400 transition-transform ${
                  selectedExecution === ex.id ? 'rotate-90' : ''
                }`}
              />
            </div>
          </button>

          {/* Error message */}
          {ex.errorMessage && (
            <div className="mx-4 mb-3 rounded bg-red-50 px-3 py-2 text-xs text-red-600">
              {ex.errorMessage}
            </div>
          )}

          {/* Grupos expandidos */}
          {selectedExecution === ex.id && (
            <div className="px-4 pb-4">
              <GroupsPanel
                executionId={ex.id}
                mappings={mappings}
                onClose={() => setSelectedExecution(null)}
              />
            </div>
          )}
        </div>
      ))}

      {/* Paginación de ejecuciones */}
      {totalPages > 1 && (
        <div className="flex items-center justify-between pt-2">
          <button
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            disabled={page === 1}
            className="flex items-center gap-1 rounded-md border border-gray-300 px-3 py-1.5 text-sm text-gray-600 hover:bg-gray-50 disabled:opacity-40"
          >
            <ChevronLeft className="h-4 w-4" /> Anterior
          </button>
          <span className="text-sm text-gray-500">
            {page} / {totalPages}
          </span>
          <button
            onClick={() => setPage((p) => p + 1)}
            disabled={page >= totalPages}
            className="flex items-center gap-1 rounded-md border border-gray-300 px-3 py-1.5 text-sm text-gray-600 hover:bg-gray-50 disabled:opacity-40"
          >
            Siguiente <ChevronRight className="h-4 w-4" />
          </button>
        </div>
      )}
    </div>
  )
}
