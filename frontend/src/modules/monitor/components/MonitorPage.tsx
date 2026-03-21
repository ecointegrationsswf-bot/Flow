import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { MessageSquare } from 'lucide-react'
import { useConversationHub } from '@/shared/hooks/useConversationHub'
import { useConversations } from '@/shared/hooks/useMonitor'
import { useAuthStore } from '@/shared/stores/authStore'
import { LoadingSpinner } from '@/shared/components/LoadingSpinner'
import { ConversationList } from './ConversationList'
import { ConversationDetailPanel } from './ConversationDetailPanel'
import type { ConversationSummary } from '@/shared/types'

// Datos de prueba
const mockConversations: ConversationSummary[] = [
  {
    id: 'mock-1',
    clientPhone: '+507 6234-5678',
    clientName: 'Maria Rodriguez',
    agentType: 'Cobros',
    agentName: 'Agente SURA Cobros',
    status: 'Active',
    channel: 'WhatsApp',
    isHumanHandled: false,
    lastActivityAt: new Date(Date.now() - 2 * 60000).toISOString(),
    lastMessagePreview: 'Hola, quiero consultar sobre mi poliza de auto',
  },
  {
    id: 'mock-2',
    clientPhone: '+507 6987-1234',
    clientName: 'Carlos Mendez',
    agentType: 'Cobros',
    agentName: 'Agente ASSA Cobros',
    status: 'WaitingClient',
    channel: 'WhatsApp',
    isHumanHandled: false,
    lastActivityAt: new Date(Date.now() - 15 * 60000).toISOString(),
    lastMessagePreview: 'Ok, voy a revisar y te confirmo el pago',
  },
  {
    id: 'mock-3',
    clientPhone: '+507 6555-9876',
    clientName: 'Ana Martinez',
    agentType: 'Reclamos',
    agentName: 'Agente Reclamos General',
    status: 'EscalatedToHuman',
    channel: 'WhatsApp',
    isHumanHandled: true,
    lastActivityAt: new Date(Date.now() - 45 * 60000).toISOString(),
    lastMessagePreview: 'Necesito hablar con un supervisor por favor',
  },
  {
    id: 'mock-4',
    clientPhone: '+507 6111-2222',
    clientName: 'Roberto Gonzalez',
    agentType: 'Renovaciones',
    agentName: 'Agente Renovaciones ASSA',
    status: 'Active',
    channel: 'WhatsApp',
    isHumanHandled: false,
    lastActivityAt: new Date(Date.now() - 3 * 3600000).toISOString(),
    lastMessagePreview: 'Quiero renovar mi poliza de vida',
  },
  {
    id: 'mock-5',
    clientPhone: '+507 6333-4444',
    clientName: 'Laura Perez',
    agentType: 'Cobros',
    agentName: 'Agente SURA Cobros',
    status: 'Active',
    channel: 'WhatsApp',
    isHumanHandled: false,
    lastActivityAt: new Date(Date.now() - 5 * 3600000).toISOString(),
    lastMessagePreview: 'Ya realice la transferencia esta manana',
  },
  {
    id: 'mock-6',
    clientPhone: '+507 6777-8888',
    clientName: 'Pedro Castillo',
    agentType: 'Cobros',
    agentName: 'Agente Mapfre Cobros',
    status: 'WaitingClient',
    channel: 'WhatsApp',
    isHumanHandled: false,
    lastActivityAt: new Date(Date.now() - 24 * 3600000).toISOString(),
    lastMessagePreview: 'Perfecto, lo reviso y le confirmo',
  },
  {
    id: 'mock-7',
    clientPhone: '+507 6444-5555',
    clientName: 'Sofia Hernandez',
    agentType: 'General',
    agentName: 'Asistente General',
    status: 'Active',
    channel: 'WhatsApp',
    isHumanHandled: false,
    lastActivityAt: new Date(Date.now() - 30 * 60000).toISOString(),
    lastMessagePreview: 'Gracias por la informacion!',
  },
]

export function MonitorPage() {
  const [selected, setSelected] = useState<string | null>(null)
  const tenantId = useAuthStore((s) => s.tenantId) ?? ''
  const queryClient = useQueryClient()

  const { data: apiConversations = [], isLoading } = useConversations()

  useConversationHub(
    tenantId,
    () => queryClient.invalidateQueries({ queryKey: ['conversations'] }),
    () => queryClient.invalidateQueries({ queryKey: ['conversations'] }),
  )

  // Usar datos de prueba si no hay conversaciones reales
  const conversations = apiConversations.length > 0 ? apiConversations : mockConversations

  if (isLoading) return <LoadingSpinner />

  return (
    <div className="-m-6 flex h-[calc(100vh-0px)] overflow-hidden bg-white">
      <ConversationList
        conversations={conversations}
        selectedId={selected}
        onSelect={setSelected}
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
