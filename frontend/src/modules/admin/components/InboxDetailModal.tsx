import { X, MessageSquare, AlertCircle, FileText, Bot, User } from 'lucide-react'
import { useInboxItemDetail } from '../hooks/useInboxMonitor'

const PANAMA_TZ = 'America/Panama'

const STATUS_LABEL: Record<string, string> = {
  Pending: 'Pendiente',
  Claimed: 'Reclamado',
  Processing: 'Procesando',
  Replied: 'Respondido',
  Failed: 'Fallido',
  Escalated: 'Escalado',
}

function parseUtc(iso: string): Date {
  return /[Zz]|[+-]\d{2}:?\d{2}$/.test(iso) ? new Date(iso) : new Date(iso + 'Z')
}

function fmtPanama(iso: string | null | undefined): string {
  if (!iso) return '—'
  return parseUtc(iso).toLocaleString('es-PA', {
    timeZone: PANAMA_TZ,
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
    hour12: false,
  })
}

export function InboxDetailModal({ itemId, onClose }: { itemId: string | null; onClose: () => void }) {
  const { data, isLoading, isError } = useInboxItemDetail(itemId)

  if (!itemId) return null

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
      onClick={onClose}
    >
      <div
        className="max-h-[90vh] w-full max-w-4xl overflow-hidden rounded-lg bg-white shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <header className="flex items-start justify-between border-b border-gray-200 px-6 py-4">
          <div>
            <h2 className="text-lg font-semibold text-gray-900">Detalle del mensaje en cola</h2>
            <p className="font-mono text-xs text-gray-500">{itemId}</p>
          </div>
          <button onClick={onClose} className="rounded-md p-1 text-gray-500 hover:bg-gray-100 hover:text-gray-700">
            <X className="h-5 w-5" />
          </button>
        </header>

        <div className="max-h-[calc(90vh-4rem)] overflow-y-auto px-6 py-4">
          {isLoading && <p className="text-sm text-gray-500">Cargando…</p>}
          {isError && <p className="text-sm text-red-600">Error cargando el detalle.</p>}

          {data && (
            <div className="space-y-6">
              {/* Resumen */}
              <Section title="Resumen" icon={<FileText className="h-4 w-4 text-gray-500" />}>
                <dl className="grid grid-cols-2 gap-x-6 gap-y-2 text-sm md:grid-cols-3">
                  <Info label="Tenant" value={data.tenantName ?? data.item.tenantId} mono={!data.tenantName} />
                  <Info label="Teléfono" value={data.item.fromPhone} mono />
                  <Info label="Canal" value={data.item.channel} />
                  <Info label="Estado" value={
                    <StatusPill status={data.item.status} />
                  } />
                  <Info label="Intentos" value={String(data.item.attemptCount)} />
                  <Info label="Buffer (s)" value={String(data.item.bufferSeconds)} />
                  <Info label="Primer mensaje" value={fmtPanama(data.item.firstReceivedAt)} mono />
                  <Info label="Último mensaje" value={fmtPanama(data.item.lastReceivedAt)} mono />
                  <Info label="Completado" value={fmtPanama(data.item.completedAt)} mono />
                  <Info label="Reclamado por" value={data.item.claimedBy ?? '—'} mono />
                  <Info label="External ID" value={data.item.externalMessageId ?? '—'} mono />
                  <Info label="WhatsApp Line" value={data.item.whatsAppLineId ?? '—'} mono />
                </dl>
              </Section>

              {/* Error si existe */}
              {data.item.lastError && (
                <Section title="Último error" icon={<AlertCircle className="h-4 w-4 text-red-500" />}>
                  {data.item.lastErrorStep && (
                    <p className="mb-1 text-xs text-gray-500">
                      Paso: <code className="rounded bg-gray-100 px-1.5 py-0.5">{data.item.lastErrorStep}</code>
                    </p>
                  )}
                  <pre className="max-h-40 overflow-auto whitespace-pre-wrap rounded-md bg-red-50 p-3 text-xs text-red-900">
                    {data.item.lastError}
                  </pre>
                </Section>
              )}

              {/* Ráfaga de esta cola */}
              <Section
                title={`Ráfaga recibida (${data.burst.length} ${data.burst.length === 1 ? 'mensaje' : 'mensajes'})`}
                icon={<MessageSquare className="h-4 w-4 text-blue-500" />}
              >
                <div className="space-y-2">
                  {data.burst.length === 0 && <p className="text-sm text-gray-500">Sin contenido en MessagesJson.</p>}
                  {data.burst.map((m, i) => (
                    <div key={i} className="rounded-md border border-blue-200 bg-blue-50 p-3 text-sm">
                      <div className="mb-1 flex items-center justify-between text-xs text-blue-700">
                        <span>{fmtPanama(m.ReceivedAt)}</span>
                        {m.ExternalId && <span className="font-mono">{m.ExternalId}</span>}
                      </div>
                      <p className="whitespace-pre-wrap text-blue-900">{m.Content || <em>(vacío)</em>}</p>
                      {m.MediaUrl && (
                        <p className="mt-1 text-xs text-blue-700">
                          📎 {m.MediaType ?? 'media'}:{' '}
                          <a href={m.MediaUrl} target="_blank" rel="noreferrer" className="underline">{m.MediaUrl}</a>
                        </p>
                      )}
                    </div>
                  ))}
                </div>
              </Section>

              {/* Conversación */}
              {data.conversation ? (
                <Section title="Conversación completa" icon={<MessageSquare className="h-4 w-4 text-emerald-500" />}>
                  <p className="mb-2 text-xs text-gray-500">
                    Conversación{' '}
                    <span className="font-mono">{data.conversation.id.slice(0, 8)}…</span>
                    {' '}— estado{' '}
                    <code className="rounded bg-gray-100 px-1 py-0.5">{data.conversation.status}</code>
                    {data.conversation.isHumanHandled && <span className="ml-1 text-amber-700">· tomada por humano</span>}
                  </p>
                  <div className="space-y-2">
                    {data.messages.length === 0 && <p className="text-sm text-gray-500">Sin mensajes registrados.</p>}
                    {data.messages.map((m) => {
                      const isOut = m.direction === 'Outbound'
                      return (
                        <div
                          key={m.id}
                          className={`flex ${isOut ? 'justify-end' : 'justify-start'}`}
                        >
                          <div
                            className={`max-w-[80%] rounded-2xl px-4 py-2 text-sm ${
                              isOut
                                ? 'bg-emerald-50 text-emerald-900'
                                : 'bg-gray-100 text-gray-900'
                            }`}
                          >
                            <div className="mb-1 flex items-center gap-1.5 text-xs opacity-70">
                              {isOut ? <Bot className="h-3 w-3" /> : <User className="h-3 w-3" />}
                              <span>{isOut ? (m.agentName ?? 'Agente') : 'Cliente'}</span>
                              <span>·</span>
                              <span className="font-mono">{fmtPanama(m.sentAt)}</span>
                              {m.detectedIntent && (
                                <>
                                  <span>·</span>
                                  <code className="rounded bg-white/60 px-1">{m.detectedIntent}</code>
                                </>
                              )}
                            </div>
                            <p className="whitespace-pre-wrap">{m.content}</p>
                          </div>
                        </div>
                      )
                    })}
                  </div>
                </Section>
              ) : (
                <Section title="Conversación" icon={<MessageSquare className="h-4 w-4 text-gray-400" />}>
                  <p className="text-sm text-gray-500">
                    Aún no existe una conversación asociada a este teléfono y tenant.
                  </p>
                </Section>
              )}

              {/* Eventos de gestión */}
              {data.gestionEvents.length > 0 && (
                <Section title="Eventos de gestión" icon={<AlertCircle className="h-4 w-4 text-amber-500" />}>
                  <ul className="space-y-2 text-sm">
                    {data.gestionEvents.map((g) => (
                      <li key={g.id} className="rounded-md border border-amber-200 bg-amber-50 p-3">
                        <div className="mb-1 flex items-center justify-between text-xs text-amber-700">
                          <span>{fmtPanama(g.occurredAt)}</span>
                          <code className="rounded bg-amber-100 px-1.5 py-0.5">{g.origin}</code>
                        </div>
                        <p className="text-amber-900"><strong>{g.result}</strong>{g.notes && ` — ${g.notes}`}</p>
                      </li>
                    ))}
                  </ul>
                </Section>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

function Section({ title, icon, children }: { title: string; icon: React.ReactNode; children: React.ReactNode }) {
  return (
    <section>
      <h3 className="mb-3 flex items-center gap-2 text-sm font-semibold text-gray-700">
        {icon}
        {title}
      </h3>
      {children}
    </section>
  )
}

function Info({ label, value, mono = false }: { label: string; value: React.ReactNode; mono?: boolean }) {
  // min-w-0 en el grid item permite que el dd se achique al ancho de la columna
  // (sin esto el font-mono largo desborda horizontalmente y se monta con el
  // contenido de la siguiente columna). break-all rompe en cualquier carácter
  // para IDs sin espacios.
  return (
    <div className="min-w-0">
      <dt className="text-xs text-gray-500">{label}</dt>
      <dd className={`text-gray-900 ${mono ? 'font-mono text-xs break-all' : 'break-words'}`}>{value}</dd>
    </div>
  )
}

function StatusPill({ status }: { status: string }) {
  const palette: Record<string, string> = {
    Pending:    'bg-blue-50 text-blue-700',
    Claimed:    'bg-indigo-50 text-indigo-700',
    Processing: 'bg-violet-50 text-violet-700',
    Replied:    'bg-emerald-50 text-emerald-700',
    Failed:     'bg-red-50 text-red-700',
    Escalated:  'bg-amber-50 text-amber-800',
  }
  const cls = palette[status] ?? 'bg-gray-50 text-gray-700'
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${cls}`}>
      {STATUS_LABEL[status] ?? status}
    </span>
  )
}
