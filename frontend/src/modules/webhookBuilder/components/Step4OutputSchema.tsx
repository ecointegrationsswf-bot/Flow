import { Plus, Trash2 } from 'lucide-react'
import type { OutputField, OutputSchema, WebhookContractBundle, DetectedFieldDto } from '../types'

interface Props {
  bundle: WebhookContractBundle
  detectedFields: DetectedFieldDto[]
  onChange: (outputSchema: OutputSchema) => void
}

const DEFAULT_FIELD: OutputField = {
  fieldPath: '',
  dataType: 'string',
  outputAction: 'send_to_agent',
  label: '',
  required: true,
}

export function Step4OutputSchema({ bundle, detectedFields, onChange }: Props) {
  const schema = bundle.outputSchema ?? { fields: [] }

  const updateFields = (fields: OutputField[]) => {
    onChange({ fields })
  }

  const addField = () => {
    updateFields([...schema.fields, { ...DEFAULT_FIELD }])
  }

  const removeField = (idx: number) => {
    updateFields(schema.fields.filter((_, i) => i !== idx))
  }

  const updateField = (idx: number, patch: Partial<OutputField>) => {
    const next = schema.fields.map((f, i) => (i === idx ? { ...f, ...patch } : f))
    updateFields(next)
  }

  const prefillFromDetected = () => {
    const detected: OutputField[] = detectedFields.map((d) => ({
      fieldPath: d.fieldPath,
      dataType: (d.dataType as OutputField['dataType']) ?? 'string',
      outputAction: 'send_to_agent',
      label: d.fieldPath,
      required: false,
    }))
    updateFields(detected)
  }

  return (
    <div className="space-y-4">
      <div>
        <h3 className="text-sm font-semibold text-gray-900 mb-1">Paso 4 — Output Schema</h3>
        <p className="text-xs text-gray-500">
          Define cómo procesar cada campo de la respuesta del webhook. El sistema ejecutará
          la acción (outputAction) declarada para cada campo.
        </p>
      </div>

      {detectedFields.length > 0 && schema.fields.length === 0 && (
        <button
          type="button"
          onClick={prefillFromDetected}
          className="w-full rounded-lg bg-blue-50 border border-blue-200 px-4 py-2 text-xs font-medium text-blue-700 hover:bg-blue-100"
        >
          Pre-llenar con los {detectedFields.length} campos detectados en la prueba
        </button>
      )}

      <div className="space-y-2">
        {schema.fields.length === 0 && (
          <div className="rounded border border-dashed border-gray-300 p-6 text-center text-xs text-gray-500">
            No hay campos aún. {detectedFields.length > 0 ? 'Usa el botón de pre-llenado o agrega campos manualmente.' : 'Ejecuta el paso 2 primero para auto-detectar campos.'}
          </div>
        )}

        {schema.fields.map((field, idx) => (
          <div key={idx} className="rounded-lg border border-gray-200 bg-gray-50 p-3 space-y-2">
            <div className="flex items-center gap-2">
              <input
                type="text"
                value={field.fieldPath}
                onChange={(e) => updateField(idx, { fieldPath: e.target.value })}
                placeholder="fieldPath (ej: link_pago)"
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

            <div>
              <label className="block text-[10px] font-medium text-gray-600 mb-0.5">
                Label (texto legible para el agente)
              </label>
              <input
                type="text"
                value={field.label}
                onChange={(e) => updateField(idx, { label: e.target.value })}
                placeholder="Link de pago"
                className="w-full rounded border border-gray-300 px-2 py-1 text-xs"
              />
            </div>

            <div className="grid grid-cols-2 gap-2">
              <div>
                <label className="block text-[10px] font-medium text-gray-600 mb-0.5">Tipo de dato</label>
                <select
                  value={field.dataType}
                  onChange={(e) => updateField(idx, { dataType: e.target.value as OutputField['dataType'] })}
                  className="w-full rounded border border-gray-300 px-2 py-1 text-xs"
                >
                  <option value="string">string</option>
                  <option value="number">number</option>
                  <option value="boolean">boolean</option>
                  <option value="date">date</option>
                  <option value="url">url</option>
                  <option value="base64">base64</option>
                  <option value="array">array</option>
                  <option value="object">object</option>
                </select>
              </div>

              <div>
                <label className="block text-[10px] font-medium text-gray-600 mb-0.5">Acción</label>
                <select
                  value={field.outputAction}
                  onChange={(e) => updateField(idx, { outputAction: e.target.value as OutputField['outputAction'] })}
                  className="w-full rounded border border-gray-300 px-2 py-1 text-xs"
                >
                  <option value="send_to_agent">send_to_agent</option>
                  <option value="inject_context">inject_context</option>
                  <option value="log_only">log_only</option>
                  <option value="trigger_escalation">trigger_escalation</option>
                  <option value="send_whatsapp_media">send_whatsapp_media</option>
                </select>
              </div>
            </div>

            {field.dataType === 'base64' && (
              <div>
                <label className="block text-[10px] font-medium text-gray-600 mb-0.5">MIME type (requerido)</label>
                <select
                  value={field.mimeType ?? ''}
                  onChange={(e) => updateField(idx, { mimeType: e.target.value })}
                  className="w-full rounded border border-gray-300 px-2 py-1 text-xs"
                >
                  <option value="">— seleccionar —</option>
                  <option value="application/pdf">application/pdf</option>
                  <option value="image/jpeg">image/jpeg</option>
                  <option value="image/png">image/png</option>
                  <option value="audio/mpeg">audio/mpeg</option>
                </select>
              </div>
            )}

            <label className="flex items-center gap-1 text-[10px] text-gray-600">
              <input
                type="checkbox"
                checked={field.required}
                onChange={(e) => updateField(idx, { required: e.target.checked })}
              />
              Required (log warning si falta en la respuesta)
            </label>
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
