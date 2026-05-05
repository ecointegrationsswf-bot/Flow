import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { MessageSquare } from 'lucide-react'
import { useConversationHub } from '@/shared/hooks/useConversationHub'
import { useConversations } from '@/shared/hooks/useMonitor'
import { useAuthStore } from '@/shared/stores/authStore'
import { LoadingSpinner } from '@/shared/components/LoadingSpinner'
import { ConversationList } from './ConversationList'
import { ConversationDetailPanel } from './ConversationDetailPanel'

// Filtros por fecha civil (YYYY-MM-DD). El backend interpreta el rango en
// la zona horaria del tenant — por eso NO mandamos ISO con hora local del
// navegador (eso provocaba desfasajes cuando el navegador tenía un TZ
// distinto al del tenant).
function todayLocalDate(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

export function MonitorPage() {
  const [selected, setSelected] = useState<string | null>(null)
  const tenantId = useAuthStore((s) => s.tenantId) ?? ''
  const queryClient = useQueryClient()

  // Filtros — por defecto: del día actual.
  const today = todayLocalDate()
  const [fromDate, setFromDate] = useState<string>(today)
  const [toDate, setToDate]     = useState<string>(today)
  const [launchedByUserId, setLaunchedByUserId] = useState<string>('')

  const { data: conversations = [], isLoading } = useConversations({
    fromIso: fromDate || null,    // YYYY-MM-DD; el backend lo interpreta en TZ del tenant
    toIso:   toDate   || null,
    launchedByUserId: launchedByUserId || null,
  })

  useConversationHub(
    tenantId,
    (msg) => {
      queryClient.invalidateQueries({ queryKey: ['conversations'] })
      queryClient.invalidateQueries({ queryKey: ['conversations', msg.conversationId] })
    },
    (convId) => {
      queryClient.invalidateQueries({ queryKey: ['conversations'] })
      queryClient.invalidateQueries({ queryKey: ['conversations', convId] })
    },
  )

  if (isLoading) return <LoadingSpinner />

  return (
    <div className="-m-6 flex h-[calc(100vh-0px)] overflow-hidden bg-white">
      <ConversationList
        conversations={conversations}
        selectedId={selected}
        onSelect={setSelected}
        fromDate={fromDate}
        toDate={toDate}
        onFromDateChange={setFromDate}
        onToDateChange={setToDate}
        launchedByUserId={launchedByUserId}
        onLaunchedByUserIdChange={setLaunchedByUserId}
      />

      <main className="flex flex-1 flex-col bg-[#f0f2f5]">
        {selected ? (
          <ConversationDetailPanel conversationId={selected} />
        ) : (
          <div className="flex flex-1 flex-col items-center justify-center gap-4">
            <div className="rounded-full bg-[#e3f2fd] p-8">
              <MessageSquare className="h-16 w-16 text-[#5b7fdb]" />
            </div>
            <h2 className="text-2xl font-light text-gray-500">TalkIA Monitor</h2>
            <p className="text-sm text-gray-400">Selecciona una conversacion para ver el historial</p>
          </div>
        )}
      </main>
    </div>
  )
}

export default MonitorPage
