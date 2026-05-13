import { useMemo, useState } from 'react'
import { AlertTriangle, Clock, RefreshCcw, CheckCircle2, XCircle, Users, ChevronLeft, ChevronRight } from 'lucide-react'
import {
  useInboxSummary,
  useInboxItems,
  useRetryInboxItem,
  useTenantsLite,
  type InboxItem,
} from '../hooks/useInboxMonitor'

// Etiquetas en español — el código interno sigue siendo el inglés (Pending,
// Replied, etc.) porque está en la BD; solo cambia el texto que se muestra.
const STATUS_OPTIONS: { value: string; label: string }[] = [
  { value: '',           label: 'Todos'      },
  { value: 'Pending',    label: 'Pendiente'  },
  { value: 'Claimed',    label: 'Reclamado'  },
  { value: 'Processing', label: 'Procesando' },
  { value: 'Replied',    label: 'Respondido' },
  { value: 'Failed',     label: 'Fallido'    },
  { value: 'Escalated',  label: 'Escalado'   },
]

const STATUS_LABEL: Record<string, string> = Object.fromEntries(
  STATUS_OPTIONS.filter(s => s.value).map(s => [s.value, s.label])
)

const PAGE_SIZE = 50
const PANAMA_TZ = 'America/Panama'

/** El API devuelve fechas en UTC sin marcador 'Z'. Las parseamos como UTC. */
function parseUtc(iso: string): Date {
  return /[Zz]|[+-]\d{2}:?\d{2}$/.test(iso) ? new Date(iso) : new Date(iso + 'Z')
}

/** Formato hora Panamá (UTC-5), formato local '13/05/2026 21:14:59'. */
function formatPanama(iso: string): string {
  return parseUtc(iso).toLocaleString('es-PA', {
    timeZone: PANAMA_TZ,
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
    hour12: false,
  })
}

/** Convierte un <input type="datetime-local"> (hora Panamá) a ISO UTC para enviar. */
function panamaLocalToUtcIso(local: string): string {
  if (!local) return ''
  // Panamá no usa DST. Offset fijo UTC-5. "2026-05-13T14:00" → "2026-05-13T19:00:00Z"
  const [d, t] = local.split('T')
  const [Y, M, D] = d.split('-').map(Number)
  const [h, m] = t.split(':').map(Number)
  // Construir como si fuera UTC, después sumar 5h para llegar al UTC real.
  const utc = new Date(Date.UTC(Y, M - 1, D, h + 5, m, 0))
  return utc.toISOString()
}

/** Default del input: ahora en TZ Panamá, formato YYYY-MM-DDTHH:mm. */
function nowPanamaForInput(offsetHours = 0): string {
  const now = new Date(Date.now() + offsetHours * 3600_000)
  const fmt = new Intl.DateTimeFormat('en-CA', {
    timeZone: PANAMA_TZ,
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit',
    hour12: false,
  })
  const parts = Object.fromEntries(fmt.formatToParts(now).map(p => [p.type, p.value]))
  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`
}

export function InboxMonitorPage() {
  const [statusFilter, setStatusFilter] = useState<string>('')
  const [phoneFilter, setPhoneFilter]   = useState('')
  const [tenantFilter, setTenantFilter] = useState('')
  const [fromLocal, setFromLocal]       = useState(() => nowPanamaForInput(-24))
  const [toLocal, setToLocal]           = useState(() => nowPanamaForInput(0))
  const [page, setPage]                 = useState(0)

  const summary = useInboxSummary(24)
  const tenants = useTenantsLite()
  const retry   = useRetryInboxItem()

  const filterArgs = useMemo(() => ({
    status:   statusFilter || undefined,
    tenantId: tenantFilter || undefined,
    phone:    phoneFilter || undefined,
    from:     fromLocal ? panamaLocalToUtcIso(fromLocal) : undefined,
    to:       toLocal   ? panamaLocalToUtcIso(toLocal)   : undefined,
    take:     PAGE_SIZE,
    skip:     page * PAGE_SIZE,
  }), [statusFilter, tenantFilter, phoneFilter, fromLocal, toLocal, page])

  const items = useInboxItems(filterArgs)

  const byStatusMap: Record<string, number> = {}
  summary.data?.byStatus.forEach((s) => { byStatusMap[s.status] = s.count })

  const total = items.data?.total ?? 0
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE))

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-bold text-gray-900">Monitor de cola de mensajes</h1>
        <p className="text-sm text-gray-600">
          Estado en tiempo real de InboundMessageQueue (todos los tenants). Solo visible a super admin.
          Hora mostrada en zona horaria de Panamá (UTC-5).
        </p>
      </header>

      {summary.data && (
        <section className="grid grid-cols-1 gap-4 md:grid-cols-4">
          <KpiCard label="Pendientes"             value={byStatusMap['Pending']    ?? 0} icon={<Clock className="h-5 w-5 text-blue-600" />} />
          <KpiCard label="Atascados (> 2 min)"    value={summary.data.stuckCount}       icon={<AlertTriangle className="h-5 w-5 text-amber-600" />} emphasized={summary.data.stuckCount > 0} />
          <KpiCard label="Fallidos (últimas 24h)" value={byStatusMap['Failed']     ?? 0} icon={<XCircle className="h-5 w-5 text-red-600" />} emphasized={(byStatusMap['Failed'] ?? 0) > 0} />
          <KpiCard label="Escalados"              value={byStatusMap['Escalated']  ?? 0} icon={<Users className="h-5 w-5 text-purple-600" />} />
        </section>
      )}

      {summary.data?.oldestUnresolved && (
        <section className="rounded-lg border border-amber-300 bg-amber-50 p-4">
          <div className="flex items-start gap-3">
            <AlertTriangle className="mt-0.5 h-5 w-5 text-amber-700" />
            <div className="flex-1">
              <p className="text-sm font-semibold text-amber-900">Caso más antiguo sin resolver</p>
              <p className="mt-1 text-sm text-amber-800">
                Tenant{' '}
                <span className="font-medium">
                  {summary.data.oldestUnresolved.tenantName ?? `${summary.data.oldestUnresolved.tenantId.slice(0, 8)}…`}
                </span>
                {' '}— teléfono{' '}
                <span className="font-mono">{summary.data.oldestUnresolved.fromPhone}</span>
                {' '}— recibido <RelativeTime iso={summary.data.oldestUnresolved.firstReceivedAt} />
                {' '}({STATUS_LABEL[summary.data.oldestUnresolved.status] ?? summary.data.oldestUnresolved.status})
              </p>
            </div>
          </div>
        </section>
      )}

      <section className="space-y-4 rounded-lg border border-gray-200 bg-white p-4">
        <h2 className="text-sm font-semibold text-gray-700">Filtros</h2>

        <div className="grid grid-cols-1 gap-3 md:grid-cols-4">
          <Field label="Estado">
            <select
              value={statusFilter}
              onChange={(e) => { setStatusFilter(e.target.value); setPage(0) }}
              className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
            >
              {STATUS_OPTIONS.map(opt => (
                <option key={opt.value || 'all'} value={opt.value}>{opt.label}</option>
              ))}
            </select>
          </Field>

          <Field label="Tenant">
            <select
              value={tenantFilter}
              onChange={(e) => { setTenantFilter(e.target.value); setPage(0) }}
              className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
            >
              <option value="">Todos</option>
              {tenants.data?.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
            </select>
          </Field>

          <Field label="Teléfono">
            <input
              value={phoneFilter}
              onChange={(e) => { setPhoneFilter(e.target.value); setPage(0) }}
              placeholder="+5076000…"
              className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm font-mono"
            />
          </Field>

          <Field label="Desde (hora Panamá)">
            <input
              type="datetime-local"
              value={fromLocal}
              onChange={(e) => { setFromLocal(e.target.value); setPage(0) }}
              className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
            />
          </Field>

          <Field label="Hasta (hora Panamá)">
            <input
              type="datetime-local"
              value={toLocal}
              onChange={(e) => { setToLocal(e.target.value); setPage(0) }}
              className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
            />
          </Field>

          <div className="flex items-end gap-2 md:col-span-3 md:justify-end">
            <button
              onClick={() => {
                setStatusFilter(''); setTenantFilter(''); setPhoneFilter('')
                setFromLocal(nowPanamaForInput(-24)); setToLocal(nowPanamaForInput(0))
                setPage(0)
              }}
              className="rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50"
            >
              Limpiar filtros
            </button>
            <span className="text-xs text-gray-500">
              {items.isFetching ? 'Cargando…' : `${total} ${total === 1 ? 'resultado' : 'resultados'}`}
            </span>
          </div>
        </div>
      </section>

      <section className="rounded-lg border border-gray-200 bg-white">
        {items.isError && <p className="p-4 text-sm text-red-600">Error cargando items.</p>}

        {items.data && (
          <>
            <table className="min-w-full divide-y divide-gray-200 text-sm">
              <thead className="bg-gray-50">
                <tr>
                  <Th>Recibido</Th>
                  <Th>Estado</Th>
                  <Th>Tenant</Th>
                  <Th>Teléfono</Th>
                  <Th>Edad</Th>
                  <Th>Intentos</Th>
                  <Th>Último error</Th>
                  <Th>Acciones</Th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 bg-white">
                {items.data.items.map((it) => (
                  <Row key={it.id} item={it} onRetry={() => retry.mutate(it.id)} />
                ))}
                {items.data.items.length === 0 && (
                  <tr>
                    <td colSpan={8} className="px-4 py-8 text-center text-sm text-gray-500">
                      Sin items para estos filtros
                    </td>
                  </tr>
                )}
              </tbody>
            </table>

            {total > PAGE_SIZE && (
              <div className="flex items-center justify-between border-t border-gray-200 px-4 py-2 text-xs text-gray-600">
                <span>
                  Mostrando {page * PAGE_SIZE + 1}–{Math.min((page + 1) * PAGE_SIZE, total)} de {total}
                </span>
                <div className="flex items-center gap-1">
                  <button
                    onClick={() => setPage(p => Math.max(0, p - 1))}
                    disabled={page === 0}
                    className="rounded-md border border-gray-300 p-1 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-40"
                  >
                    <ChevronLeft className="h-4 w-4" />
                  </button>
                  <span className="px-2">Página {page + 1} / {totalPages}</span>
                  <button
                    onClick={() => setPage(p => Math.min(totalPages - 1, p + 1))}
                    disabled={page >= totalPages - 1}
                    className="rounded-md border border-gray-300 p-1 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-40"
                  >
                    <ChevronRight className="h-4 w-4" />
                  </button>
                </div>
              </div>
            )}
          </>
        )}
      </section>
    </div>
  )
}

function Row({ item, onRetry }: { item: InboxItem; onRetry: () => void }) {
  const canRetry = item.status === 'Failed' || item.status === 'Escalated' || item.status === 'Claimed'
  return (
    <tr>
      <Td className="font-mono text-xs">{formatPanama(item.firstReceivedAt)}</Td>
      <Td><StatusPill status={item.status} /></Td>
      <Td title={item.tenantId}>{item.tenantName ?? <span className="font-mono text-xs text-gray-500">{item.tenantId.slice(0, 8)}…</span>}</Td>
      <Td className="font-mono">{item.fromPhone}</Td>
      <Td>{formatAge(item.ageSec)}</Td>
      <Td>{item.attemptCount}</Td>
      <td className="max-w-xs truncate px-4 py-2 text-xs text-gray-600" title={item.lastError ?? ''}>
        {item.lastErrorStep && <span className="mr-1 rounded bg-gray-100 px-1.5 py-0.5 text-xs">{item.lastErrorStep}</span>}
        {item.lastError ?? '—'}
      </td>
      <Td>
        {canRetry && (
          <button
            onClick={onRetry}
            className="inline-flex items-center gap-1 rounded-md bg-blue-50 px-2 py-1 text-xs font-medium text-blue-700 hover:bg-blue-100"
          >
            <RefreshCcw className="h-3 w-3" />
            Reintentar
          </button>
        )}
      </Td>
    </tr>
  )
}

function StatusPill({ status }: { status: string }) {
  const palette: Record<string, string> = {
    Pending:    'bg-blue-50 text-blue-700',
    Claimed:    'bg-indigo-50 text-indigo-700',
    Processing: 'bg-violet-50 text-violet-700',
    Replied:    'bg-emerald-50 text-emerald-700',
    Failed:     'bg-red-50 text-red-700',
    Escalated:  'bg-amber-50 text-amber-800',
  }
  const cls = palette[status] ?? 'bg-gray-50 text-gray-700'
  const Icon =
    status === 'Replied'   ? CheckCircle2 :
    status === 'Failed'    ? XCircle :
    status === 'Escalated' ? Users :
    Clock
  return (
    <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${cls}`}>
      <Icon className="h-3 w-3" />
      {STATUS_LABEL[status] ?? status}
    </span>
  )
}

function KpiCard({
  label, value, icon, emphasized = false,
}: {
  label: string; value: number; icon: React.ReactNode; emphasized?: boolean
}) {
  return (
    <div className={`rounded-lg border bg-white p-4 ${emphasized ? 'border-amber-300 shadow-sm' : 'border-gray-200'}`}>
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-gray-600">{label}</span>
        {icon}
      </div>
      <p className={`mt-2 text-3xl font-bold ${emphasized ? 'text-amber-700' : 'text-gray-900'}`}>{value}</p>
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-gray-600">{label}</span>
      {children}
    </label>
  )
}

function Th({ children }: { children: React.ReactNode }) {
  return <th className="px-4 py-2 text-left text-xs font-semibold uppercase tracking-wide text-gray-500">{children}</th>
}
function Td({ children, className = '', title }: { children: React.ReactNode; className?: string; title?: string }) {
  return <td className={`px-4 py-2 ${className}`} title={title}>{children}</td>
}

function RelativeTime({ iso }: { iso: string }) {
  const ms = Date.now() - parseUtc(iso).getTime()
  const sec = Math.floor(ms / 1000)
  if (sec < 60) return <>hace {sec}s</>
  const min = Math.floor(sec / 60)
  if (min < 60) return <>hace {min} min</>
  const hr = Math.floor(min / 60)
  return <>hace {hr}h</>
}

function formatAge(sec: number) {
  if (sec < 60) return `${sec}s`
  if (sec < 3600) return `${Math.floor(sec / 60)}m ${sec % 60}s`
  return `${Math.floor(sec / 3600)}h ${Math.floor((sec % 3600) / 60)}m`
}
