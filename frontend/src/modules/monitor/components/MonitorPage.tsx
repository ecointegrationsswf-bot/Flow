import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { MessageSquare } from 'lucide-react'
import { useConversationHub } from '@/shared/hooks/useConversationHub'
import { useConversations } from '@/shared/hooks/useMonitor'
import { useAuthStore } from '@/shared/stores/authStore'
import { EmptyState } from '@/shared/components/EmptyState'
import { LoadingSpinner } from '@/shared/components/LoadingSpinner'
import { ConversationList } from './ConversationList'
import { ConversationDetailPanel } from './ConversationDetailPanel'

export function MonitorPage() {
  const [selected, setSelected] = useState<string | null>(null)
  const tenantId = useAuthStore((s) => s.tenantId) ?? ''
  const queryClient = useQueryClient()

  const { data: conversations = [], isLoading } = useConversations()

  useConversationHub(
    tenantId,
    () => queryClient.invalidateQueries({ queryKey: ['conversations'] }),
    () => queryClient.invalidateQueries({ queryKey: ['conversations'] }),
  )

  if (isLoading) return <LoadingSpinner />

  return (
    <div className="-m-6 flex h-[calc(100vh)] bg-gray-50">
      <ConversationList
        conversations={conversations}
        selectedId={selected}
        onSelect={setSelected}
      />

      <main className="flex flex-1 flex-col">
        {selected ? (
          <ConversationDetailPanel conversationId={selected} />
        ) : (
          <EmptyState
            icon={MessageSquare}
            title="Selecciona una conversacion"
            description="Elige una conversacion de la lista para ver el historial"
          />
        )}
      </main>
    </div>
  )
}

export default MonitorPage
