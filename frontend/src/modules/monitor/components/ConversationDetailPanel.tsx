import { useState, useEffect, useRef } from 'react'
import { UserCheck, PauseCircle, PlayCircle, Send, Paperclip, Smile, X, Loader2, MoreVertical } from 'lucide-react'
import { LoadingSpinner } from '@/shared/components/LoadingSpinner'
import { MessageBubble } from './MessageBubble'
import { useConversationDetail, useTakeConversation, useSendReply, useSendFile } from '@/shared/hooks/useMonitor'
import type { Conversation, Message } from '@/shared/types'
import { format } from 'date-fns'

interface ConversationDetailPanelProps {
  conversationId: string
}

// Mock messages para datos de prueba
const mockMessages: Message[] = [
  {
    id: 'msg-1', conversationId: 'mock-1', direction: 'Inbound', status: 'Delivered',
    content: 'Hola, buenos dias. Quiero consultar sobre mi poliza de auto.',
    isFromAgent: false, sentAt: new Date(Date.now() - 30 * 60000).toISOString(),
  },
  {
    id: 'msg-2', conversationId: 'mock-1', direction: 'Outbound', status: 'Delivered',
    content: 'Hola Maria! Buenos dias. Con gusto te ayudo con tu consulta. Puedo ver que tienes una poliza de auto con SURA. ¿En que puedo ayudarte?',
    isFromAgent: true, agentName: 'Agente Cobros', sentAt: new Date(Date.now() - 28 * 60000).toISOString(),
  },
  {
    id: 'msg-3', conversationId: 'mock-1', direction: 'Inbound', status: 'Delivered',
    content: 'Quiero saber cuanto debo de mi prima. Me dijeron que tengo un saldo pendiente.',
    isFromAgent: false, sentAt: new Date(Date.now() - 25 * 60000).toISOString(),
  },
  {
    id: 'msg-4', conversationId: 'mock-1', direction: 'Outbound', status: 'Delivered',
    content: 'Claro, dejame verificar tu cuenta. Segun nuestros registros, tienes un saldo pendiente de $245.00 correspondiente a la cuota de marzo 2026. La fecha limite de pago es el 25 de marzo.',
    isFromAgent: true, agentName: 'Agente Cobros', sentAt: new Date(Date.now() - 23 * 60000).toISOString(),
  },
  {
    id: 'msg-5', conversationId: 'mock-1', direction: 'Inbound', status: 'Delivered',
    content: 'Ok, y como puedo hacer el pago?',
    isFromAgent: false, sentAt: new Date(Date.now() - 20 * 60000).toISOString(),
  },
  {
    id: 'msg-6', conversationId: 'mock-1', direction: 'Outbound', status: 'Delivered',
    content: 'Puedes realizar el pago por transferencia bancaria a la cuenta:\n\nBanco General\nCuenta: 04-12-34-567890\nNombre: Somos Seguros S.A.\n\nUna vez realices la transferencia, por favor enviame el comprobante para registrar tu pago.',
    isFromAgent: true, agentName: 'Agente Cobros', sentAt: new Date(Date.now() - 18 * 60000).toISOString(),
  },
  {
    id: 'msg-7', conversationId: 'mock-1', direction: 'Inbound', status: 'Delivered',
    content: 'Perfecto, voy a hacer la transferencia ahora mismo. Gracias!',
    isFromAgent: false, sentAt: new Date(Date.now() - 5 * 60000).toISOString(),
  },
  {
    id: 'msg-8', conversationId: 'mock-1', direction: 'Outbound', status: 'Delivered',
    content: 'Excelente Maria! Quedo atenta a tu comprobante. Que tengas un buen dia.',
    isFromAgent: true, agentName: 'Agente Cobros', sentAt: new Date(Date.now() - 2 * 60000).toISOString(),
  },
]

const baseMock: Conversation = {
  id: 'mock-1', tenantId: 'tenant-1', clientPhone: '+507 6234-5678', clientName: 'Maria Rodriguez',
  channel: 'WhatsApp', status: 'Active', isHumanHandled: false, gestionResult: 'Pending',
  startedAt: new Date(Date.now() - 35 * 60000).toISOString(), lastActivityAt: new Date(Date.now() - 2 * 60000).toISOString(),
  messages: mockMessages,
}

const mockConversationsData: Record<string, Conversation> = {
  'mock-1': baseMock,
  'mock-2': {
    ...baseMock, id: 'mock-2', clientPhone: '+507 6987-1234', clientName: 'Carlos Mendez', status: 'WaitingClient',
    messages: [
      { id: 'msg-c2-1', conversationId: 'mock-2', direction: 'Outbound', status: 'Delivered', content: 'Hola Carlos, le escribimos de Somos Seguros. Le informamos que tiene un saldo pendiente de $180.50 en su poliza de incendio #POL-2024-1234.', isFromAgent: true, agentName: 'Agente Cobros', sentAt: new Date(Date.now() - 2 * 3600000).toISOString() },
      { id: 'msg-c2-2', conversationId: 'mock-2', direction: 'Inbound', status: 'Delivered', content: 'Hola, si estoy al tanto. Puedo pagar la proxima semana?', isFromAgent: false, sentAt: new Date(Date.now() - 90 * 60000).toISOString() },
      { id: 'msg-c2-3', conversationId: 'mock-2', direction: 'Outbound', status: 'Delivered', content: 'Entiendo Carlos. Le comento que la fecha limite es el 22 de marzo. Si necesita una extension, podemos revisar las opciones. ¿Le gustaria que le ayude con un plan de pago?', isFromAgent: true, agentName: 'Agente Cobros', sentAt: new Date(Date.now() - 85 * 60000).toISOString() },
      { id: 'msg-c2-4', conversationId: 'mock-2', direction: 'Inbound', status: 'Delivered', content: 'Ok, voy a revisar y te confirmo el pago', isFromAgent: false, sentAt: new Date(Date.now() - 15 * 60000).toISOString() },
    ],
  },
  'mock-3': {
    ...baseMock, id: 'mock-3', clientPhone: '+507 6555-9876', clientName: 'Ana Martinez', status: 'EscalatedToHuman', isHumanHandled: true,
    messages: [
      { id: 'msg-c3-1', conversationId: 'mock-3', direction: 'Inbound', status: 'Delivered', content: 'Tuve un accidente y necesito reportar un reclamo urgente', isFromAgent: false, sentAt: new Date(Date.now() - 3 * 3600000).toISOString() },
      { id: 'msg-c3-2', conversationId: 'mock-3', direction: 'Outbound', status: 'Delivered', content: 'Lamento escuchar eso Ana. ¿Se encuentra usted bien? Voy a iniciar el proceso de reclamo. ¿Puede proporcionarme los detalles del incidente?', isFromAgent: true, agentName: 'Agente Reclamos', sentAt: new Date(Date.now() - 2.9 * 3600000).toISOString() },
      { id: 'msg-c3-3', conversationId: 'mock-3', direction: 'Inbound', status: 'Delivered', content: 'Necesito hablar con un supervisor por favor, esto es muy complicado', isFromAgent: false, sentAt: new Date(Date.now() - 45 * 60000).toISOString() },
      { id: 'msg-c3-4', conversationId: 'mock-3', direction: 'Outbound', status: 'Delivered', content: 'Entiendo Ana. Voy a transferir su caso a un supervisor que podra atenderla directamente. Un momento por favor.', isFromAgent: true, agentName: 'Agente Reclamos', sentAt: new Date(Date.now() - 44 * 60000).toISOString() },
    ],
  },
  'mock-4': {
    ...baseMock, id: 'mock-4', clientPhone: '+507 6111-2222', clientName: 'Roberto Gonzalez', status: 'Active',
    messages: [
      { id: 'msg-c4-1', conversationId: 'mock-4', direction: 'Inbound', status: 'Delivered', content: 'Quiero renovar mi poliza de vida que vence el proximo mes', isFromAgent: false, sentAt: new Date(Date.now() - 4 * 3600000).toISOString() },
      { id: 'msg-c4-2', conversationId: 'mock-4', direction: 'Outbound', status: 'Delivered', content: 'Hola Roberto! Claro, veo que su poliza de vida #VID-2023-5678 con ASSA vence el 15 de abril de 2026. Le puedo ofrecer la renovacion con las mismas condiciones o si desea podemos revisar opciones de cobertura.', isFromAgent: true, agentName: 'Agente Renovaciones', sentAt: new Date(Date.now() - 3.5 * 3600000).toISOString() },
    ],
  },
  'mock-5': {
    ...baseMock, id: 'mock-5', clientPhone: '+507 6333-4444', clientName: 'Laura Perez', status: 'Active',
    messages: [
      { id: 'msg-c5-1', conversationId: 'mock-5', direction: 'Outbound', status: 'Delivered', content: 'Hola Laura, le escribimos de Somos Seguros para recordarle que tiene un saldo de $320.00 correspondiente a su poliza de auto.', isFromAgent: true, agentName: 'Agente Cobros', sentAt: new Date(Date.now() - 6 * 3600000).toISOString() },
      { id: 'msg-c5-2', conversationId: 'mock-5', direction: 'Inbound', status: 'Delivered', content: 'Ya realice la transferencia esta manana', isFromAgent: false, sentAt: new Date(Date.now() - 5 * 3600000).toISOString() },
    ],
  },
  'mock-6': {
    ...baseMock, id: 'mock-6', clientPhone: '+507 6777-8888', clientName: 'Pedro Castillo', status: 'WaitingClient',
    messages: [
      { id: 'msg-c6-1', conversationId: 'mock-6', direction: 'Outbound', status: 'Delivered', content: 'Buenos dias Pedro. Le informamos que su cuota mensual de $150.00 se encuentra pendiente.', isFromAgent: true, agentName: 'Agente Cobros', sentAt: new Date(Date.now() - 25 * 3600000).toISOString() },
      { id: 'msg-c6-2', conversationId: 'mock-6', direction: 'Inbound', status: 'Delivered', content: 'Perfecto, lo reviso y le confirmo', isFromAgent: false, sentAt: new Date(Date.now() - 24 * 3600000).toISOString() },
    ],
  },
  'mock-7': {
    ...baseMock, id: 'mock-7', clientPhone: '+507 6444-5555', clientName: 'Sofia Hernandez', status: 'Active',
    messages: [
      { id: 'msg-c7-1', conversationId: 'mock-7', direction: 'Inbound', status: 'Delivered', content: 'Hola, me pueden dar informacion sobre sus seguros?', isFromAgent: false, sentAt: new Date(Date.now() - 35 * 60000).toISOString() },
      { id: 'msg-c7-2', conversationId: 'mock-7', direction: 'Outbound', status: 'Delivered', content: 'Hola Sofia! Claro, con gusto. Ofrecemos seguros de auto, vida, incendio y mas. ¿Que tipo de seguro le interesa?', isFromAgent: true, agentName: 'Agente General', sentAt: new Date(Date.now() - 33 * 60000).toISOString() },
      { id: 'msg-c7-3', conversationId: 'mock-7', direction: 'Inbound', status: 'Delivered', content: 'Gracias por la informacion!', isFromAgent: false, sentAt: new Date(Date.now() - 30 * 60000).toISOString() },
    ],
  },
}

export function ConversationDetailPanel({ conversationId }: ConversationDetailPanelProps) {
  const [reply, setReply] = useState('')
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [filePreview, setFilePreview] = useState<string | null>(null)
  const messagesEndRef = useRef<HTMLDivElement>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  const isMock = conversationId.startsWith('mock-')
  const { data: apiConversation, isLoading } = useConversationDetail(isMock ? null : conversationId)
  const takeMutation = useTakeConversation()
  const replyMutation = useSendReply()
  const fileMutation = useSendFile()

  const conversation = isMock ? mockConversationsData[conversationId] : apiConversation

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [conversation?.messages])

  useEffect(() => {
    if (selectedFile && selectedFile.type.startsWith('image/')) {
      const url = URL.createObjectURL(selectedFile)
      setFilePreview(url)
      return () => URL.revokeObjectURL(url)
    } else {
      setFilePreview(null)
    }
  }, [selectedFile])

  useEffect(() => {
    if (textareaRef.current) {
      textareaRef.current.style.height = 'auto'
      textareaRef.current.style.height = Math.min(textareaRef.current.scrollHeight, 120) + 'px'
    }
  }, [reply])

  const handleSend = () => {
    if (isMock) return
    if (selectedFile) {
      fileMutation.mutate({ id: conversationId, file: selectedFile }, { onSuccess: () => setSelectedFile(null) })
      return
    }
    if (!reply.trim()) return
    replyMutation.mutate({ id: conversationId, message: reply })
    setReply('')
  }

  const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (file) setSelectedFile(file)
    e.target.value = ''
  }

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); handleSend() }
  }

  if (!isMock && isLoading) return <LoadingSpinner />

  if (!conversation) {
    return (
      <div className="flex flex-1 items-center justify-center text-sm text-gray-400">
        No se pudo cargar la conversacion
      </div>
    )
  }

  const isSending = replyMutation.isPending || fileMutation.isPending
  const displayName = conversation.clientName ?? conversation.clientPhone

  // Get time for header
  const lastTime = conversation.lastActivityAt ? format(new Date(conversation.lastActivityAt), 'h:mm a') : ''

  return (
    <div className="flex h-full flex-col">
      {/* Header — clean white with name and actions */}
      <div className="flex items-center justify-between border-b border-gray-200 bg-white px-6 py-3">
        <div className="flex items-center gap-3">
          <div>
            <p className="text-base font-semibold text-gray-900">{displayName}</p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <span className={`rounded-full px-3 py-1 text-xs font-medium ${
            conversation.status === 'Active' ? 'bg-green-100 text-green-700' :
            conversation.status === 'WaitingClient' ? 'bg-yellow-100 text-yellow-700' :
            conversation.status === 'EscalatedToHuman' ? 'bg-red-100 text-red-700' :
            'bg-gray-100 text-gray-700'
          }`}>
            {conversation.status === 'Active' ? 'Activa' :
             conversation.status === 'WaitingClient' ? 'Esperando' :
             conversation.status === 'EscalatedToHuman' ? 'Escalada' :
             conversation.status}
          </span>

          {!conversation.isHumanHandled ? (
            <>
              <button
                onClick={() => !isMock && takeMutation.mutate(conversationId)}
                disabled={takeMutation.isPending}
                className="flex items-center gap-1.5 rounded-lg bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700 transition-colors"
              >
                <UserCheck className="h-3.5 w-3.5" /> Tomar
              </button>
              <button className="flex items-center gap-1.5 rounded-lg bg-amber-500 px-3 py-1.5 text-xs font-medium text-white hover:bg-amber-600 transition-colors">
                <PauseCircle className="h-3.5 w-3.5" /> Pausar IA
              </button>
            </>
          ) : (
            <button className="flex items-center gap-1.5 rounded-lg bg-green-500 px-3 py-1.5 text-xs font-medium text-white hover:bg-green-600 transition-colors">
              <PlayCircle className="h-3.5 w-3.5" /> Reactivar IA
            </button>
          )}

          <button className="rounded-lg p-2 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors">
            <MoreVertical className="h-5 w-5" />
          </button>
        </div>
      </div>

      {/* Messages area — light gray bg like the reference */}
      <div className="flex-1 overflow-y-auto px-6 py-4 space-y-3 bg-[#f0f2f5]">
        {/* Date/time separator */}
        <div className="flex items-center justify-center py-2">
          <span className="rounded-lg bg-white px-4 py-1 text-xs text-gray-500 shadow-sm">
            {lastTime || 'Hoy'}
          </span>
        </div>

        {conversation.messages.length > 0 ? (
          conversation.messages.map((msg) => (
            <MessageBubble key={msg.id} message={msg} />
          ))
        ) : (
          <div className="flex items-center justify-center h-full">
            <div className="rounded-lg bg-white px-4 py-2 text-xs text-gray-500 shadow-sm">
              Sin mensajes aun
            </div>
          </div>
        )}
        <div ref={messagesEndRef} />
      </div>

      {/* File preview */}
      {selectedFile && (
        <div className="flex items-center gap-3 border-t border-gray-200 bg-white px-6 py-3">
          {filePreview ? (
            <img src={filePreview} alt="Preview" className="h-16 w-16 rounded-lg object-cover" />
          ) : (
            <div className="flex h-16 w-16 items-center justify-center rounded-lg bg-blue-50">
              <span className="text-xs font-medium text-blue-600">{selectedFile.name.split('.').pop()?.toUpperCase()}</span>
            </div>
          )}
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-medium text-gray-900">{selectedFile.name}</p>
            <p className="text-xs text-gray-500">{(selectedFile.size / 1024).toFixed(1)} KB</p>
          </div>
          <button onClick={() => setSelectedFile(null)} className="text-gray-400 hover:text-gray-600">
            <X className="h-5 w-5" />
          </button>
        </div>
      )}

      {/* Input area — matching reference: emoji, clip, text field, send */}
      <div className="flex items-end gap-3 border-t border-gray-200 bg-white px-6 py-3">
        <button type="button" className="mb-1 rounded-full p-2 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors">
          <Smile className="h-5 w-5" />
        </button>

        <button type="button" onClick={() => fileInputRef.current?.click()} className="mb-1 rounded-full p-2 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors">
          <Paperclip className="h-5 w-5" />
        </button>

        <input ref={fileInputRef} type="file" className="hidden" onChange={handleFileSelect} />

        <div className="flex-1">
          <textarea
            ref={textareaRef}
            value={reply}
            onChange={(e) => setReply(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Type a message..."
            rows={1}
            className="w-full resize-none rounded-xl border border-gray-200 bg-[#f6f6f6] px-4 py-2.5 text-sm text-gray-700 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-400/50 focus:border-blue-400"
            style={{ maxHeight: 120 }}
          />
        </div>

        <button
          onClick={handleSend}
          disabled={(!reply.trim() && !selectedFile) || isSending}
          className="mb-0.5 flex h-10 w-10 items-center justify-center rounded-full bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-40 transition-colors shrink-0"
        >
          {isSending ? (
            <Loader2 className="h-5 w-5 animate-spin" />
          ) : (
            <Send className="h-5 w-5" />
          )}
        </button>
      </div>
    </div>
  )
}
