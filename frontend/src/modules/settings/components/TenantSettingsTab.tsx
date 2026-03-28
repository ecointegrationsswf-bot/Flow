import { useState, useEffect } from 'react'
import { Loader2, Save, Mail, Brain } from 'lucide-react'
import { useTenant, useUpdateTenantSendGrid, useUpdateTenantLlm } from '@/shared/hooks/useTenant'

// Modelos disponibles por proveedor — para poblar el selector dinámicamente
const LLM_MODELS: Record<string, { value: string; label: string }[]> = {
  Anthropic: [
    { value: 'claude-sonnet-4-6', label: 'Claude Sonnet 4.6 (Recomendado)' },
    { value: 'claude-opus-4-6', label: 'Claude Opus 4.6 (Premium)' },
    { value: 'claude-haiku-4-5-20251001', label: 'Claude Haiku 4.5 (Economico)' },
  ],
  OpenAI: [
    { value: 'gpt-4o', label: 'GPT-4o (Recomendado)' },
    { value: 'gpt-4o-mini', label: 'GPT-4o Mini (Economico)' },
    { value: 'gpt-4.1', label: 'GPT-4.1' },
  ],
  Gemini: [
    { value: 'gemini-2.5-pro', label: 'Gemini 2.5 Pro (Recomendado)' },
    { value: 'gemini-2.5-flash', label: 'Gemini 2.5 Flash (Economico)' },
  ],
}

export function TenantSettingsTab() {
  const { data: tenant, isLoading, error } = useTenant()
  const updateSendGrid = useUpdateTenantSendGrid()
  const updateLlm = useUpdateTenantLlm()

  const [sendGridApiKey, setSendGridApiKey] = useState('')
  const [senderEmail, setSenderEmail] = useState('')

  const [llmProvider, setLlmProvider] = useState('Anthropic')
  const [llmApiKey, setLlmApiKey] = useState('')
  const [llmModel, setLlmModel] = useState('claude-sonnet-4-6')

  useEffect(() => {
    if (tenant) {
      setSendGridApiKey('')
      setSenderEmail(tenant.senderEmail ?? '')
      setLlmProvider(tenant.llmProvider ?? 'Anthropic')
      setLlmApiKey('')
      setLlmModel(tenant.llmModel ?? 'claude-sonnet-4-6')
    }
  }, [tenant])

  const handleSaveSendGrid = () => {
    updateSendGrid.mutate({
      sendGridApiKey: sendGridApiKey || null,
      senderEmail: senderEmail || null,
    })
  }

  const handleSaveLlm = () => {
    updateLlm.mutate({
      llmProvider,
      llmApiKey: llmApiKey || null,
      llmModel,
    })
  }

  // Cuando cambia el proveedor, seleccionar el primer modelo de ese proveedor
  const handleProviderChange = (provider: string) => {
    setLlmProvider(provider)
    const models = LLM_MODELS[provider]
    if (models?.length) setLlmModel(models[0].value)
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="h-6 w-6 animate-spin text-gray-400" />
      </div>
    )
  }

  if (error || !tenant) {
    return (
      <div className="rounded-lg bg-red-50 p-4 text-sm text-red-600">
        Error al cargar la informacion del tenant.
      </div>
    )
  }

  const inputClass = "mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"

  return (
    <div className="space-y-6">
      {/* Info del tenant */}
      <div className="rounded-lg bg-white p-5 shadow-sm">
        <h3 className="mb-4 text-sm font-semibold text-gray-900">Informacion del tenant</h3>
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-medium text-gray-500">Nombre</label>
              <p className="mt-1 text-sm text-gray-900">{tenant.name}</p>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-500">Slug</label>
              <p className="mt-1 text-sm text-gray-900">{tenant.slug}</p>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-500">Pais</label>
              <p className="mt-1 text-sm text-gray-900">{tenant.country || '—'}</p>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-500">Proveedor WhatsApp</label>
              <p className="mt-1 text-sm text-gray-900">{tenant.whatsAppProvider}</p>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-500">Telefono WhatsApp</label>
              <p className="mt-1 text-sm text-gray-900">{tenant.whatsAppPhoneNumber || '—'}</p>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-500">Horario de atencion</label>
              <p className="mt-1 text-sm text-gray-900">{tenant.businessHoursStart} - {tenant.businessHoursEnd}</p>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-500">Zona horaria</label>
              <p className="mt-1 text-sm text-gray-900">{tenant.timeZone}</p>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-500">Estado</label>
              <span className={`mt-1 inline-block rounded-full px-2 py-0.5 text-xs font-medium ${tenant.isActive ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'}`}>
                {tenant.isActive ? 'Activo' : 'Inactivo'}
              </span>
            </div>
          </div>
          <p className="text-xs text-gray-400">
            La configuracion del tenant se administra desde el panel de administrador.
          </p>
        </div>
      </div>

      {/* Configuración LLM */}
      <div className="rounded-lg bg-white p-5 shadow-sm">
        <div className="mb-4 flex items-center gap-2">
          <Brain className="h-4 w-4 text-purple-600" />
          <h3 className="text-sm font-semibold text-gray-900">Configuracion del modelo de IA (LLM)</h3>
        </div>
        <p className="mb-4 text-xs text-gray-500">
          Selecciona el proveedor de inteligencia artificial y el modelo que usaran los agentes de este tenant para responder mensajes.
        </p>
        <div className="grid grid-cols-3 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700">Proveedor</label>
            <select
              value={llmProvider}
              onChange={e => handleProviderChange(e.target.value)}
              className={inputClass}
            >
              <option value="Anthropic">Anthropic (Claude)</option>
              <option value="OpenAI">OpenAI (GPT)</option>
              <option value="Gemini">Google (Gemini)</option>
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700">Modelo</label>
            <select
              value={llmModel}
              onChange={e => setLlmModel(e.target.value)}
              className={inputClass}
            >
              {(LLM_MODELS[llmProvider] ?? []).map(m => (
                <option key={m.value} value={m.value}>{m.label}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700">API Key</label>
            <input
              type="password"
              value={llmApiKey}
              onChange={e => setLlmApiKey(e.target.value)}
              className={inputClass}
              placeholder={tenant.llmApiKey ? `Configurado (${tenant.llmApiKey})` : 'sk-...'}
            />
          </div>
        </div>
        <div className="mt-4">
          <button
            type="button"
            onClick={handleSaveLlm}
            disabled={updateLlm.isPending}
            className="flex items-center gap-2 rounded-lg bg-purple-600 px-4 py-2 text-sm font-medium text-white hover:bg-purple-700 disabled:opacity-50 transition-colors"
          >
            {updateLlm.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            {updateLlm.isPending ? 'Guardando...' : 'Guardar configuracion LLM'}
          </button>
          {updateLlm.isSuccess && (
            <p className="mt-2 text-sm text-green-600">Configuracion de LLM actualizada correctamente.</p>
          )}
          {updateLlm.isError && (
            <p className="mt-2 text-sm text-red-600">Error al actualizar la configuracion de LLM.</p>
          )}
        </div>
      </div>

      {/* Configuración SendGrid */}
      <div className="rounded-lg bg-white p-5 shadow-sm">
        <div className="mb-4 flex items-center gap-2">
          <Mail className="h-4 w-4 text-blue-600" />
          <h3 className="text-sm font-semibold text-gray-900">Configuracion de Email (SendGrid)</h3>
        </div>
        <p className="mb-4 text-xs text-gray-500">
          Configura el token de SendGrid y la cuenta de correo para el envio de emails desde las campanas de este tenant.
        </p>
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700">Cuenta de correo remitente</label>
            <input
              type="email"
              value={senderEmail}
              onChange={e => setSenderEmail(e.target.value)}
              className={inputClass}
              placeholder="cobros@empresa.com"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700">Token SendGrid</label>
            <input
              type="password"
              value={sendGridApiKey}
              onChange={e => setSendGridApiKey(e.target.value)}
              className={inputClass}
              placeholder={tenant.sendGridApiKey ? `Configurado (${tenant.sendGridApiKey})` : 'SG.xxxxx'}
            />
          </div>
        </div>
        <div className="mt-4">
          <button
            type="button"
            onClick={handleSaveSendGrid}
            disabled={updateSendGrid.isPending}
            className="flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
          >
            {updateSendGrid.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            {updateSendGrid.isPending ? 'Guardando...' : 'Guardar configuracion'}
          </button>
          {updateSendGrid.isSuccess && (
            <p className="mt-2 text-sm text-green-600">Configuracion actualizada correctamente.</p>
          )}
          {updateSendGrid.isError && (
            <p className="mt-2 text-sm text-red-600">Error al actualizar la configuracion.</p>
          )}
        </div>
      </div>
    </div>
  )
}
