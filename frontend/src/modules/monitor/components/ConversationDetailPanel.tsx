import { useState, useEffect, useRef } from 'react'
import { UserCheck, PauseCircle, PlayCircle, Send } from 'lucide-react'
import { Badge } from '@/shared/components/Badge'
import { StatusBadge } from '@/shared/components/StatusBadge'
import { LoadingSpinner } from '@/shared/components/LoadingSpinner'
import { MessageBubble } from './MessageBubble'
import { useConversationDetail, useTakeConversation, useSendReply } from '@/shared/hooks/useMonitor'

interface ConversationDetailPanelProps {
  conversationId: string
}

export function ConversationDetailPanel({ conversationId }: ConversationDetailPanelProps) {
  const [reply, setReply] = useState('')
  const messagesEndRef = useRef<HTMLDivElement>(null)

  const { data: conversation, isLoading } = useConversationDetail(conversationId)
  const takeMutation = useTakeConversation()
  const replyMutation = useSendReply()

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [conversation?.messages])

  const handleSend = () => {
    if (!reply.trim()) return
    replyMutation.mutate({ id: conversationId, message: reply })
    setReply('')
  }

  if (isLoading) return <LoadingSpinner />

  if (!conversation) {
    return (
      <div className="flex flex-1 items-center justify-center text-sm text-gray-400">
        No se pudo cargar la conversacion
      </div>
    )
  }

  return (
    <div className="flex h-full flex-col">
      {/* Header */}
      <div className="flex items-center justify-between border-b border-gray-200 bg-white px-5 py-3">
        <div className="flex items-center gap-3">
          <div>
            <p className="text-sm font-medium text-gray-900">
              {conversation.clientName ?? conversation.clientPhone}
            </p>
            <div className="flex items-center gap-2 text-xs text-gray-500">
              <span>{conversation.clientPhone}</span>
              {conversation.policyNumber && <span>| Poliza: {conversation.policyNumber}</span>}
            </div>
          </div>
          {conversation.activeAgentId && (
            <Badge variant={conversation.status === 'EscalatedToHuman' ? 'humano' : 'General'}>
              {conversation.channel}
            </Badge>
          )}
          <StatusBadge status={conversation.status} />
        </div>

        <div className="flex items-center gap-2">
          {!conversation.isHumanHandled && (
            <button
              onClick={() => takeMutation.mutate(conversationId)}
              disabled={takeMutation.isPending}
              className="flex items-center gap-1.5 rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50"
            >
              <UserCheck className="h-3.5 w-3.5" />
              Tomar conversacion
            </button>
          )}
          {conversation.isHumanHandled ? (
            <button className="flex items-center gap-1.5 rounded-md border border-green-300 bg-green-50 px-3 py-1.5 text-xs font-medium text-green-700 hover:bg-green-100">
              <PlayCircle className="h-3.5 w-3.5" />
              Reactivar IA
            </button>
          ) : (
            <button className="flex items-center gap-1.5 rounded-md border border-amber-300 bg-amber-50 px-3 py-1.5 text-xs font-medium text-amber-700 hover:bg-amber-100">
              <PauseCircle className="h-3.5 w-3.5" />
              Pausar IA
            </button>
          )}
        </div>
      </div>

      {/* Messages */}
      <div className="flex-1 space-y-3 overflow-y-auto p-5">
        {conversation.messages.length > 0 ? (
          conversation.messages.map((msg) => (
            <MessageBubble key={msg.id} message={msg} />
          ))
        ) : (
          <p className="text-center text-xs text-gray-400">Sin mensajes aun</p>
        )}
        <div ref={messagesEndRef} />
      </div>

      {/* Reply */}
      <div className="flex gap-2 border-t border-gray-200 bg-white p-4">
        <input
          value={reply}
          onChange={(e) => setReply(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && !e.shiftKey && handleSend()}
          placeholder="Escribe para intervenir..."
          className="flex-1 rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
        />
        <button
          onClick={handleSend}
          disabled={!reply.trim() || replyMutation.isPending}
          className="flex items-center gap-1.5 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
        >
          <Send className="h-4 w-4" />
          Enviar
        </button>
      </div>
    </div>
  )
}
