import { format } from 'date-fns'
import { FileText, Download, CheckCheck } from 'lucide-react'
import type { Message } from '@/shared/types'

const agentAvatars: Record<string, string> = {
  'Agente Cobros': 'https://i.pravatar.cc/150?img=32',
  'Agente Reclamos': 'https://i.pravatar.cc/150?img=44',
  'Agente Renovaciones': 'https://i.pravatar.cc/150?img=52',
  'Agente General': 'https://i.pravatar.cc/150?img=60',
}

export function MessageBubble({ message }: { message: Message }) {
  const isInbound = message.direction === 'Inbound'

  const isImage = /\.(jpg|jpeg|png|gif|webp)(\?|$)/i.test(message.content)
  const isFile = /\.(pdf|doc|docx|xls|xlsx|csv|txt|zip)(\?|$)/i.test(message.content)

  const getFileName = (url: string) => {
    try {
      return decodeURIComponent(url.split('/').pop()?.split('?')[0] ?? 'archivo')
    } catch { return 'archivo' }
  }

  const avatarUrl = !isInbound && message.agentName ? agentAvatars[message.agentName] : null

  return (
    <div className={`flex ${isInbound ? 'justify-start' : 'justify-end'} mb-1`}>
      {/* Inbound: avatar */}
      {isInbound && (
        <div className="mr-2 mt-auto shrink-0">
          <img
            src="https://i.pravatar.cc/150?img=1"
            alt=""
            className="h-8 w-8 rounded-full object-cover"
          />
        </div>
      )}

      <div
        className={`relative max-w-[55%] rounded-2xl px-4 py-2.5 shadow-sm ${
          isInbound
            ? 'bg-white text-gray-900'
            : 'bg-[#4a7cf7] text-white'
        }`}
        style={{
          borderBottomLeftRadius: isInbound ? '4px' : undefined,
          borderBottomRightRadius: !isInbound ? '4px' : undefined,
        }}
      >
        {/* Content */}
        {isImage ? (
          <div className="my-1">
            <img src={message.content} alt="Imagen" className="max-w-full rounded-lg" style={{ maxHeight: 300 }} />
          </div>
        ) : isFile ? (
          <a
            href={message.content}
            target="_blank"
            rel="noopener noreferrer"
            className={`flex items-center gap-2 rounded-lg p-2 my-1 transition-colors ${
              isInbound ? 'bg-gray-50 hover:bg-gray-100' : 'bg-white/10 hover:bg-white/20'
            }`}
          >
            <FileText className={`h-8 w-8 shrink-0 ${isInbound ? 'text-blue-500' : 'text-white/80'}`} />
            <div className="min-w-0 flex-1">
              <p className={`text-sm font-medium truncate ${isInbound ? 'text-gray-900' : 'text-white'}`}>
                {getFileName(message.content)}
              </p>
              <p className={`text-xs ${isInbound ? 'text-gray-500' : 'text-white/60'}`}>Documento</p>
            </div>
            <Download className={`h-4 w-4 ${isInbound ? 'text-gray-400' : 'text-white/60'}`} />
          </a>
        ) : (
          <p className="whitespace-pre-wrap text-[13.5px] leading-relaxed">{message.content}</p>
        )}

        {/* Time & status */}
        <div className={`flex items-center justify-end gap-1 mt-1 ${
          isInbound ? 'text-gray-400' : 'text-white/60'
        }`}>
          {message.detectedIntent && (
            <span className={`rounded px-1.5 py-0.5 text-[10px] ${
              isInbound ? 'bg-blue-50 text-blue-600' : 'bg-white/15 text-white/80'
            }`}>
              {message.detectedIntent}
            </span>
          )}
          <span className="text-[11px]">{format(new Date(message.sentAt), 'h:mm a')}</span>
          {!isInbound && (
            <CheckCheck className="h-3.5 w-3.5" />
          )}
        </div>
      </div>

      {/* Outbound: avatar */}
      {!isInbound && avatarUrl && (
        <div className="ml-2 mt-auto shrink-0">
          <img
            src={avatarUrl}
            alt=""
            className="h-8 w-8 rounded-full object-cover"
          />
        </div>
      )}
    </div>
  )
}
