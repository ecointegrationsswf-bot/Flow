import { useState, useEffect, useRef } from 'react'
import { UserCheck, PauseCircle, PlayCircle, Send, Paperclip, Smile, X, Loader2, MoreVertical } from 'lucide-react'
import { LoadingSpinner } from '@/shared/components/LoadingSpinner'
import { MessageBubble } from './MessageBubble'
import { useConversationDetail, useTakeConversation, useSendReply, useSendFile, useReactivateAgent } from '@/shared/hooks/useMonitor'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { useTenantTime } from '@/shared/hooks/useTenantTime'

interface ConversationDetailPanelProps {
  conversationId: string
}


export function ConversationDetailPanel({ conversationId }: ConversationDetailPanelProps) {
  const [reply, setReply] = useState('')
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [filePreview, setFilePreview] = useState<string | null>(null)
  const messagesEndRef = useRef<HTMLDivElement>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  const { hasPermission } = usePermissions()
  const canTake = hasPermission('take_conversation')
  const { time: fmtTime } = useTenantTime()

  const { data: conversation, isLoading } = useConversationDetail(conversationId)
  const takeMutation = useTakeConversation()
  const replyMutation = useSendReply()
  const fileMutation = useSendFile()
  const reactivateMutation = useReactivateAgent()

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

  if (isLoading) return <LoadingSpinner />

  if (!conversation) {
    return (
      <div className="flex flex-1 items-center justify-center text-sm text-gray-400">
        No se pudo cargar la conversacion
      </div>
    )
  }

  const isSending = replyMutation.isPending || fileMutation.isPending
  const displayName = conversation.clientName ?? conversation.clientPhone

  // Get time for header (en zona horaria del tenant)
  const lastTime = fmtTime(conversation.lastActivityAt)

  return (
    <div className="flex h-full flex-col">
      {/* Header — clean white with name and actions */}
      <div className="flex items-center justify-between border-b border-gray-200 bg-white px-6 py-3">
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-2">
            <p className="text-base font-semibold text-gray-900">{displayName}</p>
            {conversation.campaignName && (
              <span className="rounded-full bg-blue-50 px-2.5 py-0.5 text-xs font-medium text-blue-600 border border-blue-100">
                {conversation.campaignName}
              </span>
            )}
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

          {canTake && (!conversation.isHumanHandled ? (
            <button
              onClick={() => takeMutation.mutate(conversationId)}
              disabled={takeMutation.isPending}
              className="flex items-center gap-1.5 rounded-lg bg-amber-500 px-3 py-1.5 text-xs font-medium text-white hover:bg-amber-600 transition-colors disabled:opacity-50"
            >
              <PauseCircle className="h-3.5 w-3.5" /> Pausar IA
            </button>
          ) : (
            <button
              onClick={() => reactivateMutation.mutate(conversationId)}
              disabled={reactivateMutation.isPending}
              className="flex items-center gap-1.5 rounded-lg bg-green-500 px-3 py-1.5 text-xs font-medium text-white hover:bg-green-600 transition-colors disabled:opacity-50"
            >
              <PlayCircle className="h-3.5 w-3.5" /> Reactivar IA
            </button>
          ))}

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

        {(conversation.messages ?? []).length > 0 ? (
          (conversation.messages ?? []).map((msg) => (
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

      {/* Input area */}
      {!canTake ? (
        <div className="flex items-center justify-center gap-2 border-t border-gray-200 bg-gray-50 px-6 py-3">
          <p className="text-xs text-gray-400">Solo lectura — sin permiso para intervenir conversaciones</p>
        </div>
      ) : conversation.isHumanHandled ? (
        <div className="flex items-center justify-center gap-2 border-t border-gray-200 bg-gray-50 px-6 py-4">
          <UserCheck className="h-4 w-4 text-amber-500 shrink-0" />
          <p className="text-sm text-gray-500">
            Un ejecutivo está atendiendo esta conversación por WhatsApp. El chat del monitor está deshabilitado.
          </p>
        </div>
      ) : (
        <div className="flex items-end gap-3 border-t border-gray-200 bg-white px-6 py-3">
          <button type="button" className="mb-1 rounded-full p-2 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors">
            <Smile className="h-5 w-5" />
          </button>

          <button type="button" onClick={() => fileInputRef.current?.click()} className="mb-1 rounded-full p-2 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors">
            <Paperclip className="h-5 w-5" />
          </button>

          <input ref={fileInputRef} type="file" className="hidden" onChange={handleFileSelect} accept="image/*,.pdf" />

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
      )}
    </div>
  )
}
