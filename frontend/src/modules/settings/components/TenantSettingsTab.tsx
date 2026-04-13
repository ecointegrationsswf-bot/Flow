import { useState, useEffect } from 'react'
import { Loader2, Save, Mail, Brain, Clock, MessageSquare } from 'lucide-react'
import { ToggleLeft, ToggleRight } from 'lucide-react'
import { Webhook } from 'lucide-react'
import { useTenant, useUpdateTenantSendGrid, useUpdateTenantLlm, useUpdateTenantTimezone, useUpdateCampaignDelay, useUpdateBrainEnabled, useUpdateWebhookContract } from '@/shared/hooks/useTenant'

const TIMEZONES = [
  { value: 'America/Panama',      label: 'America/Panama (UTC-5)' },
  { value: 'America/Bogota',      label: 'America/Bogota (UTC-5)' },
  { value: 'America/Lima',        label: 'America/Lima (UTC-5)' },
  { value: 'America/Guayaquil',   label: 'America/Guayaquil (UTC-5)' },
  { value: 'America/Caracas',     label: 'America/Caracas (UTC-4)' },
  { value: 'America/Santiago',    label: 'America/Santiago (UTC-3/-4)' },
  { value: 'America/Argentina/Buenos_Aires', label: 'America/Buenos Aires (UTC-3)' },
  { value: 'America/Sao_Paulo',   label: 'America/Sao Paulo (UTC-3)' },
  { value: 'America/Mexico_City', label: 'America/Mexico City (UTC-6)' },
  { value: 'America/Guatemala',   label: 'America/Guatemala (UTC-6)' },
  { value: 'America/Costa_Rica',  label: 'America/Costa Rica (UTC-6)' },
  { value: 'America/El_Salvador', label: 'America/El Salvador (UTC-6)' },
  { value: 'America/Tegucigalpa', label: 'America/Tegucigalpa (UTC-6)' },
  { value: 'America/Managua',     label: 'America/Managua (UTC-6)' },
  { value: 'America/New_York',    label: 'America/New York (UTC-4/-5)' },
  { value: 'America/Chicago',     label: 'America/Chicago (UTC-5/-6)' },
  { value: 'America/Los_Angeles', label: 'America/Los Angeles (UTC-7/-8)' },
  { value: 'Europe/Madrid',       label: 'Europe/Madrid (UTC+1/+2)' },
  { value: 'UTC',                 label: 'UTC (UTC+0)' },
]

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
  const updateTimezone = useUpdateTenantTimezone()
  const updateDelay = useUpdateCampaignDelay()
  const updateBrain = useUpdateBrainEnabled()
  const updateWebhookContract = useUpdateWebhookContract()

  const [sendGridApiKey, setSendGridApiKey] = useState('')
  const [senderEmail, setSenderEmail] = useState('')

  const [llmProvider, setLlmProvider] = useState('Anthropic')
  const [llmApiKey, setLlmApiKey] = useState('')
  const [llmModel, setLlmModel] = useState('claude-sonnet-4-6')

  const [timeZone, setTimeZone] = useState('America/Panama')
  const [timezoneSaved, setTimezoneSaved] = useState(false)

  const [campaignDelay, setCampaignDelay] = useState(10)
  const [delaySaved, setDelaySaved] = useState(false)

  useEffect(() => {
    if (tenant) {
      setSendGridApiKey('')
      setSenderEmail(tenant.senderEmail ?? '')
      setLlmProvider(tenant.llmProvider ?? 'Anthropic')
      setLlmApiKey('')
      setLlmModel(tenant.llmModel ?? 'claude-sonnet-4-6')
      setTimeZone(tenant.timeZone ?? 'America/Panama')
      setCampaignDelay(tenant.campaignMessageDelaySeconds ?? 10)
    }
  }, [tenant])

  const handleSaveTimezone = async () => {
    await updateTimezone.mutateAsync(timeZone)
    setTimezoneSaved(true)
    setTimeout(() => setTimezoneSaved(false), 2500)
  }

  const handleSaveDelay = async () => {
    await updateDelay.mutateAsync(campaignDelay)
    setDelaySaved(true)
    setTimeout(() => setDelaySaved(false), 2500)
  }

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
            <div className="col-span-2">
              <div className="flex items-end gap-3">
                <div className="flex-1">
                  <label className="block text-xs font-medium text-gray-500 mb-1">
                    <Clock className="inline h-3.5 w-3.5 mr-1 text-gray-400" />
                    Zona horaria
                  </label>
                  <select
                    value={timeZone}
                    onChange={e => setTimeZone(e.target.value)}
                    className="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                  >
                    {TIMEZONES.map(tz => (
                      <option key={tz.value} value={tz.value}>{tz.label}</option>
                    ))}
                  </select>
                </div>
                <button
                  type="button"
                  onClick={handleSaveTimezone}
                  disabled={updateTimezone.isPending}
                  className="flex items-center gap-1.5 rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
                >
                  {updateTimezone.isPending
                    ? <Loader2 className="h-4 w-4 animate-spin" />
                    : <Save className="h-4 w-4" />}
                  {timezoneSaved ? '¡Guardado!' : 'Guardar'}
                </button>
              </div>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-500">Estado</label>
              <span className={`mt-1 inline-block rounded-full px-2 py-0.5 text-xs font-medium ${tenant.isActive ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'}`}>
                {tenant.isActive ? 'Activo' : 'Inactivo'}
              </span>
            </div>
          </div>
          <p className="text-xs text-gray-400">
            El nombre, pais y proveedor WhatsApp se administran desde el panel de administrador.
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

      {/* Delay entre mensajes de campaña */}
      <div className="rounded-lg bg-white p-5 shadow-sm">
        <div className="mb-4 flex items-center gap-2">
          <MessageSquare className="h-4 w-4 text-green-600" />
          <h3 className="text-sm font-semibold text-gray-900">Delay entre mensajes de campaña</h3>
        </div>
        <p className="mb-5 text-xs text-gray-500">
          Tiempo de espera entre cada mensaje enviado al lanzar una campaña. Valores bajos pueden causar bloqueos de WhatsApp. Recomendado: 8–15 segundos.
        </p>
        <div className="space-y-3">
          <div className="flex items-center gap-4">
            <input
              type="range"
              min={3}
              max={120}
              step={1}
              value={campaignDelay}
              onChange={e => setCampaignDelay(Number(e.target.value))}
              className="flex-1 accent-green-600"
            />
            <div className="flex w-24 items-center gap-1">
              <input
                type="number"
                min={3}
                max={120}
                value={campaignDelay}
                onChange={e => setCampaignDelay(Math.min(120, Math.max(3, Number(e.target.value))))}
                className="w-16 rounded-md border border-gray-300 px-2 py-1.5 text-center text-sm focus:border-green-500 focus:outline-none focus:ring-1 focus:ring-green-500"
              />
              <span className="text-xs text-gray-500">seg</span>
            </div>
          </div>
          <div className="flex justify-between text-xs text-gray-400">
            <span>3 seg (mínimo)</span>
            <span className={`font-medium ${campaignDelay <= 5 ? 'text-red-500' : campaignDelay <= 10 ? 'text-yellow-600' : 'text-green-600'}`}>
              {campaignDelay <= 5 ? 'Riesgo de bloqueo' : campaignDelay <= 10 ? 'Aceptable' : 'Seguro'}
            </span>
            <span>120 seg (máximo)</span>
          </div>
        </div>
        <div className="mt-4">
          <button
            type="button"
            onClick={handleSaveDelay}
            disabled={updateDelay.isPending}
            className="flex items-center gap-2 rounded-lg bg-green-600 px-4 py-2 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50 transition-colors"
          >
            {updateDelay.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            {delaySaved ? '¡Guardado!' : updateDelay.isPending ? 'Guardando...' : 'Guardar delay'}
          </button>
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

      {/* Seccion: Cerebro */}
      <div className="rounded-lg bg-white p-6 shadow-sm">
        <div className="flex items-center gap-2 mb-4">
          <Brain className="h-5 w-5 text-indigo-600" />
          <h2 className="text-base font-semibold text-gray-900">Cerebro — Orquestacion Inteligente</h2>
        </div>

        <div className="flex items-center justify-between rounded-lg border border-gray-200 px-4 py-4">
          <div>
            <p className="text-sm font-medium text-gray-800">Activar el Cerebro para este tenant</p>
            <p className="text-xs text-gray-500 mt-1 max-w-md">
              Cuando esta activo, todos los mensajes entrantes pasan por el Cerebro antes de ser asignados a un agente.
              Requiere al menos un Agente Welcome configurado en el registro de agentes.
            </p>
          </div>
          <button
            type="button"
            onClick={() => updateBrain.mutate(!tenant?.brainEnabled)}
            disabled={updateBrain.isPending}
            className="transition-colors"
          >
            {tenant?.brainEnabled ? (
              <ToggleRight className="h-8 w-8 text-indigo-600" />
            ) : (
              <ToggleLeft className="h-8 w-8 text-gray-300" />
            )}
          </button>
        </div>
        {updateBrain.isError && (
          <p className="mt-2 text-sm text-red-600">{(updateBrain.error as any)?.response?.data?.error ?? 'Error al actualizar.'}</p>
        )}
        {updateBrain.isSuccess && (
          <p className="mt-2 text-sm text-green-600">Configuracion del Cerebro actualizada.</p>
        )}
      </div>

      {/* Seccion: Webhook Contract / Action Trigger Protocol */}
      <div className="rounded-lg bg-white p-6 shadow-sm">
        <div className="flex items-center gap-2 mb-4">
          <Webhook className="h-5 w-5 text-purple-600" />
          <h2 className="text-base font-semibold text-gray-900">Webhook Contract System</h2>
        </div>

        <div className="flex items-center justify-between rounded-lg border border-gray-200 px-4 py-4">
          <div>
            <p className="text-sm font-medium text-gray-800">Activar Webhook Contract y Action Trigger Protocol</p>
            <p className="text-xs text-gray-500 mt-1 max-w-md">
              Cuando esta activo, las acciones configuradas en los maestros de campana pueden ejecutar
              webhooks externos y el agente IA puede disparar acciones automaticamente durante las conversaciones.
            </p>
          </div>
          <button
            type="button"
            onClick={() => updateWebhookContract.mutate(!tenant?.webhookContractEnabled)}
            disabled={updateWebhookContract.isPending}
            className="transition-colors"
          >
            {tenant?.webhookContractEnabled ? (
              <ToggleRight className="h-8 w-8 text-purple-600" />
            ) : (
              <ToggleLeft className="h-8 w-8 text-gray-300" />
            )}
          </button>
        </div>
        {updateWebhookContract.isError && (
          <p className="mt-2 text-sm text-red-600">{(updateWebhookContract.error as any)?.response?.data?.error ?? 'Error al actualizar.'}</p>
        )}
        {updateWebhookContract.isSuccess && (
          <p className="mt-2 text-sm text-green-600">Configuracion de Webhook Contract actualizada.</p>
        )}
      </div>
    </div>
  )
}
