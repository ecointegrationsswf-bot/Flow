import { useRef, useState, useEffect } from 'react'
import { Sparkles, Send, Wrench, Check, X, Loader2 } from 'lucide-react'
import { adminClient } from '@/shared/api/adminClient'
import { toast } from '@/shared/components/dialog'

// Copiloto IT — Fase 1 (Monitor + Prompts). Chat con tool-use sobre el API:
// lecturas libres (diagnóstico) y escrituras SOLO como borrador con tarjeta "Aplicar".

interface ToolActivity { tool: string; args: string }
interface Draft { type: string; title: string; payload: Record<string, unknown>; applied?: boolean; discarded?: boolean }
interface ChatMsg {
  role: 'user' | 'assistant'
  content: string
  tools?: ToolActivity[]
  drafts?: Draft[]
}

const SUGERENCIAS = [
  '¿Cómo están los crons hoy?',
  '¿Por qué la última campaña de UNISEGUROS no envió?',
  'Listame los prompts del catálogo',
  'Mejorame el prompt de avisos de cancelación',
]

export function CopilotPage() {
  const [messages, setMessages] = useState<ChatMsg[]>([])
  const [input, setInput] = useState('')
  const [loading, setLoading] = useState(false)
  const bottomRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLTextAreaElement>(null)

  useEffect(() => { bottomRef.current?.scrollIntoView({ behavior: 'smooth' }) }, [messages, loading])

  const send = async (text?: string) => {
    const content = (text ?? input).trim()
    if (!content || loading) return
    setInput('')
    if (inputRef.current) inputRef.current.style.height = 'auto'
    const next: ChatMsg[] = [...messages, { role: 'user', content }]
    setMessages(next)
    setLoading(true)
    try {
      const { data } = await adminClient.post('/admin/copilot/chat', {
        messages: next.map((m) => ({ role: m.role, content: m.content })),
      })
      setMessages((prev) => [...prev, {
        role: 'assistant',
        content: data.reply,
        tools: data.tools ?? [],
        drafts: (data.drafts ?? []).map((d: Draft) => ({ ...d })),
      }])
    } catch {
      setMessages((prev) => [...prev, { role: 'assistant', content: '⚠️ No pude procesar la consulta. Reintentá en un momento.' }])
    } finally {
      setLoading(false)
    }
  }

  const applyDraft = async (msgIdx: number, draftIdx: number) => {
    const draft = messages[msgIdx]?.drafts?.[draftIdx]
    if (!draft || draft.applied) return
    try {
      const { data } = await adminClient.post('/admin/copilot/apply', { type: draft.type, payload: draft.payload })
      setMessages((prev) => prev.map((m, i) => i !== msgIdx ? m : {
        ...m,
        drafts: m.drafts?.map((d, j) => j === draftIdx ? { ...d, applied: true } : d),
      }))
      toast.success(`Aplicado: ${draft.title}${data?.maestroNote ? ' · ' + data.maestroNote : ''}`)
    } catch {
      /* interceptor global muestra el error */
    }
  }

  const discardDraft = (msgIdx: number, draftIdx: number) => {
    setMessages((prev) => prev.map((m, i) => i !== msgIdx ? m : {
      ...m,
      drafts: m.drafts?.map((d, j) => j === draftIdx ? { ...d, discarded: true } : d),
    }))
  }

  return (
    <div className="flex h-[calc(100vh-5rem)] flex-col">
      <div className="mb-3 flex items-center gap-2">
        <Sparkles className="h-6 w-6 text-amber-500" />
        <div>
          <h1 className="text-xl font-bold text-gray-800">Copiloto IT</h1>
          <p className="text-xs text-gray-500">
            Diagnostica procesos y ayuda con prompts. Las escrituras son borradores: nada se guarda sin tu clic en <b>Aplicar</b>.
          </p>
        </div>
      </div>

      <div className="flex-1 space-y-4 overflow-y-auto rounded-lg border border-gray-200 bg-white p-4">
        {messages.length === 0 && (
          <div className="py-10 text-center">
            <p className="mb-4 text-sm text-gray-500">¿En qué te ayudo? Probá con:</p>
            <div className="mx-auto flex max-w-xl flex-wrap justify-center gap-2">
              {SUGERENCIAS.map((s) => (
                <button key={s} onClick={() => void send(s)}
                  className="rounded-full border border-gray-200 px-3 py-1.5 text-xs text-gray-600 hover:border-amber-400 hover:bg-amber-50">
                  {s}
                </button>
              ))}
            </div>
          </div>
        )}

        {messages.map((m, mi) => (
          <div key={mi} className={m.role === 'user' ? 'flex justify-end' : 'flex justify-start'}>
            <div className={`max-w-[85%] rounded-lg px-4 py-3 text-sm ${m.role === 'user' ? 'bg-amber-500 text-white' : 'bg-gray-50 text-gray-800 ring-1 ring-gray-200'}`}>
              {m.tools && m.tools.length > 0 && (
                <div className="mb-2 flex flex-wrap gap-1.5">
                  {m.tools.map((t, ti) => (
                    <span key={ti} title={t.args} className="flex items-center gap-1 rounded-full bg-blue-50 px-2 py-0.5 text-[10px] font-medium text-blue-700">
                      <Wrench className="h-2.5 w-2.5" /> {t.tool}
                    </span>
                  ))}
                </div>
              )}
              <div className="whitespace-pre-wrap leading-relaxed">{m.content}</div>

              {m.drafts?.map((d, di) => (
                <div key={di} className={`mt-3 rounded-md border p-3 ${d.applied ? 'border-emerald-200 bg-emerald-50' : d.discarded ? 'border-gray-200 bg-gray-100 opacity-60' : 'border-amber-300 bg-amber-50'}`}>
                  <div className="mb-1 flex items-center justify-between gap-2">
                    <span className="text-xs font-bold text-gray-800">{d.title}</span>
                    {d.applied && <span className="flex items-center gap-1 text-[11px] font-semibold text-emerald-700"><Check className="h-3 w-3" /> Aplicado</span>}
                    {d.discarded && <span className="text-[11px] text-gray-500">Descartado</span>}
                  </div>
                  <pre className="mb-2 max-h-48 overflow-y-auto whitespace-pre-wrap rounded bg-white p-2 text-[11px] text-gray-700 ring-1 ring-gray-200">
                    {typeof d.payload?.systemPrompt === 'string'
                      ? (d.payload.systemPrompt as string)
                      : JSON.stringify(d.payload, null, 2)}
                  </pre>
                  {!d.applied && !d.discarded && (
                    <div className="flex gap-2">
                      <button onClick={() => void applyDraft(mi, di)}
                        className="flex items-center gap-1 rounded-md bg-emerald-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-emerald-700">
                        <Check className="h-3.5 w-3.5" /> Aplicar
                      </button>
                      <button onClick={() => discardDraft(mi, di)}
                        className="flex items-center gap-1 rounded-md border border-gray-300 px-3 py-1.5 text-xs text-gray-600 hover:bg-gray-50">
                        <X className="h-3.5 w-3.5" /> Descartar
                      </button>
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>
        ))}

        {loading && (
          <div className="flex items-center gap-2 text-xs text-gray-400">
            <Loader2 className="h-3.5 w-3.5 animate-spin" /> Consultando la plataforma…
          </div>
        )}
        <div ref={bottomRef} />
      </div>

      <div className="mt-3 flex items-end gap-2">
        <textarea
          ref={inputRef}
          value={input}
          onChange={(e) => {
            setInput(e.target.value)
            // Auto-crecer hasta ~10 líneas; después scroll interno.
            const el = e.target
            el.style.height = 'auto'
            el.style.height = Math.min(el.scrollHeight, 220) + 'px'
          }}
          onKeyDown={(e) => {
            // Enter envía · Shift+Enter inserta salto de línea (peticiones extensas).
            if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void send() }
          }}
          rows={2}
          placeholder="Preguntale al copiloto… (Shift+Enter para salto de línea — podés pegar peticiones largas, endpoints, JSON, etc.)"
          className="flex-1 resize-none rounded-lg border border-gray-300 px-4 py-2.5 text-sm leading-relaxed focus:border-amber-500 focus:outline-none focus:ring-1 focus:ring-amber-500"
          style={{ maxHeight: 220 }}
          disabled={loading}
        />
        <button onClick={() => void send()} disabled={loading || !input.trim()}
          className="flex items-center gap-1.5 rounded-lg bg-amber-500 px-4 py-2.5 text-sm font-semibold text-white hover:bg-amber-600 disabled:opacity-50">
          <Send className="h-4 w-4" /> Enviar
        </button>
      </div>
    </div>
  )
}
