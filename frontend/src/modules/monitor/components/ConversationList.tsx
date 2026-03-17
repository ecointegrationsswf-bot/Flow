import { useState } from 'react'
import { Search } from 'lucide-react'
import { formatDistanceToNow, differenceInMinutes } from 'date-fns'
import { es } from 'date-fns/locale'
import { Badge } from '@/shared/components/Badge'
import type { ConversationSummary, AgentType, ConversationStatus } from '@/shared/types'

interface ConversationListProps {
  conversations: ConversationSummary[]
  selectedId: string | null
  onSelect: (id: string) => void
}

export function ConversationList({ conversations, selectedId, onSelect }: ConversationListProps) {
  const [search, setSearch] = useState('')
  const [filterAgent, setFilterAgent] = useState<AgentType | ''>('')
  const [filterStatus, setFilterStatus] = useState<ConversationStatus | ''>('')

  const filtered = conversations.filter((c) => {
    if (search) {
      const q = search.toLowerCase()
      const matchName = c.clientName?.toLowerCase().includes(q)
      const matchPhone = c.clientPhone.includes(q)
      if (!matchName && !matchPhone) return false
    }
    if (filterAgent && c.agentType !== filterAgent) return false
    if (filterStatus && c.status !== filterStatus) return false
    return true
  })

  return (
    <aside className="flex w-80 flex-col border-r border-gray-200 bg-white">
      {/* Header */}
      <div className="border-b border-gray-200 px-4 py-3">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-medium text-gray-900">Conversaciones</h2>
          <span className="rounded-full bg-red-100 px-2 py-0.5 text-xs font-medium text-red-700">
            {filtered.length}
          </span>
        </div>

        {/* Search */}
        <div className="relative mt-2">
          <Search className="absolute left-2.5 top-2 h-4 w-4 text-gray-400" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Buscar cliente..."
            className="w-full rounded-md border border-gray-300 py-1.5 pl-8 pr-3 text-xs focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
        </div>

        {/* Filters */}
        <div className="mt-2 flex gap-2">
          <select
            value={filterAgent}
            onChange={(e) => setFilterAgent(e.target.value as AgentType | '')}
            className="flex-1 rounded border border-gray-300 px-2 py-1 text-xs text-gray-600"
          >
            <option value="">Todos los agentes</option>
            <option value="Cobros">Cobros</option>
            <option value="Reclamos">Reclamos</option>
            <option value="Renovaciones">Renovaciones</option>
            <option value="General">General</option>
          </select>
          <select
            value={filterStatus}
            onChange={(e) => setFilterStatus(e.target.value as ConversationStatus | '')}
            className="flex-1 rounded border border-gray-300 px-2 py-1 text-xs text-gray-600"
          >
            <option value="">Todo estado</option>
            <option value="Active">Activa</option>
            <option value="WaitingClient">Esperando</option>
            <option value="EscalatedToHuman">Escalada</option>
          </select>
        </div>
      </div>

      {/* List */}
      <div className="flex-1 overflow-y-auto">
        {filtered.map((c) => {
          const minutesSince = differenceInMinutes(new Date(), new Date(c.lastActivityAt))
          const isStale = minutesSince > 8 && c.status === 'WaitingClient'

          return (
            <button
              key={c.id}
              onClick={() => onSelect(c.id)}
              className={`w-full border-b border-gray-100 px-4 py-3 text-left transition-colors hover:bg-gray-50 ${
                selectedId === c.id ? 'bg-blue-50' : ''
              }`}
            >
              <div className="flex items-start justify-between gap-2">
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-1.5">
                    <p className="truncate text-sm font-medium text-gray-900">
                      {c.clientName ?? c.clientPhone}
                    </p>
                    {isStale && <span className="h-2 w-2 shrink-0 rounded-full bg-red-500" />}
                  </div>
                  <p className="mt-0.5 truncate text-xs text-gray-500">
                    {c.lastMessagePreview ?? '—'}
                  </p>
                </div>
                <Badge variant={c.agentType}>{c.agentType}</Badge>
              </div>
              <div className="mt-1 flex items-center gap-2 text-xs text-gray-400">
                <span>{formatDistanceToNow(new Date(c.lastActivityAt), { addSuffix: true, locale: es })}</span>
                {c.isHumanHandled && <span className="font-medium text-green-600">Humano</span>}
              </div>
            </button>
          )
        })}
      </div>
    </aside>
  )
}
