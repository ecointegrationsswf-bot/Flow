import { useMemo, useState } from 'react'
import { Search, CalendarRange, X } from 'lucide-react'
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
                    <p className="truncate text-xs text-gray-500">
                      {c.lastMessagePreview ?? '—'}
                    </p>
                    <div className="flex shrink-0 items-center gap-1">
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
