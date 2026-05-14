import { useMemo, useState } from 'react'
import {
  Send, Mail, MessageSquare, Smartphone, XCircle, CheckCircle2,
  ChevronLeft, ChevronRight, Eye, X,
} from 'lucide-react'
import {
  useOutboundSummary,
  useOutboundItems,
  useOutboundItemDetail,
  type OutboundItem,
} from '../hooks/useOutboundMessages'
import { useTenantsLite } from '../hooks/useInboxMonitor'

const PAGE_SIZE = 50
const PANAMA_TZ = 'America/Panama'

const CHANNEL_OPTIONS: { value: string; label: string }[] = [
  { value: '',         label: 'Todos los canales' },
  { value: 'WhatsApp', label: 'WhatsApp' },
  { value: 'Email',    label: 'Email' },
  { value: 'Sms',      label: 'SMS' },
]

const STATUS_OPTIONS: { value: string; label: string }[] = [
  { value: '',          label: 'Todos los estados' },
  { value: 'Sent',      label: 'Enviado' },
  { value: 'Delivered', label: 'Entregado' },
  { value: 'Read',      label: 'Leído' },
  { value: 'Failed',    label: 'Fallido' },
]

const STATUS_LABEL: Record<string, string> = Object.fromEntries(
  STATUS_OPTIONS.filter(s => s.value).map(s => [s.value, s.label])
)

/** API devuelve fechas UTC sin marcador 'Z' — parsearlas siempre como UTC. */
function parseUtc(iso: string): Date {
  return /[Zz]|[+-]\d{2}:?\d{2}$/.test(iso) ? new Date(iso) : new Date(iso + 'Z')
}

/** Formato hora Panamá (UTC-5). */
function formatPanama(iso: string): string {
  return parseUtc(iso).toLocaleString('es-PA', {
    timeZone: PANAMA_TZ,
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
    hour12: false,
  })
}

function panamaLocalToUtcIso(local: string): string {
  if (!local) return ''
  const [d, t] = local.split('T')
  const [Y, M, D] = d.split('-').map(Number)
  const [h, m] = t.split(':').map(Number)
  const utc = new Date(Date.UTC(Y, M - 1, D, h + 5, m, 0))
  return utc.toISOString()
}

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

export function OutboundMessagesPage() {
  const [channelFilter, setChannelFilter] = useState('')
  const [statusFilter, setStatusFilter]   = useState('')
  const [tenantFilter, setTenantFilter]   = useState('')
  const [recipientFilter, setRecipientFilter] = useState('')
  const [fromLocal, setFromLocal] = useState(() => nowPanamaForInput(-24))
  const [toLocal, setToLocal]     = useState(() => nowPanamaForInput(0))
  const [page, setPage]           = useState(0)
  const [detailId, setDetailId]   = useState<string | null>(null)

  const summary = useOutboundSummary(24)
  const tenants = useTenantsLite()

  const filterArgs = useMemo(() => ({
    channel:   channelFilter || undefined,
    status:    statusFilter || undefined,
    tenantId:  tenantFilter || undefined,
    recipient: recipientFilter || undefined,
    from:      fromLocal ? panamaLocalToUtcIso(fromLocal) : undefined,
    to:        toLocal   ? panamaLocalToUtcIso(toLocal)   : undefined,
    take:      PAGE_SIZE,
    skip:      page * PAGE_SIZE,
  }), [channelFilter, statusFilter, tenantFilter, recipientFilter, fromLocal, toLocal, page])

  const items = useOutboundItems(filterArgs)

  // Conteos por canal en las últimas 24h — para los KPIs.
  const byChannelMap: Record<string, number> = {}
  summary.data?.byChannel.forEach(c => { if (c.channel) byChannelMap[c.channel] = c.count })
  const byStatusMap: Record<string, number> = {}
  summary.data?.byStatus.forEach(s => { byStatusMap[s.status] = s.count })

  const total = items.data?.total ?? 0
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE))

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-bold text-gray-900">Mensajes enviados</h1>
        <p className="text-sm text-gray-600">
          Salida de campañas y respuestas del agente IA hacia clientes (WhatsApp, Email, SMS) en todos los tenants.
          Solo visible para super admin. Hora en zona horaria de Panamá (UTC-5).
        </p>
      </header>

      {summary.data && (
        <section className="grid grid-cols-1 gap-4 md:grid-cols-5">
          <KpiCard label="Total enviados (24h)" value={summary.data.totalSent}        icon={<Send className="h-5 w-5 text-emerald-600" />} />
          <KpiCard label="Email"                 value={byChannelMap['Email']    ?? 0} icon={<Mail className="h-5 w-5 text-blue-600" />} />
          <KpiCard label="WhatsApp"              value={byChannelMap['WhatsApp'] ?? 0} icon={<MessageSquare className="h-5 w-5 text-green-600" />} />
          <KpiCard label="SMS"                   value={byChannelMap['Sms']      ?? 0} icon={<Smartphone className="h-5 w-5 text-amber-600" />} />
          <KpiCard label="Fallidos"              value={byStatusMap['Failed']    ?? 0} icon={<XCircle className="h-5 w-5 text-red-600" />}
            emphasized={(byStatusMap['Failed'] ?? 0) > 0} />
        </section>
      )}

      <section className="space-y-4 rounded-lg border border-gray-200 bg-white p-4">
        <h2 className="text-sm font-semibold text-gray-700">Filtros</h2>

        <div className="grid grid-cols-1 gap-3 md:grid-cols-4">
          <Field label="Canal">
            <select
              value={channelFilter}
              onChange={(e) => { setChannelFilter(e.target.value); setPage(0) }}
              className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
            >
              {CHANNEL_OPTIONS.map(opt => (
                <option key={opt.value || 'all'} value={opt.value}>{opt.label}</option>
              ))}
            </select>
          </Field>

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

          <Field label="Destinatario (email o teléfono)">
            <input
              value={recipientFilter}
              onChange={(e) => { setRecipientFilter(e.target.value); setPage(0) }}
              placeholder="cliente@dominio.com o +5076…"
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

          <div className="flex items-end gap-2 md:col-span-2 md:justify-end">
            <button
              onClick={() => {
                setChannelFilter(''); setStatusFilter(''); setTenantFilter('')
                setRecipientFilter('')
                setFromLocal(nowPanamaForInput(-24)); setToLocal(nowPanamaForInput(0))
                setPage(0)
              }}
              className="rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50"
            >
              Limpiar filtros
            </button>
            <span className="text-xs text-gray-500">
              {items.isFetching ? 'Cargando…' : `${total} ${total === 1 ? 'mensaje' : 'mensajes'}`}
            </span>
          </div>
        </div>
      </section>

      <section className="rounded-lg border border-gray-200 bg-white">
        {items.isError && <p className="p-4 text-sm text-red-600">Error cargando mensajes.</p>}

        {items.data && (
          <>
            <table className="min-w-full divide-y divide-gray-200 text-sm">
              <thead className="bg-gray-50">
                <tr>
                  <Th>Enviado</Th>
                  <Th>Canal</Th>
                  <Th>Tenant</Th>
                  <Th>Destinatario</Th>
                  <Th>Asunto / Preview</Th>
                  <Th>Estado</Th>
                  <Th>Acciones</Th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 bg-white">
                {items.data.items.map(it => (
                  <Row key={it.id} item={it} onView={() => setDetailId(it.id)} />
                ))}
                {items.data.items.length === 0 && (
                  <tr>
                    <td colSpan={7} className="px-4 py-8 text-center text-sm text-gray-500">
                      Sin mensajes para estos filtros
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

      {detailId && <DetailModal id={detailId} onClose={() => setDetailId(null)} />}
    </div>
  )
}

function Row({ item, onView }: { item: OutboundItem; onView: () => void }) {
  return (
    <tr>
      <Td className="font-mono text-xs whitespace-nowrap">{formatPanama(item.sentAt)}</Td>
      <Td><ChannelPill channel={item.channel} /></Td>
      <Td title={item.tenantId}>
        {item.tenantName ?? <span className="font-mono text-xs text-gray-500">{item.tenantId.slice(0, 8)}…</span>}
      </Td>
      <Td>
        <div className="flex flex-col">
          <span className="font-mono text-xs">{item.recipient}</span>
          {item.clientName && <span className="text-xs text-gray-500">{item.clientName}</span>}
        </div>
      </Td>
      <td className="max-w-md px-4 py-2 text-xs text-gray-700">
        {item.subject ? (
          <div className="flex flex-col">
            <span className="font-medium text-gray-900 truncate">{item.subject}</span>
            <span className="text-gray-500 truncate">{item.preview}</span>
          </div>
        ) : (
          <span className="text-gray-600 truncate block">{item.preview}</span>
        )}
      </td>
      <Td><StatusPill status={item.status} /></Td>
      <Td>
        <button
          onClick={onView}
          title="Ver contenido completo"
          className="inline-flex items-center gap-1 rounded-md bg-gray-100 px-2 py-1 text-xs font-medium text-gray-700 hover:bg-gray-200"
        >
          <Eye className="h-3 w-3" />
          Ver
        </button>
      </Td>
    </tr>
  )
}

function DetailModal({ id, onClose }: { id: string; onClose: () => void }) {
  const detail = useOutboundItemDetail(id)
  const isEmail = detail.data?.channel === 'Email'
  const isHtml = isEmail && /<\/?[a-z][\s\S]*>/i.test(detail.data?.content ?? '')

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onClick={onClose}>
      <div
        className="flex max-h-[90vh] w-full max-w-4xl flex-col overflow-hidden rounded-xl bg-white shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <header className="flex items-start justify-between border-b border-gray-200 px-6 py-4">
          <div>
            <h3 className="text-lg font-semibold text-gray-900">
              {detail.data?.subject ?? 'Mensaje enviado'}
            </h3>
            <p className="mt-1 text-xs text-gray-500">
              {detail.data && (
                <>
                  Para <span className="font-mono">{detail.data.recipient ?? detail.data.clientPhone}</span>
                  {' • '}
                  {detail.data.tenantName} • <ChannelPill channel={detail.data.channel} />
                  {' • '}<StatusPill status={detail.data.status} />
                  {' • '}{formatPanama(detail.data.sentAt)}
                </>
              )}
            </p>
          </div>
          <button onClick={onClose} className="rounded-md p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-700">
            <X className="h-5 w-5" />
          </button>
        </header>

        <div className="flex-1 overflow-auto bg-gray-50 p-4">
          {detail.isLoading && <p className="text-sm text-gray-500">Cargando…</p>}
          {detail.data && (
            isHtml ? (
              <iframe
                title="Contenido del correo"
                srcDoc={detail.data.content}
                sandbox=""
                className="h-[60vh] w-full rounded-md border border-gray-200 bg-white"
              />
            ) : (
              <pre className="whitespace-pre-wrap break-words rounded-md border border-gray-200 bg-white p-4 text-sm text-gray-800">
                {detail.data.content}
              </pre>
            )
          )}
        </div>
      </div>
    </div>
  )
}

function ChannelPill({ channel }: { channel: string }) {
  const palette: Record<string, { cls: string; Icon: React.ComponentType<{ className?: string }> }> = {
    Email:    { cls: 'bg-blue-50 text-blue-700',       Icon: Mail },
    WhatsApp: { cls: 'bg-green-50 text-green-700',     Icon: MessageSquare },
    Sms:      { cls: 'bg-amber-50 text-amber-700',     Icon: Smartphone },
  }
  const cfg = palette[channel] ?? { cls: 'bg-gray-50 text-gray-700', Icon: Send }
  const Icon = cfg.Icon
  return (
    <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${cfg.cls}`}>
      <Icon className="h-3 w-3" />
      {channel}
    </span>
  )
}

function StatusPill({ status }: { status: string }) {
  const palette: Record<string, string> = {
    Sent:      'bg-emerald-50 text-emerald-700',
    Delivered: 'bg-emerald-100 text-emerald-800',
    Read:      'bg-emerald-200 text-emerald-900',
    Failed:    'bg-red-50 text-red-700',
  }
  const cls = palette[status] ?? 'bg-gray-50 text-gray-700'
  const Icon = status === 'Failed' ? XCircle : CheckCircle2
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
    <div className={`rounded-lg border bg-white p-4 ${emphasized ? 'border-red-300 shadow-sm' : 'border-gray-200'}`}>
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-gray-600">{label}</span>
        {icon}
      </div>
      <p className={`mt-2 text-3xl font-bold ${emphasized ? 'text-red-700' : 'text-gray-900'}`}>{value}</p>
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
