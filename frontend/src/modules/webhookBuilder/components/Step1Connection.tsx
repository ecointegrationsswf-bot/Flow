import type { WebhookContractBundle } from '../types'

interface Props {
  bundle: WebhookContractBundle
  onChange: (bundle: WebhookContractBundle) => void
}

export function Step1Connection({ bundle, onChange }: Props) {
  const update = <K extends keyof WebhookContractBundle>(key: K, value: WebhookContractBundle[K]) => {
    onChange({ ...bundle, [key]: value })
  }

  return (
    <div className="space-y-4">
      <div>
        <h3 className="text-sm font-semibold text-gray-900 mb-1">Paso 1 — Conexión</h3>
        <p className="text-xs text-gray-500">
          Configura la URL del endpoint y la autenticación. Esta información la usará el sistema
          para ejecutar el webhook en runtime.
        </p>
      </div>

      <div>
        <label className="block text-xs font-medium text-gray-700 mb-1">URL del webhook *</label>
        <input
          type="url"
          value={bundle.webhookUrl}
          onChange={(e) => update('webhookUrl', e.target.value)}
          placeholder="https://api.tuservicio.com/endpoint"
          className="w-full rounded border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        />
      </div>

      <div className="grid grid-cols-3 gap-3">
        <div>
          <label className="block text-xs font-medium text-gray-700 mb-1">Método HTTP</label>
          <select
            value={bundle.webhookMethod}
            onChange={(e) => update('webhookMethod', e.target.value as WebhookContractBundle['webhookMethod'])}
            className="w-full rounded border border-gray-300 px-3 py-2 text-sm"
          >
            <option value="POST">POST</option>
            <option value="GET">GET</option>
            <option value="PUT">PUT</option>
            <option value="PATCH">PATCH</option>
          </select>
        </div>

        <div>
          <label className="block text-xs font-medium text-gray-700 mb-1">Content-Type</label>
          <select
            value={bundle.contentType}
            onChange={(e) => update('contentType', e.target.value as WebhookContractBundle['contentType'])}
            className="w-full rounded border border-gray-300 px-3 py-2 text-sm"
          >
            <option value="application/json">JSON</option>
            <option value="application/x-www-form-urlencoded">Form</option>
            <option value="multipart/form-data">Multipart</option>
          </select>
        </div>

        <div>
          <label className="block text-xs font-medium text-gray-700 mb-1">Estructura</label>
          <select
            value={bundle.structure}
            onChange={(e) => update('structure', e.target.value as WebhookContractBundle['structure'])}
            className="w-full rounded border border-gray-300 px-3 py-2 text-sm"
          >
            <option value="flat">Plano</option>
            <option value="nested">Anidado</option>
          </select>
        </div>
      </div>

      <div className="border-t border-gray-200 pt-4">
        <h4 className="text-xs font-semibold text-gray-800 mb-3">Autenticación</h4>

        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="block text-xs font-medium text-gray-700 mb-1">Tipo</label>
            <select
              value={bundle.authType}
              onChange={(e) => update('authType', e.target.value as WebhookContractBundle['authType'])}
              className="w-full rounded border border-gray-300 px-3 py-2 text-sm"
            >
              <option value="None">Ninguna</option>
              <option value="Bearer">Bearer Token</option>
              <option value="ApiKey">API Key (header)</option>
            </select>
          </div>

          <div>
            <label className="block text-xs font-medium text-gray-700 mb-1">Timeout (segundos)</label>
            <input
              type="number"
              min={1}
              max={60}
              value={bundle.timeoutSeconds}
              onChange={(e) => update('timeoutSeconds', Number(e.target.value) || 10)}
              className="w-full rounded border border-gray-300 px-3 py-2 text-sm"
            />
          </div>
        </div>

        {bundle.authType !== 'None' && (
          <div className="mt-3">
            <label className="block text-xs font-medium text-gray-700 mb-1">
              {bundle.authType === 'Bearer' ? 'Token Bearer' : 'Valor del API Key'}
            </label>
            <input
              type="password"
              value={bundle.authValue ?? ''}
              onChange={(e) => update('authValue', e.target.value)}
              placeholder={bundle.authType === 'Bearer' ? 'eyJhbGci...' : 'abc123...'}
              className="w-full rounded border border-gray-300 px-3 py-2 text-sm font-mono"
            />
          </div>
        )}

        {bundle.authType === 'ApiKey' && (
          <div className="mt-3">
            <label className="block text-xs font-medium text-gray-700 mb-1">Nombre del header (default: X-Api-Key)</label>
            <input
              type="text"
              value={bundle.apiKeyHeaderName ?? 'X-Api-Key'}
              onChange={(e) => update('apiKeyHeaderName', e.target.value)}
              className="w-full rounded border border-gray-300 px-3 py-2 text-sm font-mono"
            />
          </div>
        )}
      </div>

      <div>
        <label className="block text-xs font-medium text-gray-700 mb-1">Headers adicionales (JSON opcional)</label>
        <textarea
          rows={2}
          value={bundle.webhookHeaders ?? ''}
          onChange={(e) => update('webhookHeaders', e.target.value)}
          placeholder='{"X-Custom-Header": "valor"}'
          className="w-full rounded border border-gray-300 px-3 py-2 text-xs font-mono"
        />
      </div>
    </div>
  )
}
