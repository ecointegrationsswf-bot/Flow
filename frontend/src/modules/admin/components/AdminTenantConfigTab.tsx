import { useEffect, useState } from 'react'
import { Loader2, Save, Mail, Brain, Clock, Webhook, FileText, ToggleLeft, ToggleRight, Building2, Gauge, AlertTriangle } from 'lucide-react'
import {
  useAdminTenantConfig,
  useAdminUpdateTenantTimezone,
  useAdminUpdateTenantLlm,
  useAdminUpdateTenantSendGrid,
  useAdminUpdateTenantCampaignDelay,
  useAdminUpdateTenantBrain,
  useAdminUpdateTenantWebhookContract,
  useAdminUpdateTenantReferenceDocs,
  useAdminUpdateTenantMessageBuffer,
  useAdminUpdateTenantCampaignRateLimits,
} from '@/modules/admin/hooks/useAdminTenantConfig'

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

const LLM_MODELS: Record<string, { value: string; label: string }[]> = {
  Anthropic: [
    { value: 'claude-sonnet-4-6', label: 'Claude Sonnet 4.6 (Recomendado)' },
    { value: 'claude-opus-4-6', label: 'Claude Opus 4.6 (Premium)' },
    { value: 'claude-haiku-4-5-20251001', label: 'Claude Haiku 4.5 (Económico)' },
  ],
  OpenAI: [
    { value: 'gpt-4o', label: 'GPT-4o (Recomendado)' },
    { value: 'gpt-4o-mini', label: 'GPT-4o Mini (Económico)' },
    { value: 'gpt-4.1', label: 'GPT-4.1' },
  ],
  Gemini: [
    { value: 'gemini-2.5-pro', label: 'Gemini 2.5 Pro (Recomendado)' },
    { value: 'gemini-2.5-flash', label: 'Gemini 2.5 Flash (Económico)' },
  ],
}

interface Props {
  tenantId: string
}

export function AdminTenantConfigTab({ tenantId }: Props) {
  const { data: tenant, isLoading, error } = useAdminTenantConfig(tenantId)
  const updateTimezone = useAdminUpdateTenantTimezone()
  const updateLlm = useAdminUpdateTenantLlm()
  const updateSendGrid = useAdminUpdateTenantSendGrid()
  const updateDelay = useAdminUpdateTenantCampaignDelay()
  const updateBrain = useAdminUpdateTenantBrain()
  const updateWebhookContract = useAdminUpdateTenantWebhookContract()
  const updateReferenceDocs = useAdminUpdateTenantReferenceDocs()
  const updateBuffer = useAdminUpdateTenantMessageBuffer()
  const updateRateLimits = useAdminUpdateTenantCampaignRateLimits()

  const [timeZone, setTimeZone] = useState('America/Panama')
  const [timezoneSaved, setTimezoneSaved] = useState(false)

  const [llmProvider, setLlmProvider] = useState('Anthropic')
  const [llmApiKey, setLlmApiKey] = useState('')
  const [llmModel, setLlmModel] = useState('claude-sonnet-4-6')

  const [sendGridApiKey, setSendGridApiKey] = useState('')
  const [senderEmail, setSenderEmail] = useState('')

  const [campaignDelay, setCampaignDelay] = useState(10)
  const [delaySaved, setDelaySaved] = useState(false)

  const [bufferSeconds, setBufferSeconds] = useState(5)
  const [bufferSaved, setBufferSaved] = useState(false)

  // Rate limit
  const [msgPerMin, setMsgPerMin] = useState(6)
  const [maxPerHour, setMaxPerHour] = useState(200)
  const [maxPerDay, setMaxPerDay] = useState(1000)
  const [dispatchEnabled, setDispatchEnabled] = useState(true)
  const [rateLimitsSaved, setRateLimitsSaved] = useState(false)
  const [rateLimitsError, setRateLimitsError] = useState<string | null>(null)

  useEffect(() => {
    if (!tenant) return
    setTimeZone(tenant.timeZone ?? 'America/Panama')
    setLlmProvider(tenant.llmProvider ?? 'Anthropic')
    setLlmApiKey('')
    setLlmModel(tenant.llmModel ?? 'claude-sonnet-4-6')
    setSendGridApiKey('')
    setSenderEmail(tenant.senderEmail ?? '')
    setCampaignDelay(tenant.campaignMessageDelaySeconds ?? 10)
    setBufferSeconds(tenant.messageBufferSeconds ?? 5)
    setMsgPerMin(tenant.campaignMessagesPerMinute ?? 6)
    setMaxPerHour(tenant.campaignMaxPerHour ?? 200)
    setMaxPerDay(tenant.campaignMaxPerDay ?? 1000)
    setDispatchEnabled(tenant.campaignDispatchEnabled ?? true)
  }, [tenant])

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="h-6 w-6 animate-spin text-gray-400" />
      </div>
    )
  }
  if (error || !tenant) {
    return <div className="rounded-lg bg-red-50 p-4 text-sm text-red-600 m-4">Error al cargar la configuración.</div>
  }

  const inputClass = 'mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500'
  const btnPrimary = 'flex items-center gap-1.5 rounded-lg bg-indigo-600 px-3 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50 transition-colors'

  const handleProviderChange = (p: string) => {
    setLlmProvider(p)
    const models = LLM_MODELS[p]
    if (models?.length) setLlmModel(models[0].value)
  }

  const handleSaveTimezone = async () => {
    await updateTimezone.mutateAsync({ tenantId, timeZone })
    setTimezoneSaved(true)
    setTimeout(() => setTimezoneSaved(false), 2500)
  }

  const handleSaveDelay = async () => {
    await updateDelay.mutateAsync({ tenantId, delaySeconds: campaignDelay })
    setDelaySaved(true)
    setTimeout(() => setDelaySaved(false), 2500)
  }

  const handleSaveBuffer = async () => {
    await updateBuffer.mutateAsync({ tenantId, seconds: bufferSeconds })
    setBufferSaved(true)
    setTimeout(() => setBufferSaved(false), 2500)
  }

  const handleSaveRateLimits = async () => {
    setRateLimitsError(null)
    try {
      await updateRateLimits.mutateAsync({
        tenantId,
        messagesPerMinute: msgPerMin,
        maxPerHour,
        maxPerDay,
        dispatchEnabled,
      })
      setRateLimitsSaved(true)
      setTimeout(() => setRateLimitsSaved(false), 2500)
    } catch (err: unknown) {
      const e = err as { response?: { data?: { error?: string } } }
      setRateLimitsError(e.response?.data?.error ?? 'Error al guardar.')
    }
  }

  // Estimación: cuánto tarda una campaña de N contactos con la config actual.
  const estimateHoursForN = (n: number): string => {
    const ratePerMinute = Math.min(msgPerMin, maxPerHour / 60)
    const cappedRatePerHour = Math.min(ratePerMinute * 60, maxPerHour)
    const hours = n / cappedRatePerHour
    if (hours < 1) return `${Math.ceil(hours * 60)} min`
    return `~${hours.toFixed(1)}h`
  }

  return (
    <div className="space-y-6 px-6 py-4">
      {/* Información del tenant (sólo lectura excepto zona horaria) */}
      <section className="rounded-lg border border-gray-200 bg-white p-5">
        <div className="mb-4 flex items-center gap-2">
          <Building2 className="h-4 w-4 text-gray-600" />
          <h3 className="text-sm font-semibold text-gray-900">Información del tenant</h3>
        </div>
        <div className="grid grid-cols-2 gap-4 text-sm">
          <div><span className="block text-xs text-gray-500">Nombre</span><p className="mt-0.5 text-gray-900">{tenant.name}</p></div>
          <div><span className="block text-xs text-gray-500">Slug</span><p className="mt-0.5 text-gray-900">{tenant.slug}</p></div>
          <div><span className="block text-xs text-gray-500">País</span><p className="mt-0.5 text-gray-900">{tenant.country || '—'}</p></div>
          <div><span className="block text-xs text-gray-500">Estado</span>
            <span className={`mt-0.5 inline-block rounded-full px-2 py-0.5 text-xs font-medium ${tenant.isActive ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'}`}>
              {tenant.isActive ? 'Activo' : 'Inactivo'}
            </span>
          </div>
          <div><span className="block text-xs text-gray-500">Proveedor WhatsApp</span><p className="mt-0.5 text-gray-900">{tenant.whatsAppProvider}</p></div>
          <div><span className="block text-xs text-gray-500">Teléfono WhatsApp</span><p className="mt-0.5 text-gray-900">{tenant.whatsAppPhoneNumber || '—'}</p></div>
          <div><span className="block text-xs text-gray-500">Horario de atención</span><p className="mt-0.5 text-gray-900">{tenant.businessHoursStart} – {tenant.businessHoursEnd}</p></div>
          <div className="col-span-2">
            <label className="mb-1 block text-xs font-medium text-gray-500"><Clock className="inline h-3.5 w-3.5 mr-1 text-gray-400" />Zona horaria</label>
            <div className="flex items-end gap-2">
              <select value={timeZone} onChange={(e) => setTimeZone(e.target.value)} className={inputClass + ' mt-0 flex-1'}>
                {TIMEZONES.map((tz) => (<option key={tz.value} value={tz.value}>{tz.label}</option>))}
              </select>
              <button type="button" onClick={handleSaveTimezone} disabled={updateTimezone.isPending} className={btnPrimary}>
                {updateTimezone.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
                {timezoneSaved ? '¡Guardado!' : 'Guardar'}
              </button>
            </div>
          </div>
        </div>
      </section>

      {/* LLM */}
      <section className="rounded-lg border border-gray-200 bg-white p-5">
        <div className="mb-3 flex items-center gap-2">
          <Brain className="h-4 w-4 text-purple-600" />
          <h3 className="text-sm font-semibold text-gray-900">Configuración del modelo de IA (LLM)</h3>
        </div>
        <div className="grid grid-cols-3 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700">Proveedor</label>
            <select value={llmProvider} onChange={(e) => handleProviderChange(e.target.value)} className={inputClass}>
              <option value="Anthropic">Anthropic (Claude)</option>
              <option value="OpenAI">OpenAI (GPT)</option>
              <option value="Gemini">Google (Gemini)</option>
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700">Modelo</label>
            <select value={llmModel} onChange={(e) => setLlmModel(e.target.value)} className={inputClass}>
              {LLM_MODELS[llmProvider]?.map((m) => (<option key={m.value} value={m.value}>{m.label}</option>))}
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700">API Key</label>
            <input
              type="password"
              value={llmApiKey}
              onChange={(e) => setLlmApiKey(e.target.value)}
              placeholder={tenant.llmApiKey ? `Configurado (${tenant.llmApiKey})` : 'Sin configurar'}
              className={inputClass}
            />
          </div>
        </div>
        <div className="mt-4 flex justify-end">
          <button
            type="button"
            onClick={() => updateLlm.mutate({ tenantId, llmProvider, llmApiKey: llmApiKey || null, llmModel })}
            disabled={updateLlm.isPending}
            className={btnPrimary}
          >
            {updateLlm.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            Guardar configuración LLM
          </button>
        </div>
        {updateLlm.isSuccess && <p className="mt-2 text-xs text-emerald-700">Configuración de LLM actualizada.</p>}
      </section>

      {/* Delay */}
      <section className="rounded-lg border border-gray-200 bg-white p-5">
        <div className="mb-3 flex items-center gap-2">
          <Clock className="h-4 w-4 text-emerald-600" />
          <h3 className="text-sm font-semibold text-gray-900">Delay entre mensajes de campaña</h3>
        </div>
        <p className="mb-3 text-xs text-gray-500">Tiempo de espera entre cada mensaje enviado al lanzar una campaña. Valores bajos pueden causar bloqueos de WhatsApp (recomendado 8–15 seg).</p>
        <div className="flex items-center gap-3">
          <input
            type="range"
            min={3}
            max={120}
            value={campaignDelay}
            onChange={(e) => setCampaignDelay(Number(e.target.value))}
            className="flex-1"
          />
          <div className="w-16 text-right">
            <input
              type="number"
              min={3}
              max={120}
              value={campaignDelay}
              onChange={(e) => setCampaignDelay(Number(e.target.value))}
              className="w-full rounded-md border border-gray-300 px-2 py-1 text-sm"
            />
          </div>
          <span className="text-xs text-gray-500">seg</span>
          <button type="button" onClick={handleSaveDelay} disabled={updateDelay.isPending} className={btnPrimary}>
            {updateDelay.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            {delaySaved ? '¡Guardado!' : 'Guardar'}
          </button>
        </div>
      </section>

      {/* Debounce de mensajes entrantes */}
      <section className="rounded-lg border border-gray-200 bg-white p-5">
        <div className="mb-3 flex items-center gap-2">
          <Clock className="h-4 w-4 text-blue-600" />
          <h3 className="text-sm font-semibold text-gray-900">Debounce de mensajes entrantes</h3>
        </div>
        <p className="mb-3 text-xs text-gray-500">
          Cuando el cliente escribe por partes (ej: "hola", "cómo estás?", "me puedes ayudar"), el sistema
          espera este tiempo antes de procesar. Los mensajes recibidos durante la espera se concatenan y el
          agente responde una sola vez con contexto completo. <strong>0 = deshabilitado</strong> (procesa cada mensaje al instante).
        </p>
        <div className="flex items-center gap-3">
          <input
            type="range"
            min={0}
            max={15}
            value={bufferSeconds}
            onChange={(e) => setBufferSeconds(Number(e.target.value))}
            className="flex-1"
          />
          <div className="w-16 text-right">
            <input
              type="number"
              min={0}
              max={15}
              value={bufferSeconds}
              onChange={(e) => setBufferSeconds(Number(e.target.value))}
              className="w-full rounded-md border border-gray-300 px-2 py-1 text-sm"
            />
          </div>
          <span className="text-xs text-gray-500">seg</span>
          <button type="button" onClick={handleSaveBuffer} disabled={updateBuffer.isPending} className={btnPrimary}>
            {updateBuffer.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            {bufferSaved ? '¡Guardado!' : 'Guardar'}
          </button>
        </div>
        {bufferSeconds === 0 && (
          <p className="mt-2 text-xs text-amber-700">⚠️ Modo legacy activo — el agente responderá a cada mensaje del cliente por separado.</p>
        )}
      </section>

      {/* SendGrid */}
      <section className="rounded-lg border border-gray-200 bg-white p-5">
        <div className="mb-3 flex items-center gap-2">
          <Mail className="h-4 w-4 text-blue-600" />
          <h3 className="text-sm font-semibold text-gray-900">Configuración de Email (SendGrid)</h3>
        </div>
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700">Cuenta de correo remitente</label>
            <input
              type="email"
              value={senderEmail}
              onChange={(e) => setSenderEmail(e.target.value)}
              placeholder="noreply@tuempresa.com"
              className={inputClass}
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700">Token SendGrid</label>
            <input
              type="password"
              value={sendGridApiKey}
              onChange={(e) => setSendGridApiKey(e.target.value)}
              placeholder={tenant.sendGridApiKey ? `Configurado (${tenant.sendGridApiKey})` : 'Sin configurar'}
              className={inputClass}
            />
          </div>
        </div>
        <div className="mt-4 flex justify-end">
          <button
            type="button"
            onClick={() => updateSendGrid.mutate({ tenantId, sendGridApiKey: sendGridApiKey || null, senderEmail: senderEmail || null })}
            disabled={updateSendGrid.isPending}
            className={btnPrimary}
          >
            {updateSendGrid.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            Guardar configuración
          </button>
        </div>
        {updateSendGrid.isSuccess && <p className="mt-2 text-xs text-emerald-700">Configuración de SendGrid actualizada.</p>}
      </section>

      {/* Cerebro */}
      <ToggleSection
        icon={<Brain className="h-4 w-4 text-indigo-600" />}
        title="Cerebro — Orquestación Inteligente"
        description="Cuando está activo, todos los mensajes entrantes pasan por el Cerebro antes de ser asignados a un agente. Requiere al menos un Agente Welcome configurado en el registro de agentes."
        enabled={tenant.brainEnabled}
        onToggle={(v) => updateBrain.mutate({ tenantId, enabled: v })}
        color="indigo"
        loading={updateBrain.isPending}
        errorMsg={(updateBrain.error as { response?: { data?: { error?: string } } })?.response?.data?.error}
      />

      {/* Webhook Contract */}
      <ToggleSection
        icon={<Webhook className="h-4 w-4 text-purple-600" />}
        title="Webhook Contract System"
        description="Cuando está activo, las acciones configuradas en los maestros de campaña pueden ejecutar webhooks externos y el agente IA puede disparar acciones automáticamente durante las conversaciones."
        enabled={tenant.webhookContractEnabled}
        onToggle={(v) => updateWebhookContract.mutate({ tenantId, enabled: v })}
        color="purple"
        loading={updateWebhookContract.isPending}
      />

      {/* Documentos de referencia */}
      <ToggleSection
        icon={<FileText className="h-4 w-4 text-emerald-600" />}
        title="Documentos de Referencia (PDFs)"
        description='Cuando está activo, los PDFs que cargás en el tab "Documentos" de cada maestro se envían al agente como contexto. El agente los consulta cuando su prompt base no cubre la pregunta del cliente. Desactivado = los PDFs se siguen guardando pero el agente no los lee.'
        enabled={tenant.referenceDocumentsEnabled}
        onToggle={(v) => updateReferenceDocs.mutate({ tenantId, enabled: v })}
        color="emerald"
        loading={updateReferenceDocs.isPending}
      />

      {/* Rate limit de envíos masivos */}
      <section className="rounded-lg border border-orange-200 bg-orange-50/30 p-5">
        <div className="mb-1 flex items-center gap-2">
          <Gauge className="h-4 w-4 text-orange-600" />
          <h3 className="text-sm font-semibold text-gray-900">Rate limit de envíos masivos</h3>
        </div>
        <p className="mb-4 text-xs text-gray-600">
          Topes que aplican <strong>al lanzar una campaña inicial</strong> y <strong>al sweeper que envía
          los seguimientos</strong>. Existen para evitar que UltraMsg banee el WhatsApp del cliente
          por enviar en ráfaga. Cualquier envío masivo respeta estos límites sin excepción.
        </p>

        <div className="mb-4 rounded-lg border border-orange-200 bg-white p-3">
          <p className="text-xs text-gray-700">
            <strong>Tiempo estimado para lanzar una campaña con la configuración actual:</strong>
          </p>
          <div className="mt-2 grid grid-cols-3 gap-3 text-xs">
            <div className="rounded bg-gray-50 px-2 py-1.5">
              <span className="block text-gray-500">100 contactos</span>
              <span className="text-base font-semibold text-gray-900">{estimateHoursForN(100)}</span>
            </div>
            <div className="rounded bg-gray-50 px-2 py-1.5">
              <span className="block text-gray-500">500 contactos</span>
              <span className="text-base font-semibold text-gray-900">{estimateHoursForN(500)}</span>
            </div>
            <div className="rounded bg-gray-50 px-2 py-1.5">
              <span className="block text-gray-500">1000 contactos</span>
              <span className="text-base font-semibold text-gray-900">{estimateHoursForN(1000)}</span>
            </div>
          </div>
        </div>

        <div className="space-y-5">
          {/* Mensajes por minuto */}
          <div>
            <label className="mb-1 flex items-center justify-between text-sm font-medium text-gray-700">
              <span>Mensajes por minuto</span>
              <span className="font-mono text-orange-700">{msgPerMin} msg/min</span>
            </label>
            <p className="mb-2 text-xs text-gray-500">
              Cadencia con la que se envían los mensajes. <strong>Default 6</strong> = un mensaje cada
              10 segundos. Subir mucho aumenta la velocidad pero también el riesgo de ban.
              Recomendado: 4–10. <em>Piso técnico:</em> 3 segundos entre mensajes (no se puede ir más rápido).
            </p>
            <div className="flex items-center gap-3">
              <input
                type="range" min={1} max={20} value={msgPerMin}
                onChange={(e) => setMsgPerMin(Number(e.target.value))}
                className="flex-1"
              />
              <input
                type="number" min={1} max={60} value={msgPerMin}
                onChange={(e) => setMsgPerMin(Number(e.target.value))}
                className="w-20 rounded-md border border-gray-300 px-2 py-1 text-sm"
              />
            </div>
          </div>

          {/* Tope por hora */}
          <div>
            <label className="mb-1 flex items-center justify-between text-sm font-medium text-gray-700">
              <span>Tope por hora</span>
              <span className="font-mono text-orange-700">{maxPerHour} msg/h</span>
            </label>
            <p className="mb-2 text-xs text-gray-500">
              Máximo de mensajes que se permite enviar en cualquier ventana de 60 minutos
              (sliding window). <strong>Default 200</strong>. Cuando se llega al tope, el sweeper
              pausa hasta que el más viejo cumpla 1 hora. Es el tope que más impacta la
              duración total de una campaña grande.
            </p>
            <div className="flex items-center gap-3">
              <input
                type="range" min={20} max={1000} step={10} value={maxPerHour}
                onChange={(e) => setMaxPerHour(Number(e.target.value))}
                className="flex-1"
              />
              <input
                type="number" min={1} max={5000} value={maxPerHour}
                onChange={(e) => setMaxPerHour(Number(e.target.value))}
                className="w-24 rounded-md border border-gray-300 px-2 py-1 text-sm"
              />
            </div>
          </div>

          {/* Tope diario */}
          <div>
            <label className="mb-1 flex items-center justify-between text-sm font-medium text-gray-700">
              <span>Tope diario</span>
              <span className="font-mono text-orange-700">{maxPerDay} msg/día</span>
            </label>
            <p className="mb-2 text-xs text-gray-500">
              Tope total para todo el día (00:00 a 24:00 UTC). <strong>Default 1000</strong>. Se
              cuenta sumando todos los mensajes outbound del tenant — campañas + follow-ups +
              respuestas del agente. Cuando se alcanza, el sweeper se detiene y retoma al día siguiente.
            </p>
            <div className="flex items-center gap-3">
              <input
                type="range" min={100} max={5000} step={50} value={maxPerDay}
                onChange={(e) => setMaxPerDay(Number(e.target.value))}
                className="flex-1"
              />
              <input
                type="number" min={1} max={100000} value={maxPerDay}
                onChange={(e) => setMaxPerDay(Number(e.target.value))}
                className="w-24 rounded-md border border-gray-300 px-2 py-1 text-sm"
              />
            </div>
          </div>

          {/* Toggle dispatch */}
          <div className="rounded-lg border border-gray-200 bg-white p-3">
            <div className="flex items-center justify-between">
              <div className="flex-1 pr-4">
                <p className="text-sm font-medium text-gray-900">Dispatch de campañas habilitado</p>
                <p className="mt-0.5 text-xs text-gray-500">
                  Cuando está apagado, el Worker NO procesa las campañas en ejecución de este tenant
                  (se quedan en cola). Sirve para pausar temporalmente todos los envíos masivos
                  sin tener que cancelar las campañas. Las conversaciones entrantes
                  (cliente que escribe primero) NO se ven afectadas.
                </p>
              </div>
              <button
                type="button"
                onClick={() => setDispatchEnabled(v => !v)}
                className="transition-colors"
              >
                {dispatchEnabled
                  ? <ToggleRight className="h-8 w-8 text-orange-600" />
                  : <ToggleLeft className="h-8 w-8 text-gray-300" />}
              </button>
            </div>
          </div>

          {/* Validación visual */}
          {maxPerDay < maxPerHour && (
            <div className="flex items-start gap-2 rounded-md bg-red-50 border border-red-200 p-2 text-xs text-red-700">
              <AlertTriangle className="h-4 w-4 shrink-0" />
              <span>El <strong>Tope diario</strong> debe ser mayor o igual al <strong>Tope por hora</strong>.</span>
            </div>
          )}
          {msgPerMin > 10 && (
            <div className="flex items-start gap-2 rounded-md bg-amber-50 border border-amber-200 p-2 text-xs text-amber-800">
              <AlertTriangle className="h-4 w-4 shrink-0" />
              <span>Cadencia alta — riesgo de ban de UltraMsg. Recomendado dejar en 6–10 msg/min.</span>
            </div>
          )}

          <div className="flex items-center justify-between border-t border-orange-200 pt-3">
            <p className="text-xs text-gray-500">
              Los topes se evalúan contra <code className="font-mono text-gray-700">Messages.SentAt</code> Outbound del tenant.
            </p>
            <button
              type="button"
              onClick={handleSaveRateLimits}
              disabled={updateRateLimits.isPending || maxPerDay < maxPerHour}
              className={btnPrimary}
            >
              {updateRateLimits.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
              {rateLimitsSaved ? '¡Guardado!' : 'Guardar topes'}
            </button>
          </div>
          {rateLimitsError && <p className="text-xs text-red-600">{rateLimitsError}</p>}
        </div>
      </section>
    </div>
  )
}

function ToggleSection({
  icon, title, description, enabled, onToggle, loading, errorMsg, color,
}: {
  icon: React.ReactNode
  title: string
  description: string
  enabled: boolean
  onToggle: (v: boolean) => void
  loading: boolean
  errorMsg?: string
  color: 'indigo' | 'purple' | 'emerald'
}) {
  const onColor = color === 'indigo' ? 'text-indigo-600' : color === 'purple' ? 'text-purple-600' : 'text-emerald-600'
  return (
    <section className="rounded-lg border border-gray-200 bg-white p-5">
      <div className="mb-3 flex items-center gap-2">
        {icon}
        <h3 className="text-sm font-semibold text-gray-900">{title}</h3>
      </div>
      <div className="flex items-center justify-between rounded-lg border border-gray-200 px-4 py-3">
        <p className="flex-1 text-xs text-gray-600 pr-4">{description}</p>
        <button
          type="button"
          onClick={() => onToggle(!enabled)}
          disabled={loading}
          className="transition-colors disabled:opacity-50"
        >
          {enabled ? <ToggleRight className={`h-8 w-8 ${onColor}`} /> : <ToggleLeft className="h-8 w-8 text-gray-300" />}
        </button>
      </div>
      {errorMsg && <p className="mt-2 text-xs text-red-600">{errorMsg}</p>}
    </section>
  )
}
