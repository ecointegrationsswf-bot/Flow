import { useState } from 'react'
import { Play, CheckCircle2, XCircle, Loader2 } from 'lucide-react'
import { useWebhookTest } from '../hooks/useWebhookTest'
import type { WebhookContractBundle, DetectedFieldDto } from '../types'

interface Props {
  bundle: WebhookContractBundle
  onDetectedFields: (fields: DetectedFieldDto[]) => void
}

export function Step2TestEndpoint({ bundle, onDetectedFields }: Props) {
  const testMutation = useWebhookTest()
  const [samplePayloadText, setSamplePayloadText] = useState(
    '{\n  "ejemplo": "valor"\n}'
  )

  const handleTest = async () => {
    let samplePayload: Record<string, unknown> | undefined
    try {
      samplePayload = JSON.parse(samplePayloadText)
    } catch {
      samplePayload = {}
    }

    const response = await testMutation.mutateAsync({
      webhookUrl: bundle.webhookUrl,
      webhookMethod: bundle.webhookMethod,
      contentType: bundle.contentType,
      authType: bundle.authType,
      authValue: bundle.authValue,
      apiKeyHeaderName: bundle.apiKeyHeaderName,
      webhookHeaders: bundle.webhookHeaders,
      timeoutSeconds: bundle.timeoutSeconds,
      samplePayload,
    })

    if (response.success && response.detectedFields) {
      onDetectedFields(response.detectedFields)
    }
  }

  const result = testMutation.data
  const isLoading = testMutation.isPending

  return (
    <div className="space-y-4">
      <div>
        <h3 className="text-sm font-semibold text-gray-900 mb-1">Paso 2 — Prueba de endpoint</h3>
        <p className="text-xs text-gray-500">
          Envía un request de prueba al webhook para verificar la conexión y auto-detectar
          los campos de la respuesta.
        </p>
      </div>

      <div>
        <label className="block text-xs font-medium text-gray-700 mb-1">Payload de prueba (JSON)</label>
        <textarea
          rows={5}
          value={samplePayloadText}
          onChange={(e) => setSamplePayloadText(e.target.value)}
          className="w-full rounded border border-gray-300 px-3 py-2 text-xs font-mono"
        />
      </div>

      <button
        type="button"
        onClick={handleTest}
        disabled={isLoading || !bundle.webhookUrl}
        className="flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
      >
        {isLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Play className="h-4 w-4" />}
        {isLoading ? 'Probando...' : 'Probar endpoint'}
      </button>

      {result && (
        <div
          className={`rounded-lg border p-3 ${
            result.success ? 'border-green-200 bg-green-50' : 'border-red-200 bg-red-50'
          }`}
        >
          <div className="flex items-center gap-2 mb-2">
            {result.success ? (
              <CheckCircle2 className="h-4 w-4 text-green-600" />
            ) : (
              <XCircle className="h-4 w-4 text-red-600" />
            )}
            <span className="text-xs font-semibold">
              HTTP {result.httpStatus} · {result.durationMs}ms
            </span>
          </div>

          {result.errorMessage && (
            <p className="text-xs text-red-700">{result.errorMessage}</p>
          )}

          {result.success && result.detectedFields && result.detectedFields.length > 0 && (
            <div className="mt-2">
              <p className="text-xs font-medium text-gray-700 mb-1">
                Campos detectados ({result.detectedFields.length}):
              </p>
              <div className="space-y-1">
                {result.detectedFields.map((f) => (
                  <div key={f.fieldPath} className="flex items-center gap-2 text-xs">
                    <code className="rounded bg-white px-2 py-0.5 font-mono text-gray-700 border border-gray-200">
                      {f.fieldPath}
                    </code>
                    <span className="text-gray-500">→</span>
                    <span className="rounded bg-blue-100 px-2 py-0.5 text-blue-700">{f.dataType}</span>
                  </div>
                ))}
              </div>
            </div>
          )}

          {result.rawBody && (
            <details className="mt-2" open={!result.success}>
              <summary className="text-xs cursor-pointer text-gray-600">
                {result.success ? 'Ver respuesta completa' : 'Respuesta del servidor (error)'}
              </summary>
              <pre className={`mt-1 max-h-60 overflow-auto rounded p-2 text-xs font-mono border ${
                result.success
                  ? 'bg-white text-gray-700 border-gray-200'
                  : 'bg-red-50 text-red-900 border-red-200'
              }`}>
                {result.rawBody}
              </pre>
            </details>
          )}
        </div>
      )}

      {testMutation.isError && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-3">
          <p className="text-xs text-red-700">Error al probar el webhook: {testMutation.error.message}</p>
        </div>
      )}
    </div>
  )
}
