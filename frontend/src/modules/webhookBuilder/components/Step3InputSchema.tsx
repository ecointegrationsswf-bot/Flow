import { Plus, Trash2 } from 'lucide-react'
import type { InputField, InputSchema, WebhookContractBundle } from '../types'
import { SYSTEM_SOURCE_KEYS } from '../types'

interface Props {
  bundle: WebhookContractBundle
  onChange: (inputSchema: InputSchema) => void
}

const DEFAULT_FIELD: InputField = {
  fieldPath: '',
  sourceType: 'system',
  sourceKey: '',
  dataType: 'string',
  required: true,
}

export function Step3InputSchema({ bundle, onChange }: Props) {
  const schema = bundle.inputSchema ?? {
    contentType: bundle.contentType,
    httpMethod: bundle.webhookMethod,
    structure: bundle.structure,
    fields: [],
  }

  const updateFields = (fields: InputField[]) => {
    onChange({ ...schema, fields })
  }

  const addField = () => {
    updateFields([...schema.fields, { ...DEFAULT_FIELD }])
  }

  const removeField = (idx: number) => {
    updateFields(schema.fields.filter((_, i) => i !== idx))
  }

  const updateField = (idx: number, patch: Partial<InputField>) => {
    const next = schema.fields.map((f, i) => (i === idx ? { ...f, ...patch } : f))
    updateFields(next)
  }

  return (
    <div className="space-y-4">
      <div>
        <h3 className="text-sm font-semibold text-gray-900 mb-1">Paso 3 — Input Schema</h3>
        <p className="text-xs text-gray-500">
          Define qué campos se envían al webhook. Cada campo tiene un destino (fieldPath)
          y una fuente (sistema, chat o valor estático).
        </p>
      </div>

      <div className="space-y-2">
        {schema.fields.length === 0 && (
          <div className="rounded border border-dashed border-gray-300 p-6 text-center text-xs text-gray-500">
            No hay campos aún. Agrega uno para empezar a construir el payload.
          </div>
        )}

        {schema.fields.map((field, idx) => (
          <div key={idx} className="rounded-lg border border-gray-200 bg-gray-50 p-3 space-y-2">
            <div className="flex items-center gap-2">
              <input
                type="text"
                value={field.fieldPath}
                onChange={(e) => updateField(idx, { fieldPath: e.target.value })}
                placeholder="Nombre destino (ej: cliente.cedula)"
                className="flex-1 rounded border border-gray-300 px-2 py-1.5 text-xs font-mono"
              />
              <button
                type="button"
                onClick={() => removeField(idx)}
                className="rounded p-1 text-red-500 hover:bg-red-50"
              >
                <Trash2 className="h-4 w-4" />
              </button>
            </div>

            <div className="grid grid-cols-3 gap-2">
              <div>
                <label className="block text-[10px] font-medium text-gray-600 mb-0.5">Fuente</label>
                <select
                  value={field.sourceType}
                  onChange={(e) => updateField(idx, { sourceType: e.target.value as InputField['sourceType'] })}
                  className="w-full rounded border border-gray-300 px-2 py-1 text-xs"
                >
                  <option value="system">Sistema</option>
                  <option value="labelingResult">Resultado etiquetado</option>
                  <option value="conversation">Chat (futuro)</option>
                  <option value="static">Estático</option>
                </select>
              </div>

              <div className="col-span-2">
                {field.sourceType === 'system' && (
                  <>
                    <label className="block text-[10px] font-medium text-gray-600 mb-0.5">sourceKey</label>
                    <select
                      value={field.sourceKey ?? ''}
                      onChange={(e) => updateField(idx, { sourceKey: e.target.value })}
                      className="w-full rounded border border-gray-300 px-2 py-1 text-xs font-mono"
                    >
                      <option value="">— seleccionar —</option>
                      {Object.entries(
                        SYSTEM_SOURCE_KEYS.reduce<Record<string, typeof SYSTEM_SOURCE_KEYS>>((acc, k) => {
                          acc[k.group] = [...(acc[k.group] ?? []), k]
                          return acc
                        }, {})
                      ).map(([group, keys]) => (
                        <optgroup key={group} label={group}>
                          {keys.map((k) => (
                            <option key={k.key} value={k.key}>
                              {k.key} — {k.label}
                            </option>
                          ))}
                        </optgroup>
                      ))}
                    </select>
                  </>
                )}

                {field.sourceType === 'labelingResult' && (
                  <>
                    <label className="block text-[10px] font-medium text-gray-600 mb-0.5">
                      Campo del resultado del etiquetado (definido en tab "Etiquetado" del tenant)
                    </label>
                    <input
                      type="text"
                      value={field.sourceKey ?? ''}
                      onChange={(e) => updateField(idx, { sourceKey: e.target.value })}
                      placeholder="comentario, fechaPago, montoPagar..."
                      className="w-full rounded border border-gray-300 px-2 py-1 text-xs font-mono"
                    />
                  </>
                )}

                {field.sourceType === 'conversation' && (
                  <>
                    <label className="block text-[10px] font-medium text-gray-600 mb-0.5">
                      Nombre del parámetro (collected) — requiere Fase 6
                    </label>
                    <input
                      type="text"
                      value={field.sourceKey ?? ''}
                      onChange={(e) => updateField(idx, { sourceKey: e.target.value })}
                      placeholder="amount"
                      className="w-full rounded border border-gray-300 px-2 py-1 text-xs font-mono"
                    />
                  </>
                )}

                {field.sourceType === 'static' && (
                  <>
                    <label className="block text-[10px] font-medium text-gray-600 mb-0.5">Valor estático</label>
                    <input
                      type="text"
                      value={field.staticValue ?? ''}
                      onChange={(e) => updateField(idx, { staticValue: e.target.value })}
                      placeholder="valor fijo"
                      className="w-full rounded border border-gray-300 px-2 py-1 text-xs"
                    />
                  </>
                )}
              </div>
            </div>

            <div className="flex items-center gap-3">
              <div className="flex items-center gap-1">
                <label className="text-[10px] font-medium text-gray-600">Tipo:</label>
                <select
                  value={field.dataType}
                  onChange={(e) => updateField(idx, { dataType: e.target.value as InputField['dataType'] })}
                  className="rounded border border-gray-300 px-1.5 py-0.5 text-xs"
                >
                  <option value="string">string</option>
                  <option value="number">number</option>
                  <option value="boolean">boolean</option>
                  <option value="date">date</option>
                  <option value="array">array</option>
                </select>
              </div>

              <label className="flex items-center gap-1 text-[10px] text-gray-600">
                <input
                  type="checkbox"
                  checked={field.required}
                  onChange={(e) => updateField(idx, { required: e.target.checked })}
                />
                Required
              </label>
            </div>
          </div>
        ))}

        <button
          type="button"
          onClick={addField}
          className="flex w-full items-center justify-center gap-1 rounded-lg border-2 border-dashed border-gray-300 py-2 text-xs font-medium text-gray-600 hover:border-blue-400 hover:text-blue-600"
        >
          <Plus className="h-3.5 w-3.5" />
          Agregar campo
        </button>
      </div>
    </div>
  )
}
