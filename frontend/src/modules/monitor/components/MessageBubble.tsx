import { useState } from 'react'
import { FileText, Download, CheckCheck, Mic, X, ZoomIn, Mail, ChevronDown, ChevronUp, AlertCircle, Maximize2 } from 'lucide-react'
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

  // ── Render diferenciado para mensajes de Email ──────────────────────────
  // Channel=Email se persiste por SendEmailResumeService (Phase 1 outbound
  // tracking). Visualmente queremos que NO se vea como una burbuja de WhatsApp
  // sino como una tarjeta de correo: header con ✉, asunto + destinatarios,
  // y el body HTML colapsable (puede ser muy largo).
  if (message.channel === 'Email') {
    return <EmailMessageCard message={message} fmtTime={fmtTime} />
  }

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

/**
 * Tarjeta para mensajes con Channel=Email. Distinta visualmente de WhatsApp
 * — usa banner amber, ícono de sobre, muestra asunto + destinatario y el
 * body HTML colapsable. El HTML viene sanitizado del backend (EmailTemplateRenderer)
 * así que podemos pintarlo con dangerouslySetInnerHTML sin riesgo de XSS.
 */
function EmailMessageCard({
  message, fmtTime,
}: { message: Message; fmtTime: (date: string) => string }) {
  const [expanded, setExpanded] = useState(false)
  const [fullscreen, setFullscreen] = useState(false)
  const isInbound = message.direction === 'Inbound'
  const failed = message.status === 'Failed'
  const subject = message.subject ?? '(sin asunto)'
  const recipient = message.recipient ?? ''
  const body = message.content ?? ''
  // Heurística: si el contenido empieza con < lo tratamos como HTML, sino texto.
  const isHtml = body.trimStart().startsWith('<')

  // Vista previa: primeros 220 chars del texto plano para el modo colapsado.
  const preview = isHtml
    ? body.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 220)
    : body.slice(0, 220)
  const hasMore = body.length > preview.length

  const palette = failed
    ? { ring: 'border-red-200', head: 'bg-red-50 text-red-700', icon: 'text-red-500', tag: 'bg-red-100 text-red-700' }
    : isInbound
      ? { ring: 'border-blue-200', head: 'bg-blue-50 text-blue-800', icon: 'text-blue-600', tag: 'bg-blue-100 text-blue-700' }
      : { ring: 'border-amber-200', head: 'bg-amber-50 text-amber-800', icon: 'text-amber-600', tag: 'bg-amber-100 text-amber-700' }

  return (
    <>
      <div className="my-2 flex justify-center">
        <div className={`w-full max-w-[88%] rounded-lg border bg-white shadow-sm ${palette.ring}`}>
          {/* Header */}
          <div className={`flex items-center gap-2 px-4 py-2 rounded-t-lg ${palette.head}`}>
            <Mail className={`h-4 w-4 ${palette.icon}`} />
            <span className="text-xs font-semibold uppercase tracking-wide">
              {isInbound ? 'Email recibido' : failed ? 'Email no enviado' : 'Email enviado'}
            </span>
            {failed && <AlertCircle className="h-3.5 w-3.5 text-red-500" />}
            <span className="ml-auto text-[11px] opacity-80">{fmtTime(message.sentAt)}</span>
          </div>

          {/* Asunto + destinatario */}
          <div className="px-4 py-3 border-b border-gray-100">
            <div className="text-[10px] uppercase tracking-wide text-gray-500 font-semibold">Asunto</div>
            <div className="text-sm font-semibold text-gray-900 leading-snug">{subject}</div>
            {recipient && (
              <>
                <div className="mt-2 text-[10px] uppercase tracking-wide text-gray-500 font-semibold">Destinatario</div>
                <div className="text-xs text-gray-700 font-mono break-all">{recipient}</div>
              </>
            )}
          </div>

          {/* Cuerpo: preview colapsado / expandido */}
          <div className="px-4 py-3">
            {!expanded ? (
              <>
                <p className="text-xs text-gray-600 leading-relaxed line-clamp-3">{preview}{hasMore ? '…' : ''}</p>
                <div className="mt-2 flex items-center gap-2">
                  {hasMore && (
                    <button
                      type="button"
                      onClick={() => setExpanded(true)}
                      className={`inline-flex items-center gap-1 rounded px-2 py-0.5 text-[11px] font-medium ${palette.tag} hover:opacity-90`}
                    >
                      <ChevronDown className="h-3 w-3" /> Ver contenido completo
                    </button>
                  )}
                  {isHtml && (
                    <button
                      type="button"
                      onClick={() => setFullscreen(true)}
                      className="inline-flex items-center gap-1 rounded border border-gray-200 px-2 py-0.5 text-[11px] font-medium text-gray-600 hover:bg-gray-50"
                    >
                      <Maximize2 className="h-3 w-3" /> Ver como correo
                    </button>
                  )}
                </div>
              </>
            ) : (
              <>
                {isHtml ? (
                  <div className="rounded-md border border-gray-200 bg-gray-50 p-3 overflow-auto" style={{ maxHeight: 360 }}>
                    <div className="prose prose-sm max-w-none" dangerouslySetInnerHTML={{ __html: body }} />
                  </div>
                ) : (
                  <pre className="rounded-md border border-gray-200 bg-gray-50 p-3 text-xs text-gray-800 whitespace-pre-wrap font-sans">{body}</pre>
                )}
                <div className="mt-2 flex items-center gap-2">
                  <button
                    type="button"
                    onClick={() => setExpanded(false)}
                    className={`inline-flex items-center gap-1 rounded px-2 py-0.5 text-[11px] font-medium ${palette.tag} hover:opacity-90`}
                  >
                    <ChevronUp className="h-3 w-3" /> Colapsar
                  </button>
                  {isHtml && (
                    <button
                      type="button"
                      onClick={() => setFullscreen(true)}
                      className="inline-flex items-center gap-1 rounded border border-gray-200 px-2 py-0.5 text-[11px] font-medium text-gray-600 hover:bg-gray-50"
                    >
                      <Maximize2 className="h-3 w-3" /> Ver como correo
                    </button>
                  )}
                </div>
              </>
            )}
          </div>

          {/* Footer con status + agente */}
          <div className="flex items-center justify-between border-t border-gray-100 bg-gray-50 px-4 py-1.5 rounded-b-lg">
            <div className="flex items-center gap-2 text-[11px] text-gray-500">
              {message.agentName && <span>Por {message.agentName}</span>}
              {message.externalMessageId && (
                <span className="font-mono truncate max-w-[140px]" title={message.externalMessageId}>· {message.externalMessageId}</span>
              )}
            </div>
            <span className={`text-[11px] font-medium ${failed ? 'text-red-600' : 'text-emerald-600'}`}>
              {failed ? 'Falló' : message.status}
            </span>
          </div>
        </div>
      </div>

      {/* Modal fullscreen del email — para ver el HTML renderizado como un cliente lo vería. */}
      {fullscreen && isHtml && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
          onClick={() => setFullscreen(false)}
        >
          <div
            className="max-h-[92vh] w-full max-w-3xl overflow-hidden rounded-lg bg-white shadow-2xl"
            onClick={(e) => e.stopPropagation()}
          >
            <header className="flex items-start justify-between border-b border-gray-200 px-5 py-3">
              <div className="min-w-0">
                <p className="text-xs text-gray-500">Vista previa del correo</p>
                <h3 className="text-base font-semibold text-gray-900 truncate">{subject}</h3>
                {recipient && <p className="text-xs text-gray-500 font-mono truncate">{recipient}</p>}
              </div>
              <button onClick={() => setFullscreen(false)} className="ml-2 rounded-md p-1 text-gray-500 hover:bg-gray-100">
                <X className="h-4 w-4" />
              </button>
            </header>
            <div className="max-h-[calc(92vh-5rem)] overflow-auto bg-gray-50 p-6">
              <div className="rounded-lg border border-gray-200 bg-white p-6 shadow-sm">
                <div className="prose prose-sm max-w-none" dangerouslySetInnerHTML={{ __html: body }} />
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  )
}
