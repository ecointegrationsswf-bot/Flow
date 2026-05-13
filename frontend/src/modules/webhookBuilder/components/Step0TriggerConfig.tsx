import { useState, useMemo } from 'react'
import { X as XIcon, AlertCircle, Info } from 'lucide-react'
import type { TriggerConfig, WebhookContractBundle } from '../types'

interface Props {
  bundle: WebhookContractBundle
  onChange: (bundle: WebhookContractBundle) => void
  /** Slug de la acción — se usa en el preview del system prompt. */
  actionName: string
}

const MIN_DESCRIPTION = 20
const MIN_EXAMPLES = 3
const MAX_EXAMPLES = 7

export function Step0TriggerConfig({ bundle, onChange, actionName }: Props) {
  const tc: TriggerConfig = bundle.triggerConfig ?? {}
  const [exampleDraft, setExampleDraft] = useState('')

  const updateTrigger = (patch: Partial<TriggerConfig>) => {
    onChange({
      ...bundle,
      triggerConfig: { ...tc, ...patch },
    })
  }

  // Campos elegibles para "Campos a confirmar": los del InputSchema cuyo sourceType
  // es 'conversation' (los que el agente debe recolectar del cliente).
  const eligibleFields = useMemo(
    () => (bundle.inputSchema?.fields ?? [])
      .filter(f => f.sourceType === 'conversation' && f.fieldPath.length > 0)
      .map(f => f.fieldPath),
    [bundle.inputSchema],
  )

  const addExample = () => {
    const v = exampleDraft.trim()
    if (!v) return
    const current = tc.triggerExamples ?? []
    if (current.length >= MAX_EXAMPLES) return
    if (current.includes(v)) { setExampleDraft(''); return }
    updateTrigger({ triggerExamples: [...current, v] })
    setExampleDraft('')
  }

  const removeExample = (idx: number) => {
    const current = tc.triggerExamples ?? []
    updateTrigger({ triggerExamples: current.filter((_, i) => i !== idx) })
  }

  const toggleRequiresField = (field: string) => {
    const current = tc.requiresConfirmation ?? []
    const next = current.includes(field)
      ? current.filter(f => f !== field)
      : [...current, field]
    updateTrigger({ requiresConfirmation: next.length > 0 ? next : undefined })
  }

  // ── Validaciones visuales (no bloquean el paso — son sugerencias) ──
  const descLen = (tc.description ?? '').trim().length
  const descTooShort = descLen > 0 && descLen < MIN_DESCRIPTION
  const examplesCount = tc.triggerExamples?.length ?? 0
  const examplesTooFew = examplesCount > 0 && examplesCount < MIN_EXAMPLES
  const needsClarification = (tc.requiresConfirmation?.length ?? 0) > 0
                             && (tc.clarificationPrompt ?? '').trim().length === 0

  // ── Preview del bloque que se inyectará en el system prompt ──
  const preview = useMemo(() => {
    if (!tc.description || tc.description.trim().length === 0) return null
    const lines: string[] = []
    lines.push(`### [${actionName || 'NOMBRE_ACCION'}]`)
    lines.push(`Cuándo usar: ${tc.description.trim()}`)
    if ((tc.triggerExamples?.length ?? 0) > 0) {
      lines.push('Ejemplos de frases del usuario:')
      for (const ex of tc.triggerExamples!) {
        lines.push(`  - "${ex}"`)
      }
    }
    const required = tc.requiresConfirmation ?? []
    if (required.length > 0) {
      lines.push(`Debes confirmar antes de ejecutar: ${required.join(', ')}`)
      if (tc.clarificationPrompt && tc.clarificationPrompt.trim().length > 0) {
        lines.push(`Pregunta sugerida: "${tc.clarificationPrompt.trim()}"`)
      }
    } else {
      lines.push('Puedes ejecutar de inmediato cuando detectes la intención.')
    }
    lines.push('Para ejecutar esta acción declara al final de tu respuesta:')
    lines.push(`  [ACTION:${actionName || 'NOMBRE_ACCION'}]`)
    for (const f of required) {
      lines.push(`  [PARAM:${f}=<valor confirmado>]`)
    }
    return lines.join('\n')
  }, [actionName, tc.description, tc.triggerExamples, tc.requiresConfirmation, tc.clarificationPrompt])

  return (
    <div className="space-y-4">
      <div>
        <h3 className="text-sm font-semibold text-gray-900 mb-1">Paso 0 — Trigger del agente</h3>
        <p className="text-xs text-gray-500">
          Define cuándo el agente IA debe disparar esta acción durante una conversación.
          Los campos siguientes se inyectan al system prompt del agente bajo
          "ACCIONES DISPONIBLES" — lo que escribas aquí es literalmente lo que leerá el agente.
        </p>
        <div className="mt-2 flex items-start gap-1.5 rounded-lg bg-amber-50 border border-amber-200 px-3 py-2 text-[11px] text-amber-900">
          <Info className="h-3.5 w-3.5 mt-0.5 flex-shrink-0" />
          <span>
            Este paso es <strong>opcional</strong>. Si lo dejas vacío, la acción seguirá
            ejecutándose si alguien la dispara externamente (ej: via Cerebro), pero el
            agente IA no la usará por su cuenta.
          </span>
        </div>
      </div>

      <div className="grid grid-cols-2 gap-4">
        {/* ── Columna izquierda: formulario ── */}
        <div className="space-y-4">
          {/* Descripción */}
          <div>
            <label className="block text-xs font-medium text-gray-700 mb-1">
              Descripción <span className="text-red-500">*</span>
            </label>
            <textarea
              rows={3}
              value={tc.description ?? ''}
              onChange={(e) => updateTrigger({ description: e.target.value })}
              placeholder="Usar cuando el cliente confirma explícitamente que quiere realizar un pago."
              className="w-full rounded border border-gray-300 px-3 py-2 text-xs"
            />
            <div className="mt-1 flex items-center justify-between text-[10px]">
              <span className={descTooShort ? 'text-amber-600' : 'text-gray-400'}>
                {descTooShort ? `Mínimo ${MIN_DESCRIPTION} caracteres recomendados` : `${descLen} caracteres`}
              </span>
            </div>
          </div>

          {/* Ejemplos */}
          <div>
            <label className="block text-xs font-medium text-gray-700 mb-1">
              Ejemplos de frases del usuario
              <span className="text-[10px] text-gray-400 font-normal ml-1">
                ({examplesCount}/{MAX_EXAMPLES} · recomendado {MIN_EXAMPLES}-{MAX_EXAMPLES})
              </span>
            </label>
            <div className="flex gap-2">
              <input
                type="text"
                value={exampleDraft}
                onChange={(e) => setExampleDraft(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); addExample() } }}
                placeholder="quiero pagar"
                disabled={examplesCount >= MAX_EXAMPLES}
                className="flex-1 rounded border border-gray-300 px-3 py-1.5 text-xs disabled:bg-gray-50"
              />
              <button
                type="button"
                onClick={addExample}
                disabled={examplesCount >= MAX_EXAMPLES || exampleDraft.trim().length === 0}
                className="rounded bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700 disabled:opacity-40"
              >
                Agregar
              </button>
            </div>
            <div className="mt-2 flex flex-wrap gap-1.5">
              {(tc.triggerExamples ?? []).map((ex, i) => (
                <span
                  key={i}
                  className="inline-flex items-center gap-1 rounded-full bg-blue-50 border border-blue-200 px-2 py-0.5 text-[11px] text-blue-900"
                >
                  "{ex}"
                  <button
                    type="button"
                    onClick={() => removeExample(i)}
                    className="text-blue-400 hover:text-blue-700"
                    aria-label={`Eliminar ${ex}`}
                  >
                    <XIcon className="h-2.5 w-2.5" />
                  </button>
                </span>
              ))}
              {examplesCount === 0 && (
                <span className="text-[10px] text-gray-400 italic">Enter para agregar</span>
              )}
            </div>
            {examplesTooFew && (
              <div className="mt-1 flex items-center gap-1 text-[10px] text-amber-600">
                <AlertCircle className="h-3 w-3" />
                Mínimo {MIN_EXAMPLES} ejemplos recomendados para mejor detección
              </div>
            )}
          </div>

          {/* Campos a confirmar */}
          <div>
            <label className="block text-xs font-medium text-gray-700 mb-1">
              Campos a confirmar antes de ejecutar
            </label>
            {eligibleFields.length === 0 ? (
              <div className="rounded border border-dashed border-gray-300 bg-gray-50 px-3 py-2 text-[11px] text-gray-500">
                No hay campos <code className="font-mono">sourceType=conversation</code> en el Input Schema.
                Completa el <strong>Paso 3</strong> con campos del tipo "Conversación"
                si esta acción necesita datos que el agente debe pedir al cliente.
              </div>
            ) : (
              <div className="flex flex-wrap gap-1.5">
                {eligibleFields.map(field => {
                  const checked = (tc.requiresConfirmation ?? []).includes(field)
                  return (
                    <button
                      key={field}
                      type="button"
                      onClick={() => toggleRequiresField(field)}
                      className={`rounded-full border px-2.5 py-1 text-[11px] font-medium transition ${
                        checked
                          ? 'bg-purple-600 border-purple-600 text-white'
                          : 'bg-white border-gray-300 text-gray-700 hover:border-purple-400'
                      }`}
                    >
                      {field}
                    </button>
                  )
                })}
              </div>
            )}
          </div>

          {/* Pregunta sugerida */}
          {needsClarification !== null && (tc.requiresConfirmation?.length ?? 0) > 0 && (
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">
                Pregunta de aclaración <span className="text-red-500">*</span>
              </label>
              <input
                type="text"
                value={tc.clarificationPrompt ?? ''}
                onChange={(e) => updateTrigger({ clarificationPrompt: e.target.value })}
                placeholder="¿Cuál es el monto que deseas pagar?"
                className="w-full rounded border border-gray-300 px-3 py-2 text-xs"
              />
              {needsClarification && (
                <div className="mt-1 flex items-center gap-1 text-[10px] text-amber-600">
                  <AlertCircle className="h-3 w-3" />
                  Define una pregunta para que el agente sepa cómo pedir los campos faltantes
                </div>
              )}
              <p className="mt-1 text-[10px] text-gray-400">
                El agente puede reformularla según el contexto. Esta es la base.
              </p>
            </div>
          )}
        </div>

        {/* ── Columna derecha: preview en vivo ── */}
        <div>
          <label className="block text-xs font-medium text-gray-700 mb-1">
            Preview del system prompt
          </label>
          <div className="rounded-lg border border-gray-300 bg-gray-900 text-gray-100 p-3 min-h-[280px] text-[11px] font-mono whitespace-pre-wrap leading-relaxed">
            {preview ?? (
              <span className="text-gray-500 italic">
                Completa la descripción para ver el bloque que leerá el agente.
              </span>
            )}
          </div>
          <p className="mt-1 text-[10px] text-gray-400">
            Así lo lee el agente literalmente en cada turno de esta campaña.
          </p>
        </div>
      </div>
    </div>
  )
}
