import { useMemo, useState } from 'react'
import { Search, CalendarRange, X, MessageSquare, Mail, Smartphone, CheckCheck, Check, Eye, Reply, XOctagon, HelpCircle } from 'lucide-react'
import { differenceInMinutes } from 'date-fns'
import type { ConversationSummary, ConversationStatus } from '@/shared/types'
import { useTenantTime } from '@/shared/hooks/useTenantTime'
import { useCampaignLaunchers } from '@/shared/hooks/useMonitor'

interface ConversationListProps {
  conversations: ConversationSummary[]
  selectedId: string | null
  onSelect: (id: string) => void
  fromDate: string
  toDate: string
  onFromDateChange: (v: string) => void
  onToDateChange: (v: string) => void
  launchedByUserId: string
  onLaunchedByUserIdChange: (v: string) => void
}


function getInitials(name: string) {
  return name.split(' ').map(w => w[0]).filter(Boolean).slice(0, 2).join('').toUpperCase()
}

function getAvatarBg(name: string) {
  const colors = [
    'bg-blue-500', 'bg-emerald-500', 'bg-purple-500', 'bg-orange-500',
    'bg-pink-500', 'bg-teal-500', 'bg-indigo-500', 'bg-rose-500',
  ]
  let hash = 0
  for (let i = 0; i < name.length; i++) hash = name.charCodeAt(i) + ((hash << 5) - hash)
  return colors[Math.abs(hash) % colors.length]
}

export function ConversationList({
  conversations, selectedId, onSelect,
  fromDate, toDate, onFromDateChange, onToDateChange,
  launchedByUserId, onLaunchedByUserIdChange,
}: ConversationListProps) {
  const tt = useTenantTime()
  const { data: launchers = [] } = useCampaignLaunchers()
  const formatTime = (dateStr: string) => {
    if (tt.isToday(dateStr))     return tt.time(dateStr)
    if (tt.isYesterday(dateStr)) return 'Ayer'
    return tt.date(dateStr)
  }
  const [search, setSearch] = useState('')
  const [filterAgent, setFilterAgent] = useState('')
  const [filterStatus, setFilterStatus] = useState<ConversationStatus | ''>('')
  // Filtro por canal — útil cuando el tenant maneja campañas WhatsApp + Email
  // simultáneamente. 'all' muestra todo (default).
  const [filterChannel, setFilterChannel] = useState<'all' | 'WhatsApp' | 'Email' | 'Sms'>('all')
  // Filtro por delivery status (Phase 2 — webhook message_ack):
  //   all         → sin filtro (default)
  //   read        → conversaciones donde el último saliente fue leído
  //   unread      → entregado pero no leído (al menos un mensaje en ese estado)
  //   responded   → cliente respondió DESPUÉS del último saliente
  //   undelivered → al menos un saliente NO se entregó (queue/invalid/...)
  type DeliveryFilter = 'all' | 'read' | 'unread' | 'sent' | 'responded' | 'undelivered' | 'notracking'
  const [filterDelivery, setFilterDelivery] = useState<DeliveryFilter>('all')

  const agentNames = useMemo(() => {
    const names = new Set<string>()
    conversations.forEach((c) => {
      const name = c.agentName || c.agentType
      if (name) names.add(name)
    })
    return Array.from(names).sort()
  }, [conversations])

  // Deduplicar: conservar solo la conversación más reciente por número de teléfono
  const deduplicated = useMemo(() => {
    const byPhone = new Map<string, typeof conversations[0]>()
    // Las conversaciones ya vienen ordenadas por lastActivityAt desc desde el backend
    for (const c of conversations) {
      if (!byPhone.has(c.clientPhone)) {
        byPhone.set(c.clientPhone, c)
      }
    }
    return Array.from(byPhone.values())
  }, [conversations])

  // Conteos por canal — semántica distinta según el canal:
  //   • WhatsApp/SMS: cuentan CONVERSACIONES únicas (un diálogo persistente por cliente).
  //   • Email: cuenta CORREOS ENVIADOS en el rango de fechas del filtro, no
  //     conversaciones únicas. Esto refleja "envíos por día" — si mañana
  //     reenvías a los mismos 2 contactos, el badge sumará 2 más, no se queda en 2.
  //     Para esto sumamos outboundEmailCount sobre TODAS las conversaciones del
  //     rango (no las dedupeadas), porque ese contador ya viene calculado del
  //     backend con la misma ventana de fecha civil PA que el filtro principal.
  const channelCounts = useMemo(() => {
    const counts = { WhatsApp: 0, Email: 0, Sms: 0, all: deduplicated.length }
    for (const c of deduplicated) {
      if (c.channel === 'WhatsApp') counts.WhatsApp++
      else if (c.channel === 'Sms') counts.Sms++
    }
    // Email: suma envíos en el rango sobre la lista NO deduplicada.
    for (const c of conversations) {
      if (c.channel === 'Email') counts.Email += c.outboundEmailCount ?? 0
    }
    return counts
  }, [deduplicated, conversations])

  // Contadores por estado de delivery — se calculan sobre la lista FILTRADA
  // por canal (porque los iconos ✓✓ aplican al WhatsApp principalmente; en
  // Email tendrías que mirar bounces). Para mantener simpleza, los conteos
  // se basan en deduplicated dentro del canal seleccionado.
  const deliveryCounts = useMemo(() => {
    const base = filterChannel === 'all'
      ? deduplicated
      : deduplicated.filter(c => c.channel === filterChannel)
    return {
      all:         base.length,
      read:        base.filter(c => c.lastOutboundRead).length,
      unread:      base.filter(c => c.hasUnreadOutbound && !c.lastOutboundRead).length,
      sent:        base.filter(c => c.lastOutboundSent).length,
      responded:   base.filter(c => c.clientResponded).length,
      undelivered: base.filter(c => c.hasUndelivered).length,
      notracking:  base.filter(c => c.lastOutboundNoTracking).length,
    }
  }, [deduplicated, filterChannel])

  const filtered = deduplicated.filter((c) => {
    if (search) {
      const q = search.toLowerCase()
      const matchName = c.clientName?.toLowerCase().includes(q)
      const matchPhone = c.clientPhone.includes(q)
      if (!matchName && !matchPhone) return false
    }
    if (filterAgent) {
      const agentLabel = c.agentName || c.agentType
      if (agentLabel !== filterAgent) return false
    }
    if (filterStatus && c.status !== filterStatus) return false
    if (filterChannel !== 'all' && c.channel !== filterChannel) return false
    // Filtro por delivery status — ortogonal al canal/status/agente.
    if (filterDelivery === 'read'        && !c.lastOutboundRead)                          return false
    if (filterDelivery === 'unread'      && (!c.hasUnreadOutbound || c.lastOutboundRead)) return false
    if (filterDelivery === 'sent'        && !c.lastOutboundSent)                          return false
    if (filterDelivery === 'responded'   && !c.clientResponded)                           return false
    if (filterDelivery === 'undelivered' && !c.hasUndelivered)                            return false
    if (filterDelivery === 'notracking'  && !c.lastOutboundNoTracking)                    return false
    return true
  })

  return (
    <aside className="flex w-[380px] flex-col border-r border-gray-200 bg-white">
      {/* Search */}
      <div className="px-4 pt-4 pb-2">
        <div className="relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search"
            className="w-full rounded-full border border-gray-200 bg-[#f6f6f6] py-2.5 pl-10 pr-4 text-sm text-gray-700 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-400/50 focus:border-blue-400"
          />
        </div>
      </div>

      {/* Date range filter */}
      <div className="flex items-center gap-2 px-4 pb-2 text-xs text-gray-600">
        <CalendarRange className="h-3.5 w-3.5 shrink-0 text-gray-400" />
        <input
          type="date" value={fromDate}
          onChange={e => onFromDateChange(e.target.value)}
          className="flex-1 rounded-md border border-gray-200 bg-[#f6f6f6] px-2 py-1 focus:outline-none focus:ring-1 focus:ring-blue-400/50"
        />
        <span className="text-gray-400">→</span>
        <input
          type="date" value={toDate}
          onChange={e => onToDateChange(e.target.value)}
          className="flex-1 rounded-md border border-gray-200 bg-[#f6f6f6] px-2 py-1 focus:outline-none focus:ring-1 focus:ring-blue-400/50"
        />
        {(fromDate || toDate) && (
          <button
            onClick={() => { onFromDateChange(''); onToDateChange('') }}
            title="Limpiar fechas"
            className="rounded p-0.5 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
          >
            <X className="h-3.5 w-3.5" />
          </button>
        )}
      </div>

      {/* Launched-by-user filter */}
      <div className="px-4 pb-2">
        <select
          value={launchedByUserId}
          onChange={e => onLaunchedByUserIdChange(e.target.value)}
          className="w-full rounded-lg border border-gray-200 bg-[#f6f6f6] px-3 py-1.5 text-xs text-gray-600 focus:outline-none focus:ring-2 focus:ring-blue-400/50"
          title="Filtra por el usuario que lanzó la campaña. Las conversaciones sin campaña son visibles para todos."
        >
          <option value="">Todas las campañas (cualquier usuario)</option>
          {launchers.map(l => (
            <option key={l.key} value={l.key}>{l.label}</option>
          ))}
        </select>
      </div>

      {/* Channel filter tabs — el tenant puede tener campañas WhatsApp + Email
          simultáneamente. Esto le permite ver solo lo que le interesa sin
          confundir vistas de un canal con el otro. */}
      <div className="flex gap-1 px-4 pb-2">
        <ChannelTab
          label="Todos"
          count={channelCounts.all}
          active={filterChannel === 'all'}
          onClick={() => setFilterChannel('all')}
        />
        <ChannelTab
          label="WhatsApp"
          icon={<MessageSquare className="h-3 w-3" />}
          count={channelCounts.WhatsApp}
          active={filterChannel === 'WhatsApp'}
          onClick={() => setFilterChannel('WhatsApp')}
        />
        <ChannelTab
          label="Email"
          icon={<Mail className="h-3 w-3" />}
          count={channelCounts.Email}
          active={filterChannel === 'Email'}
          onClick={() => setFilterChannel('Email')}
        />
        {channelCounts.Sms > 0 && (
          <ChannelTab
            label="SMS"
            icon={<Smartphone className="h-3 w-3" />}
            count={channelCounts.Sms}
            active={filterChannel === 'Sms'}
            onClick={() => setFilterChannel('Sms')}
          />
        )}
      </div>

      {/* Delivery status filter — Phase 2 (UltraMsg webhook message_ack).
          Permite al usuario ver de un vistazo:
          • cuántos morosos LEYERON el mensaje (verdadero indicador de impacto)
          • cuántos quedaron sin leer (recordatorio pendiente)
          • cuántos respondieron (lead caliente)
          • cuántos NO entregados (cuenta restringida o número inválido — recontactar por otro medio) */}
      <div className="flex flex-wrap gap-1 px-4 pb-2">
        <DeliveryTab
          label="Todos"
          count={deliveryCounts.all}
          active={filterDelivery === 'all'}
          onClick={() => setFilterDelivery('all')}
        />
        <DeliveryTab
          label="Leídos"
          icon={<CheckCheck className="h-3 w-3 text-sky-500" />}
          count={deliveryCounts.read}
          tone="sky"
          active={filterDelivery === 'read'}
          onClick={() => setFilterDelivery('read')}
        />
        <DeliveryTab
          label="No leídos"
          icon={<Eye className="h-3 w-3 text-gray-500" />}
          count={deliveryCounts.unread}
          tone="gray"
          active={filterDelivery === 'unread'}
          onClick={() => setFilterDelivery('unread')}
        />
        <DeliveryTab
          label="Enviado"
          icon={<Check className="h-3 w-3 text-blue-500" />}
          count={deliveryCounts.sent}
          tone="blue"
          active={filterDelivery === 'sent'}
          onClick={() => setFilterDelivery('sent')}
        />
        <DeliveryTab
          label="Respondió"
          icon={<Reply className="h-3 w-3 text-emerald-500" />}
          count={deliveryCounts.responded}
          tone="emerald"
          active={filterDelivery === 'responded'}
          onClick={() => setFilterDelivery('responded')}
        />
        <DeliveryTab
          label="No entregado"
          icon={<XOctagon className="h-3 w-3 text-red-500" />}
          count={deliveryCounts.undelivered}
          tone="red"
          active={filterDelivery === 'undelivered'}
          onClick={() => setFilterDelivery('undelivered')}
        />
        {deliveryCounts.notracking > 0 && (
          <DeliveryTab
            label="Sin info"
            icon={<HelpCircle className="h-3 w-3 text-gray-400" />}
            count={deliveryCounts.notracking}
            tone="gray"
            active={filterDelivery === 'notracking'}
            onClick={() => setFilterDelivery('notracking')}
          />
        )}
      </div>

      {/* Filters */}
      <div className="flex gap-2 px-4 pb-3">
        <select
          value={filterAgent}
          onChange={(e) => setFilterAgent(e.target.value)}
          className="flex-1 rounded-lg border border-gray-200 bg-[#f6f6f6] px-3 py-1.5 text-xs text-gray-600 focus:outline-none focus:ring-2 focus:ring-blue-400/50"
        >
          <option value="">Todos</option>
          {agentNames.map((name) => (
            <option key={name} value={name}>{name}</option>
          ))}
        </select>
        <select
          value={filterStatus}
          onChange={(e) => setFilterStatus(e.target.value as ConversationStatus | '')}
          className="flex-1 rounded-lg border border-gray-200 bg-[#f6f6f6] px-3 py-1.5 text-xs text-gray-600 focus:outline-none focus:ring-2 focus:ring-blue-400/50"
        >
          <option value="">Todo estado</option>
          <option value="Active">🟢 Activa</option>
          <option value="WaitingClient">🕐 Esperando cliente</option>
          <option value="EscalatedToHuman">🔴 Escalada a humano</option>
          <option value="Unresponsive">😶 Sin respuesta</option>
          <option value="Closed">⚫ Cerrada</option>
        </select>
      </div>

      {/* Conversation list */}
      <div className="flex-1 overflow-y-auto">
        {filtered.length === 0 && (
          <div className="py-16 text-center text-sm text-gray-400">No hay conversaciones</div>
        )}
        {filtered.map((c) => {
          const displayName = c.clientName ?? c.clientPhone
          const isSelected = selectedId === c.id
          const minutesSince = differenceInMinutes(new Date(), new Date(c.lastActivityAt))
          const isStale = minutesSince > 8 && c.status === 'WaitingClient'
          // Para canal Email mostramos el asunto del último correo saliente —
          // el lastMessagePreview suele ser HTML truncado (poco legible). El
          // backend ya nos manda el Subject limpio en lastEmailSubject.
          const isEmail = c.channel === 'Email'
          const previewText = isEmail
            ? (c.lastEmailSubject || c.lastMessagePreview || '—')
            : (c.lastMessagePreview || '—')
          return (
            <button
              key={c.id}
              onClick={() => onSelect(c.id)}
              className={`w-full px-4 py-3 text-left transition-colors hover:bg-[#f5f6f8] border-b border-gray-100 ${
                isSelected ? 'bg-[#e8ecf7] border-l-4 border-l-blue-500' : ''
              }`}
            >
              <div className="flex items-center gap-3">
                {/* Avatar - circular with initials */}
                <div className="relative shrink-0">
                  <div className={`flex h-12 w-12 items-center justify-center rounded-full text-sm font-bold text-white ${getAvatarBg(displayName)}`}>
                    {getInitials(displayName)}
                  </div>
                  {c.status === 'Active' && (
                    <span className="absolute bottom-0 right-0 h-3 w-3 rounded-full border-2 border-white bg-green-400" />
                  )}
                  {c.status === 'EscalatedToHuman' && (
                    <span className="absolute bottom-0 right-0 h-3 w-3 rounded-full border-2 border-white bg-orange-400" title="Escalado a humano" />
                  )}
                  {isStale && (
                    <span className="absolute bottom-0 right-0 h-3 w-3 rounded-full border-2 border-white bg-red-500" title="Sin respuesta del cliente" />
                  )}
                </div>

                <div className="min-w-0 flex-1">
                  <div className="flex items-center justify-between gap-2">
                    <p className={`truncate text-sm ${isSelected ? 'font-semibold text-gray-900' : 'font-medium text-gray-800'}`}>
                      {displayName}
                    </p>
                    <span className="shrink-0 text-[11px] text-gray-400">
                      {formatTime(c.lastActivityAt)}
                    </span>
                  </div>
                  <div className="mt-0.5 flex items-center justify-between gap-2">
                    <p className="truncate text-xs text-gray-500 flex items-center gap-1">
                      {isEmail && <Mail className="h-3 w-3 shrink-0 text-blue-500" />}
                      <span className="truncate">{previewText}</span>
                    </p>
                    <div className="flex shrink-0 items-center gap-1">
                      {isEmail && (c.outboundEmailCount ?? 0) > 0 && (
                        <span
                          className="rounded-full bg-blue-100 px-1.5 py-0.5 text-[10px] font-medium text-blue-700"
                          title={`${c.outboundEmailCount} correo(s) enviado(s) en el rango`}
                        >
                          {c.outboundEmailCount}
                        </span>
                      )}
                      {c.isHumanHandled && (
                        <span className="rounded-full bg-green-100 px-1.5 py-0.5 text-[10px] font-medium text-green-700">H</span>
                      )}
                    </div>
                  </div>
                </div>
              </div>
            </button>
          )
        })}
      </div>
    </aside>
  )
}

/** Tab pill para el filtro por canal del Monitor. */
function ChannelTab({
  label, icon, count, active, onClick,
}: {
  label: string
  icon?: React.ReactNode
  count: number
  active: boolean
  onClick: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`inline-flex items-center gap-1 rounded-full px-3 py-1 text-[11px] font-medium transition-colors ${
        active
          ? 'bg-blue-600 text-white shadow-sm'
          : 'bg-[#f6f6f6] text-gray-600 hover:bg-gray-200'
      }`}
    >
      {icon}
      <span>{label}</span>
      <span
        className={`ml-0.5 rounded-full px-1.5 text-[10px] font-semibold ${
          active ? 'bg-white/25 text-white' : 'bg-white text-gray-500'
        }`}
      >
        {count}
      </span>
    </button>
  )
}

/**
 * Tab pill para los filtros de delivery status (Leídos / No leídos /
 * Respondió / No entregado). Igual API que ChannelTab, pero con paleta
 * por tono — el botón activo cambia de color según la categoría para
 * que el usuario sepa al instante qué filtro está mirando.
 */
function DeliveryTab({
  label, icon, count, active, onClick, tone = 'blue',
}: {
  label: string
  icon?: React.ReactNode
  count: number
  active: boolean
  onClick: () => void
  tone?: 'blue' | 'sky' | 'gray' | 'emerald' | 'red'
}) {
  const activeBg: Record<string, string> = {
    blue:    'bg-blue-600 text-white',
    sky:     'bg-sky-500 text-white',
    gray:    'bg-gray-600 text-white',
    emerald: 'bg-emerald-600 text-white',
    red:     'bg-red-600 text-white',
  }
  return (
    <button
      type="button"
      onClick={onClick}
      className={`inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-[11px] font-medium transition-colors ${
        active
          ? `${activeBg[tone]} shadow-sm`
          : 'bg-[#f6f6f6] text-gray-600 hover:bg-gray-200'
      }`}
    >
      {icon}
      <span>{label}</span>
      <span
        className={`ml-0.5 rounded-full px-1.5 text-[10px] font-semibold ${
          active ? 'bg-white/25 text-white' : 'bg-white text-gray-500'
        }`}
      >
        {count}
      </span>
    </button>
  )
}
