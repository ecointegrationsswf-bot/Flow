import { useState } from 'react'
import { X, ChevronLeft, ChevronRight, Save, Lock } from 'lucide-react'
import { Step0TriggerConfig } from './Step0TriggerConfig'
import { Step1Connection } from './Step1Connection'
import { Step2TestEndpoint } from './Step2TestEndpoint'
import { Step3InputSchema } from './Step3InputSchema'
import { Step4OutputSchema } from './Step4OutputSchema'
import type { WebhookContractBundle, DetectedFieldDto, InputSchema, OutputSchema } from '../types'

interface Props {
  /** Bundle inicial (si ya existe config) */
  initial?: Partial<WebhookContractBundle>
  /** Nombre de la acción para mostrar en el header */
  actionName: string
  /** Callback al guardar — recibe el bundle completo. No se invoca cuando readOnly=true. */
  onSave: (bundle: WebhookContractBundle) => void
  /** Callback al cerrar sin guardar */
  onClose: () => void
  /**
   * Modo solo-consulta — el usuario puede navegar entre pasos para ver el contrato
   * pero NO puede modificarlo. Botón "Guardar" se reemplaza por "Cerrar", banner
   * superior advierte que solo el administrador edita, y los inputs quedan visualmente
   * deshabilitados (pointer-events:none + opacidad reducida). El backend también
   * bloquea la edición desde tenant (TenantActionsController devuelve 403), así que
   * esta es solo la capa de UI.
   */
  readOnly?: boolean
}

const DEFAULT_BUNDLE: WebhookContractBundle = {
  webhookUrl: '',
  webhookMethod: 'POST',
  contentType: 'application/json',
  structure: 'flat',
  authType: 'None',
  timeoutSeconds: 10,
  inputSchema: undefined,
  outputSchema: undefined,
}

export function WebhookBuilderModal({ initial, actionName, onSave, onClose, readOnly = false }: Props) {
  // Action Trigger Protocol (Fase 5): el wizard arranca en el Paso 0 (Trigger),
  // opcional pero recomendado antes de la Conexión.
  const [step, setStep] = useState(0)
  const [bundle, setBundle] = useState<WebhookContractBundle>({
    ...DEFAULT_BUNDLE,
    ...initial,
  })
  const [detectedFields, setDetectedFields] = useState<DetectedFieldDto[]>([])

  const steps = [
    { num: 0, label: 'Trigger' },
    { num: 1, label: 'Conexión' },
    { num: 2, label: 'Prueba' },
    { num: 3, label: 'Input Schema' },
    { num: 4, label: 'Output Schema' },
  ]

  const canContinue = () => {
    // Paso 0 es opcional — siempre se puede avanzar. Si hay requiresConfirmation
    // definidos pero no clarificationPrompt, permitimos avanzar igual y mostramos
    // el warning en el propio Step (es una recomendación, no un hard block).
    if (step === 0) return true
    if (step === 1) return bundle.webhookUrl.trim().length > 0
    return true
  }

  const handleInputSchemaChange = (inputSchema: InputSchema) => {
    setBundle({ ...bundle, inputSchema })
  }

  const handleOutputSchemaChange = (outputSchema: OutputSchema) => {
    setBundle({ ...bundle, outputSchema })
  }

  const handleSave = () => {
    onSave(bundle)
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <div className="w-full max-w-5xl rounded-xl bg-white shadow-xl flex flex-col max-h-[90vh]">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-gray-200 px-5 py-3">
          <div>
            <h2 className="text-base font-semibold text-gray-900">
              {readOnly ? 'Ver contrato de webhook (solo consulta)' : 'Configurar contrato de webhook'}
            </h2>
            <p className="text-xs text-gray-500">Acción: {actionName}</p>
          </div>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600">
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Banner read-only */}
        {readOnly && (
          <div className="flex items-center gap-2 bg-amber-50 border-b border-amber-200 px-5 py-2 text-xs text-amber-800">
            <Lock className="h-3.5 w-3.5" />
            <span>
              Solo el administrador puede modificar este contrato. Esta vista es de consulta.
            </span>
          </div>
        )}

        {/* Stepper */}
        <div className="flex items-center justify-between border-b border-gray-200 px-5 py-3 bg-gray-50">
          {steps.map((s, idx) => (
            <div key={s.num} className="flex items-center flex-1">
              <div className="flex items-center gap-2">
                <div
                  className={`flex h-7 w-7 items-center justify-center rounded-full text-xs font-semibold ${
                    step === s.num
                      ? 'bg-blue-600 text-white'
                      : step > s.num
                      ? 'bg-green-500 text-white'
                      : 'bg-gray-200 text-gray-600'
                  }`}
                >
                  {s.num}
                </div>
                <span
                  className={`text-xs font-medium ${
                    step === s.num ? 'text-blue-600' : step > s.num ? 'text-green-600' : 'text-gray-500'
                  }`}
                >
                  {s.label}
                </span>
              </div>
              {idx < steps.length - 1 && (
                <div className={`flex-1 h-0.5 mx-2 ${step > s.num ? 'bg-green-500' : 'bg-gray-200'}`} />
              )}
            </div>
          ))}
        </div>

        {/* Body — en readOnly, deshabilitamos visualmente pointer-events para que el
            usuario solo vea pero no pueda editar inputs/botones internos de cada Step. */}
        <div
          className="flex-1 overflow-y-auto px-5 py-4"
          style={readOnly ? { pointerEvents: 'none', opacity: 0.85 } : undefined}
        >
          {step === 0 && <Step0TriggerConfig bundle={bundle} onChange={setBundle} actionName={actionName} />}
          {step === 1 && <Step1Connection bundle={bundle} onChange={setBundle} />}
          {step === 2 && <Step2TestEndpoint bundle={bundle} onDetectedFields={setDetectedFields} />}
          {step === 3 && <Step3InputSchema bundle={bundle} onChange={handleInputSchemaChange} readOnly={readOnly} />}
          {step === 4 && (
            <Step4OutputSchema
              bundle={bundle}
              detectedFields={detectedFields}
              onChange={handleOutputSchemaChange}
              readOnly={readOnly}
            />
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between border-t border-gray-200 px-5 py-3">
          <button
            onClick={() => setStep((s) => Math.max(0, s - 1))}
            disabled={step === 0}
            className="flex items-center gap-1 rounded-lg border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-40"
          >
            <ChevronLeft className="h-3.5 w-3.5" />
            Anterior
          </button>

          <div className="flex items-center gap-2">
            {step < 4 ? (
              <button
                onClick={() => setStep((s) => Math.min(4, s + 1))}
                disabled={!canContinue()}
                className="flex items-center gap-1 rounded-lg bg-blue-600 px-4 py-1.5 text-xs font-medium text-white hover:bg-blue-700 disabled:opacity-50"
              >
                Siguiente
                <ChevronRight className="h-3.5 w-3.5" />
              </button>
            ) : readOnly ? (
              <button
                onClick={onClose}
                className="flex items-center gap-1 rounded-lg bg-gray-700 px-4 py-1.5 text-xs font-medium text-white hover:bg-gray-800"
              >
                Cerrar
              </button>
            ) : (
              <button
                onClick={handleSave}
                className="flex items-center gap-1 rounded-lg bg-green-600 px-4 py-1.5 text-xs font-medium text-white hover:bg-green-700"
              >
                <Save className="h-3.5 w-3.5" />
                Guardar contrato
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
