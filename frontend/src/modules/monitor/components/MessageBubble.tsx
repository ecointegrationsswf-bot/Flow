import { format } from 'date-fns'
import type { Message } from '@/shared/types'

export function MessageBubble({ message }: { message: Message }) {
  const isInbound = message.direction === 'Inbound'
  const isAgent = !isInbound && message.isFromAgent
  const isHuman = !isInbound && !message.isFromAgent

  const alignment = isInbound ? 'items-start' : 'items-end'
  const bgColor = isInbound
    ? 'bg-gray-100 text-gray-900'
    : isAgent
      ? 'bg-blue-600 text-white'
      : 'bg-green-600 text-white'

  const metaColor = isInbound ? 'text-gray-400' : 'text-white/70'

  return (
    <div className={`flex flex-col ${alignment}`}>
      <div className={`max-w-[75%] rounded-lg px-3 py-2 ${bgColor}`}>
        {/* Sender label */}
        {!isInbound && (
          <p className={`text-xs font-medium ${isInbound ? 'text-gray-500' : 'text-white/80'} mb-0.5`}>
            {isAgent ? (message.agentName ?? 'Agente IA') : 'Ejecutivo'}
          </p>
        )}

        <p className="whitespace-pre-wrap text-sm">{message.content}</p>

        {/* Meta */}
        <div className={`mt-1 flex items-center gap-2 text-xs ${metaColor}`}>
          <span>{format(new Date(message.sentAt), 'HH:mm')}</span>
          {message.detectedIntent && (
            <span className="rounded bg-black/10 px-1 py-0.5 text-[10px]">
              {message.detectedIntent}
            </span>
          )}
          {message.confidenceScore != null && message.confidenceScore < 0.7 && (
            <span className="rounded bg-yellow-500/20 px-1 py-0.5 text-[10px]">
              confianza: {Math.round(message.confidenceScore * 100)}%
            </span>
          )}
        </div>
      </div>
    </div>
  )
}
