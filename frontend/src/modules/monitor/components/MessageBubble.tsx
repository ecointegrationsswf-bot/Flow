import { useState } from 'react'
import { FileText, Download, CheckCheck, Mic, X, ZoomIn } from 'lucide-react'
import type { Message } from '@/shared/types'
import { useTenantTime } from '@/shared/hooks/useTenantTime'

const agentAvatars: Record<string, string> = {
  'Agente Cobros': 'https://i.pravatar.cc/150?img=32',
  'Agente Reclamos': 'https://i.pravatar.cc/150?img=44',
  'Agente Renovaciones': 'https://i.pravatar.cc/150?img=52',
  'Agente General': 'https://i.pravatar.cc/150?img=60',
}

/** Separa el texto visible del [media:URL] incrustado en el contenido */
function parseContent(raw: string | null | undefined): { text: string; mediaUrl: string | null; mediaKind: 'audio' | 'image' | 'document' | null } {
  if (!raw) return { text: '', mediaUrl: null, mediaKind: null }
  const mediaMatch = raw.match(/\[media:(https?:\/\/[^\]]+)\]/)
  if (!mediaMatch) return { text: raw, mediaUrl: null, mediaKind: null }

  const url = mediaMatch[1]
  const text = raw.replace(mediaMatch[0], '').trim()

  let mediaKind: 'audio' | 'image' | 'document' = 'document'
  if (/\.(ogg|mp3|m4a|wav|webm|oga)(\?|$)/i.test(url)) mediaKind = 'audio'
  else if (/\.(jpg|jpeg|png|gif|webp)(\?|$)/i.test(url)) mediaKind = 'image'

  return { text, mediaUrl: url, mediaKind }
}

export function MessageBubble({ message }: { message: Message }) {
  const isInbound = message.direction === 'Inbound'
  const [lightbox, setLightbox] = useState<string | null>(null)
  const { time: fmtTime } = useTenantTime()

  const { text, mediaUrl, mediaKind } = parseContent(message.content)

  // Si el contenido completo es solo una URL de imagen o documento (sin [media:] wrapper)
  const isRawImage = !mediaUrl && /\.(jpg|jpeg|png|gif|webp)(\?|$)/i.test(message.content)
  const isRawFile  = !mediaUrl && /\.(pdf|doc|docx|xls|xlsx|csv|txt|zip)(\?|$)/i.test(message.content)

  const getFileName = (url: string) => {
    try {
      return decodeURIComponent(url.split('/').pop()?.split('?')[0] ?? 'archivo')
    } catch { return 'archivo' }
  }

  const avatarUrl = !isInbound && message.agentName ? agentAvatars[message.agentName] : null

  return (
    <>
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
        {isRawImage ? (
          <div className="my-1">
            <img src={message.content} alt="Imagen" className="max-w-full rounded-lg" style={{ maxHeight: 300 }} />
          </div>
        ) : isRawFile ? (
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
          <>
            {/* Media adjunta al mensaje (imagen, audio o documento) */}
            {mediaUrl && mediaKind === 'image' && (
              <div className="relative mb-1 group cursor-pointer" onClick={() => setLightbox(mediaUrl)}>
                <img src={mediaUrl} alt="Imagen" className="max-w-full rounded-lg" style={{ maxHeight: 260 }} />
                <div className="absolute inset-0 flex items-center justify-center rounded-lg bg-black/0 group-hover:bg-black/20 transition-colors">
                  <ZoomIn className="h-7 w-7 text-white opacity-0 group-hover:opacity-100 transition-opacity drop-shadow" />
                </div>
              </div>
            )}
            {mediaUrl && mediaKind === 'audio' && (
              <div className={`flex items-center gap-2 rounded-lg px-3 py-2 mb-1 ${
                isInbound ? 'bg-gray-50' : 'bg-white/10'
              }`}>
                <Mic className={`h-4 w-4 shrink-0 ${isInbound ? 'text-blue-500' : 'text-white/80'}`} />
                <audio controls src={mediaUrl} className="h-8 w-36" preload="none" />
              </div>
            )}
            {mediaUrl && mediaKind === 'document' && (
              <a
                href={mediaUrl}
                target="_blank"
                rel="noopener noreferrer"
                className={`flex items-center gap-2 rounded-lg p-2 mb-1 transition-colors ${
                  isInbound ? 'bg-gray-50 hover:bg-gray-100' : 'bg-white/10 hover:bg-white/20'
                }`}
              >
                <FileText className={`h-8 w-8 shrink-0 ${isInbound ? 'text-blue-500' : 'text-white/80'}`} />
                <div className="min-w-0 flex-1">
                  <p className={`text-sm font-medium truncate ${isInbound ? 'text-gray-900' : 'text-white'}`}>
                    {getFileName(mediaUrl)}
                  </p>
                  <p className={`text-xs ${isInbound ? 'text-gray-500' : 'text-white/60'}`}>Documento</p>
                </div>
                <Download className={`h-4 w-4 ${isInbound ? 'text-gray-400' : 'text-white/60'}`} />
              </a>
            )}
            {/* Texto del mensaje (transcripción o texto normal) */}
            {text && <p className="whitespace-pre-wrap text-[13.5px] leading-relaxed">{text}</p>}
          </>
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
          <span className="text-[11px]">{fmtTime(message.sentAt)}</span>
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

    {/* Lightbox */}
    {lightbox && (
      <div
        className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-4"
        onClick={() => setLightbox(null)}
      >
        <button
          className="absolute right-4 top-4 rounded-full bg-white/10 p-2 text-white hover:bg-white/20"
          onClick={() => setLightbox(null)}
        >
          <X className="h-6 w-6" />
        </button>
        <img
          src={lightbox}
          alt="Imagen completa"
          className="max-h-full max-w-full rounded-xl object-contain shadow-2xl"
          onClick={(e) => e.stopPropagation()}
        />
      </div>
    )}
  </>
  )
}
