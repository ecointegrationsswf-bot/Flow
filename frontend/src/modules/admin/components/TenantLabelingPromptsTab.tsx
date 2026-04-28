import { useEffect, useState } from 'react'
import { Loader2, Save, RotateCcw } from 'lucide-react'
import {
  useAdminTenantLabelingPrompts,
  useUpdateAdminTenantLabelingPrompts,
} from '@/modules/admin/hooks/useAdminTenantLabelingPrompts'

interface Props {
  tenantId: string
}

const RESULT_SCHEMA_PLACEHOLDER = `Ejemplo:
{
  "comentario": "Resumen claro y breve de la conversación, una sola frase.",
  "fechaPago": "Fecha de compromiso de pago en formato yyyy-MM-dd, null si no aplica.",
  "montoPagar": "Monto numérico que el cliente acordó pagar (0 si no aplica)."
}`

const ANALYSIS_PLACEHOLDER = `Eres un clasificador de conversaciones...
(Si lo dejas vacío se usa el prompt por defecto del sistema.)`

/**
 * Tab de configuración de prompts del proceso de etiquetado por tenant.
 *
 * - analysisPrompt: system prompt que recibe el LLM al clasificar (override
 *   opcional del default cableado en ConversationLabelingJob).
 * - resultSchemaPrompt: describe los campos JSON adicionales que el LLM debe
 *   producir para cada conversación. El resultado se guarda en
 *   Conversation.LabelingResultJson y los webhooks pueden mapearlo via
 *   sourceType=labelingResult (ej: result.comentario, result.fechaPago).
 */
export function TenantLabelingPromptsTab({ tenantId }: Props) {
  const { data, isLoading } = useAdminTenantLabelingPrompts(tenantId)
  const update = useUpdateAdminTenantLabelingPrompts(tenantId)
  const [analysis, setAnalysis] = useState('')
  const [schema, setSchema] = useState('')
  const [savedAt, setSavedAt] = useState<number | null>(null)

  useEffect(() => {
    if (!data) return
    setAnalysis(data.analysisPrompt ?? '')
    setSchema(data.resultSchemaPrompt ?? '')
  }, [data])

  const dirty =
    (data?.analysisPrompt ?? '') !== analysis ||
    (data?.resultSchemaPrompt ?? '') !== schema

  const handleSave = () =>
    update.mutate(
      { analysisPrompt: analysis, resultSchemaPrompt: schema },
      { onSuccess: () => setSavedAt(Date.now()) },
    )

  const handleReset = () => {
    setAnalysis(data?.analysisPrompt ?? '')
    setSchema(data?.resultSchemaPrompt ?? '')
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="h-6 w-6 animate-spin text-gray-400" />
      </div>
    )
  }

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <div className="flex-1 overflow-y-auto px-6 py-4 space-y-6">
        <div>
          <h3 className="text-sm font-semibold text-gray-900">Prompt de Análisis</h3>
          <p className="mt-1 text-xs text-gray-500">
            Reemplaza el system prompt por defecto que el LLM usa al clasificar las conversaciones del tenant.
            Si lo dejas vacío se usa el prompt cableado del sistema.
          </p>
          <textarea
            value={analysis}
            onChange={(e) => setAnalysis(e.target.value)}
            placeholder={ANALYSIS_PLACEHOLDER}
            rows={10}
            className="mt-2 block w-full rounded-md border border-gray-300 px-3 py-2 text-xs font-mono leading-relaxed focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          />
        </div>

        <div>
          <h3 className="text-sm font-semibold text-gray-900">Schema de Resultado</h3>
          <p className="mt-1 text-xs text-gray-500">
            Describe el JSON adicional que el LLM debe extraer de cada conversación etiquetada.
            Cada campo declarado queda disponible en el Webhook Builder como{' '}
            <code className="rounded bg-gray-100 px-1 py-0.5 font-mono text-[10px]">result.&lt;campo&gt;</code>{' '}
            (ej: <code className="font-mono text-[10px]">result.comentario</code>,{' '}
            <code className="font-mono text-[10px]">result.fechaPago</code>).
          </p>
          <textarea
            value={schema}
            onChange={(e) => setSchema(e.target.value)}
            placeholder={RESULT_SCHEMA_PLACEHOLDER}
            rows={12}
            className="mt-2 block w-full rounded-md border border-gray-300 px-3 py-2 text-xs font-mono leading-relaxed focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          />
          <p className="mt-2 text-[11px] text-gray-400">
            Tip: pásale al LLM el JSON tal como quieres que lo devuelva. El sistema lo inyecta al prompt y exige al modelo que produzca exactamente esa estructura bajo el campo <code className="font-mono">result</code>.
          </p>
        </div>
      </div>

      <div className="flex items-center justify-between border-t border-gray-200 px-6 py-3">
        <p className="text-xs text-gray-500">
          {savedAt && !dirty
            ? '✓ Cambios guardados — el próximo run del worker los usará.'
            : 'Los cambios se aplican automáticamente en la próxima ejecución del worker LABEL_CONVERSATIONS.'}
        </p>
        <div className="flex gap-2">
          <button
            type="button"
            onClick={handleReset}
            disabled={!dirty || update.isPending}
            className="flex items-center gap-1.5 rounded-lg border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
          >
            <RotateCcw className="h-3.5 w-3.5" /> Descartar
          </button>
          <button
            type="button"
            onClick={handleSave}
            disabled={!dirty || update.isPending}
            className="flex items-center gap-1.5 rounded-lg bg-indigo-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
          >
            {update.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Save className="h-3.5 w-3.5" />}
            Guardar
          </button>
        </div>
      </div>
    </div>
  )
}
