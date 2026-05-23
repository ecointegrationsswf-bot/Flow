import { Plus, Trash2, Link2 } from 'lucide-react'
import type { ChainRule, WebhookContractBundle } from '../types'

interface Props {
  bundle: WebhookContractBundle
  /** Lista de otras acciones del tenant con webhook contract — para el dropdown del target. */
  availableSlugs: string[]
  onChange: (chainRules: ChainRule[]) => void
  /** Modo solo-consulta (vista tenant) — oculta los botones de añadir/eliminar. */
  readOnly?: boolean
}

/**
 * Paso 5 del Webhook Builder — Encadenamiento automático.
 *
 * Permite al super admin declarar que tras ejecutar esta acción, si la respuesta
 * del endpoint cumple cierta condición, el sistema debe disparar inmediatamente
 * otra acción del tenant — sin pasar por el LLM.
 *
 * Ejemplo: INSURED_INITIATE devuelve `status=CODIGO_GENERADO` ⇒ ejecutar
 * `SEND_2FA_CODE_EMAIL` automáticamente. El LLM solo redacta una vez al final
 * con todo el contexto resuelto.
 *
 * MVP: operador `equals`. Sin nesting (path con dot-notation simple).
 */
export function Step5Chaining({ bundle, availableSlugs, onChange, readOnly = false }: Props) {
  const rules = bundle.chainRules ?? []

  const update = (next: ChainRule[]) => onChange(next)

  const addRule = () => {
    update([
      ...rules,
      { when: { path: '', operator: 'equals', value: '' }, then: { actionSlug: '' } },
    ])
  }

  const removeRule = (idx: number) => {
    update(rules.filter((_, i) => i !== idx))
  }

  const updateRule = (idx: number, patch: Partial<ChainRule>) => {
    update(rules.map((r, i) => (i === idx ? { ...r, ...patch } : r)))
  }

  // sourceKey suggestions: las dejamos abiertas como input de texto porque el path
  // se resuelve sobre el JSON crudo del endpoint, no sobre el outputSchema parseado.
  // Esto permite encadenar incluso sin haber configurado outputSchema.

  return (
    <div className="space-y-4">
      <div>
        <h3 className="text-sm font-semibold text-gray-900 mb-1">Paso 5 — Encadenamiento automático</h3>
        <p className="text-xs text-gray-500">
          Si la respuesta de esta acción cumple una condición, el sistema disparará
          inmediatamente otra acción <strong>sin pasar por el agente IA</strong>. Útil para
          transiciones determinísticas (ej: tras un código generado, enviar el email).
        </p>
        <p className="mt-2 text-[11px] text-gray-400">
          Se evalúan en orden. La <strong>primera regla que matchee</strong> gana. Máximo 3
          eslabones por turno; los ciclos se cortan automáticamente.
        </p>
      </div>

      <div className="space-y-3">
        {rules.length === 0 && (
          <div className="rounded border border-dashed border-gray-300 p-6 text-center text-xs text-gray-500">
            Sin reglas configuradas. La acción no encadenará nada — el agente IA decide
            qué hacer con la respuesta.
          </div>
        )}

        {rules.map((rule, idx) => (
          <div key={idx} className="rounded-lg border border-purple-200 bg-purple-50/40 p-3 space-y-2">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-1.5 text-xs font-medium text-purple-700">
                <Link2 className="h-3.5 w-3.5" />
                Regla #{idx + 1}
              </div>
              {!readOnly && (
                <button
                  type="button"
                  onClick={() => removeRule(idx)}
                  className="rounded p-1 text-red-500 hover:bg-red-50"
                  title="Eliminar regla"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              )}
            </div>

            <div className="rounded bg-white border border-gray-200 p-2.5 space-y-2">
              <p className="text-[10px] font-semibold text-gray-600">CUANDO LA RESPUESTA CUMPLA:</p>
              <div className="grid grid-cols-12 gap-2">
                <div className="col-span-5">
                  <label className="block text-[10px] text-gray-500 mb-0.5">Campo (path)</label>
                  <input
                    type="text"
                    value={rule.when.path}
                    onChange={(e) => updateRule(idx, { when: { ...rule.when, path: e.target.value } })}
                    placeholder="status, data.code, ..."
                    className="w-full rounded border border-gray-300 px-2 py-1 text-xs font-mono"
                  />
                </div>
                <div className="col-span-3">
                  <label className="block text-[10px] text-gray-500 mb-0.5">Operador</label>
                  <select
                    value={rule.when.operator}
                    onChange={(e) =>
                      updateRule(idx, { when: { ...rule.when, operator: e.target.value as 'equals' } })
                    }
                    className="w-full rounded border border-gray-300 px-2 py-1 text-xs"
                  >
                    <option value="equals">igual a</option>
                  </select>
                </div>
                <div className="col-span-4">
                  <label className="block text-[10px] text-gray-500 mb-0.5">Valor</label>
                  <input
                    type="text"
                    value={rule.when.value ?? ''}
                    onChange={(e) => updateRule(idx, { when: { ...rule.when, value: e.target.value } })}
                    placeholder="CODIGO_GENERADO"
                    className="w-full rounded border border-gray-300 px-2 py-1 text-xs"
                  />
                </div>
              </div>
            </div>

            <div className="rounded bg-white border border-gray-200 p-2.5 space-y-2">
              <p className="text-[10px] font-semibold text-gray-600">ENTONCES EJECUTAR:</p>
              <select
                value={rule.then?.actionSlug ?? ''}
                onChange={(e) => {
                  const slug = e.target.value
                  updateRule(idx, { then: slug ? { actionSlug: slug } : null })
                }}
                className="w-full rounded border border-gray-300 px-2 py-1 text-xs font-mono"
              >
                <option value="">— ninguna (rama terminal documentada) —</option>
                {availableSlugs.map((slug) => (
                  <option key={slug} value={slug}>
                    {slug}
                  </option>
                ))}
              </select>
              {rule.then?.actionSlug && !availableSlugs.includes(rule.then.actionSlug) && (
                <p className="text-[10px] text-amber-700">
                  ⚠ La acción <strong>{rule.then.actionSlug}</strong> no aparece en la lista de acciones
                  asignadas al tenant. Verifica que esté asignada o el chain no se ejecutará.
                </p>
              )}
            </div>
          </div>
        ))}

        {!readOnly && (
          <button
            type="button"
            onClick={addRule}
            className="flex w-full items-center justify-center gap-1 rounded-lg border-2 border-dashed border-purple-300 py-2 text-xs font-medium text-purple-600 hover:border-purple-400 hover:text-purple-700"
          >
            <Plus className="h-3.5 w-3.5" />
            Agregar regla de encadenamiento
          </button>
        )}
      </div>
    </div>
  )
}
