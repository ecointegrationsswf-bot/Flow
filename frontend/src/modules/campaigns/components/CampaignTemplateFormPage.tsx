import { useState, useEffect } from 'react'

const ACTION_FRIENDLY_NAMES: Record<string, string> = {
  'SEND_EMAIL_RESUME': 'Enviar email con resumen',
  'TRANSFER_CHAT': 'Escalar a humano',
  'SEND_MESSAGE': 'Enviar mensaje',
  'SEND_RESUME': 'Enviar resumen',
  'PREMIUM': 'Premium',
  'CLOSE_CONVERSATION': 'Cerrar conversación',
  'ESCALATE_TO_HUMAN': 'Escalar a ejecutivo',
  'SEND_PAYMENT_LINK': 'Enviar enlace de pago',
  'SEND_DOCUMENT': 'Enviar documento',
}
const getFriendlyName = (name: string) => ACTION_FRIENDLY_NAMES[name] ?? name

import { useTenant } from '@/shared/hooks/useTenant'
import { useNavigate, useParams } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { ArrowLeft, Plus, X, Clock, Zap, FileText, Webhook, Mail, MessageSquare, ChevronDown, ChevronUp, Globe, Tag, Paperclip } from 'lucide-react'
import { useAgents } from '@/shared/hooks/useAgents'
import { useLabels } from '@/shared/hooks/useLabels'
import { WebhookBuilderModal } from '@/modules/webhookBuilder/components/WebhookBuilderModal'
import { CampaignTemplateDocumentsSection } from './CampaignTemplateDocumentsSection'
import type { WebhookContractBundle } from '@/modules/webhookBuilder/types'
import {
  useCampaignTemplate,
  useCreateCampaignTemplate,
  useUpdateCampaignTemplate,
  useAvailableActions,
  useAvailablePrompts,
  type ActionConfig,
} from '@/shared/hooks/useCampaignTemplates'

const schema = z.object({
  name: z.string().min(1, 'El nombre es requerido'),
  agentDefinitionId: z.string().min(1, 'Selecciona un agente'),
  autoCloseHours: z.coerce.number().min(1).max(720).default(72),
  sendFrom: z.string().nullable().default(null),
  sendUntil: z.string().nullable().default(null),
})
type FormData = z.infer<typeof schema>

const countryCodes = [
  { code: '+507', country: 'Panama' },
  { code: '+1', country: 'USA/Canada' },
  { code: '+52', country: 'Mexico' },
  { code: '+57', country: 'Colombia' },
  { code: '+506', country: 'Costa Rica' },
  { code: '+503', country: 'El Salvador' },
  { code: '+502', country: 'Guatemala' },
  { code: '+504', country: 'Honduras' },
  { code: '+505', country: 'Nicaragua' },
  { code: '+56', country: 'Chile' },
  { code: '+54', country: 'Argentina' },
  { code: '+51', country: 'Peru' },
  { code: '+593', country: 'Ecuador' },
  { code: '+58', country: 'Venezuela' },
  { code: '+34', country: 'Espana' },
]

export function CampaignTemplateFormPage() {
  const { id } = useParams<{ id: string }>()
  const isEdit = !!id
  const navigate = useNavigate()

  const { data: existing, isLoading: loadingTemplate, isError: templateError } = useCampaignTemplate(id)
  const { data: agents } = useAgents()
  const { data: labels, isLoading: loadingLabels } = useLabels()
  const { data: availableActions, isLoading: loadingActions } = useAvailableActions()
  const { data: availablePrompts, isLoading: loadingPrompts } = useAvailablePrompts()
  const createMut = useCreateCampaignTemplate()
  const updateMut = useUpdateCampaignTemplate()
  const { data: tenant } = useTenant()

  const [followUpHours, setFollowUpHours] = useState<number[]>(existing?.followUpHours ?? [])
  const [newHour, setNewHour] = useState('')
  const [selectedLabelIds, setSelectedLabelIds] = useState<string[]>(existing?.labelIds ?? [])
  const [selectedActionIds, setSelectedActionIds] = useState<string[]>(existing?.actionIds ?? [])
  const [selectedPromptIds, setSelectedPromptIds] = useState<string[]>(existing?.promptTemplateIds ?? [])
  const [actionConfigs, setActionConfigs] = useState<Record<string, ActionConfig>>(() => {
    const raw = existing?.actionConfigs
    if (!raw) return {}
    if (typeof raw === 'string') {
      try { return JSON.parse(raw) } catch { return {} }
    }
    return raw as Record<string, ActionConfig>
  })
  const [expandedActions, setExpandedActions] = useState<Set<string>>(new Set())
  const [configErrors, setConfigErrors] = useState<Record<string, Record<string, string>>>({})
  const [attentionDays, setAttentionDays] = useState<number[]>(existing?.attentionDays ?? [1, 2, 3, 4, 5])
  const [attentionStart, setAttentionStart] = useState(existing?.attentionStartTime ?? '08:00')
  const [attentionEnd, setAttentionEnd] = useState(existing?.attentionEndTime ?? '17:00')
  const [outOfContextPolicy, setOutOfContextPolicy] = useState(existing?.outOfContextPolicy ?? 'Contain')
  // Webhook Contract Builder — modal state (Fase 5)
  const [webhookBuilderActionId, setWebhookBuilderActionId] = useState<string | null>(null)
  // Tab activa — General / Etiquetas / Acciones / Prompt / Documentos
  const [activeTab, setActiveTab] = useState<'general' | 'labels' | 'actions' | 'prompt' | 'documents'>('general')

  // Sync all state when existing template loads (edit mode)
  useEffect(() => {
    if (!existing) return
    if (existing.followUpHours.length > 0) setFollowUpHours(existing.followUpHours)
    if (existing.labelIds.length > 0) setSelectedLabelIds(existing.labelIds)
    if (existing.actionIds.length > 0) setSelectedActionIds(existing.actionIds)
    if (existing.promptTemplateIds.length > 0) setSelectedPromptIds(existing.promptTemplateIds)
    if (existing.actionConfigs) {
      const raw = existing.actionConfigs
      const parsed: Record<string, ActionConfig> = typeof raw === 'string'
        ? (() => { try { return JSON.parse(raw) } catch { return {} } })()
        : raw as Record<string, ActionConfig>
      if (Object.keys(parsed).length > 0) setActionConfigs(parsed)
    }
    setAttentionDays(existing.attentionDays ?? [1, 2, 3, 4, 5])
    setAttentionStart(existing.attentionStartTime ?? '08:00')
    setAttentionEnd(existing.attentionEndTime ?? '17:00')
    if (existing.outOfContextPolicy) setOutOfContextPolicy(existing.outOfContextPolicy)
  }, [existing?.id])

  const { register, handleSubmit, watch, formState: { errors } } = useForm<FormData>({
    resolver: zodResolver(schema),
    values: isEdit && existing
      ? {
          name: existing.name,
          agentDefinitionId: existing.agentDefinitionId,
          autoCloseHours: existing.autoCloseHours,
          sendFrom: existing.sendFrom ?? null,
          sendUntil: existing.sendUntil ?? null,
        }
      : {
          name: '', agentDefinitionId: '', autoCloseHours: 72,
          sendFrom: null, sendUntil: null,
        },
  })

  const addFollowUp = () => {
    const h = parseInt(newHour)
    if (!h || h < 1 || followUpHours.includes(h)) return
    setFollowUpHours([...followUpHours, h].sort((a, b) => a - b))
    setNewHour('')
  }

  const removeFollowUp = (h: number) => {
    setFollowUpHours(followUpHours.filter(x => x !== h))
  }

  const toggleLabel = (labelId: string) => {
    setSelectedLabelIds(prev =>
      prev.includes(labelId) ? prev.filter(id => id !== labelId) : [...prev, labelId]
    )
  }

  const toggleAction = (actionId: string) => {
    const isSelected = selectedActionIds.includes(actionId)
    if (isSelected) {
      setSelectedActionIds(prev => prev.filter(id => id !== actionId))
      setActionConfigs(prev => { const n = { ...prev }; delete n[actionId]; return n })
      setExpandedActions(prev => { const n = new Set(prev); n.delete(actionId); return n })
      setConfigErrors(prev => { const n = { ...prev }; delete n[actionId]; return n })
    } else {
      setSelectedActionIds(prev => [...prev, actionId])
      const action = availableActions?.find(a => a.id === actionId)
      if (action && (action.requiresWebhook || action.sendsEmail || action.sendsSms)) {
        setExpandedActions(prev => new Set(prev).add(actionId))
        // Pre-llenar email del ejecutivo con el email remitente configurado en el tenant
        if (action.sendsEmail && tenant?.senderEmail) {
          setActionConfigs(prev => ({
            ...prev,
            [actionId]: { ...prev[actionId], emailAddress: prev[actionId]?.emailAddress || tenant.senderEmail! }
          }))
        }
      }
    }
  }

  const togglePrompt = (promptId: string) => {
    setSelectedPromptIds(prev =>
      prev.includes(promptId) ? [] : [promptId]
    )
  }

  const updateActionConfig = (actionId: string, field: keyof ActionConfig, value: string) => {
    setActionConfigs(prev => ({
      ...prev,
      [actionId]: { ...prev[actionId], [field]: value }
    }))
    // Clear field error
    if (configErrors[actionId]?.[field]) {
      setConfigErrors(prev => {
        const n = { ...prev, [actionId]: { ...prev[actionId] } }
        delete n[actionId][field]
        return n
      })
    }
  }

  const validateActionConfigs = (): boolean => {
    const errors: Record<string, Record<string, string>> = {}
    let valid = true

    for (const actionId of selectedActionIds) {
      const action = availableActions?.find(a => a.id === actionId)
      if (!action) continue
      const config = actionConfigs[actionId] ?? {}
      const actionErrors: Record<string, string> = {}

      if (action.requiresWebhook) {
        if (!config.webhookUrl?.trim()) { actionErrors.webhookUrl = 'La URL del webhook es obligatoria'; valid = false }
        if (!config.webhookMethod?.trim()) { actionErrors.webhookMethod = 'El metodo es obligatorio'; valid = false }
      }
      if (action.sendsEmail) {
        if (!config.emailAddress?.trim()) { actionErrors.emailAddress = 'El correo es obligatorio'; valid = false }
        else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(config.emailAddress.trim())) { actionErrors.emailAddress = 'Correo invalido'; valid = false }
      }
      if (action.sendsSms) {
        if (!config.smsPhoneNumber?.trim()) { actionErrors.smsPhoneNumber = 'El numero es obligatorio'; valid = false }
      }

      if (Object.keys(actionErrors).length > 0) {
        errors[actionId] = actionErrors
        setExpandedActions(prev => new Set(prev).add(actionId))
      }
    }

    setConfigErrors(errors)
    return valid
  }

  const onSubmit = (data: FormData) => {
    if (!validateActionConfigs()) return

    const payload = {
      ...data,
      followUpHours,
      labelIds: selectedLabelIds,
      actionIds: selectedActionIds,
      actionConfigs: Object.keys(actionConfigs).length > 0 ? JSON.stringify(actionConfigs) : null,
      promptTemplateIds: selectedPromptIds,
      sendFrom: data.sendFrom || null,
      sendUntil: data.sendUntil || null,
      attentionDays,
      attentionStartTime: attentionStart,
      attentionEndTime: attentionEnd,
      systemPrompt: '',
      maxRetries: 3,
      retryIntervalHours: 24,
      inactivityCloseHours: 72,
      maxTokens: 1024,
      outOfContextPolicy,
    }
    if (isEdit && id) {
      updateMut.mutate({ id, ...payload }, { onSuccess: () => navigate('/campaign-templates') })
    } else {
      createMut.mutate(payload, { onSuccess: () => navigate('/campaign-templates') })
    }
  }

  if (isEdit && loadingTemplate) return <div className="py-12 text-center text-gray-400">Cargando...</div>
  if (isEdit && templateError) return (
    <div className="py-12 text-center">
      <p className="text-red-500 mb-4">Error al cargar la campaña maestro.</p>
      <button onClick={() => window.location.reload()} className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm">Reintentar</button>
    </div>
  )

  const isPending = createMut.isPending || updateMut.isPending
  const inputClass = "mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
  const inputErrClass = "mt-1 block w-full rounded-md border border-red-400 bg-red-50 px-3 py-2 text-sm focus:border-red-500 focus:outline-none focus:ring-1 focus:ring-red-500"

  const hasConfigErrors = Object.keys(configErrors).length > 0

  // Valida si hay errores en los campos generales (nombre, agente, etc.) para
  // marcar el tab "General" con indicador rojo cuando el form falla validación.
  const hasGeneralErrors = !!(errors.name || errors.agentDefinitionId || errors.sendFrom || errors.sendUntil)

  const tabs = [
    { key: 'general' as const, label: 'General', icon: Globe, hasError: hasGeneralErrors },
    { key: 'labels' as const, label: 'Etiquetas', icon: Tag, badge: selectedLabelIds.length },
    { key: 'actions' as const, label: 'Acciones', icon: Zap, badge: selectedActionIds.length, hasError: hasConfigErrors },
    { key: 'prompt' as const, label: 'Prompt', icon: FileText, badge: selectedPromptIds.length },
    { key: 'documents' as const, label: 'Documentos', icon: Paperclip },
  ]

  return (
    <div className="mx-auto max-w-3xl">
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-900">{isEdit ? 'Editar Maestro' : 'Nuevo Maestro de Campana'}</h1>
        <button onClick={() => navigate('/campaign-templates')} className="flex items-center gap-1.5 text-sm text-gray-600 hover:text-gray-900">
          <ArrowLeft className="h-4 w-4" /> Volver
        </button>
      </div>

      {/* ─── Tabs en el encabezado: Etiquetas | Acciones | Prompt | Documentos ─── */}
      <div className="mb-6 rounded-lg bg-white shadow-sm">
        <div className="flex border-b border-gray-200 overflow-x-auto">
          {tabs.map(tab => {
            const Icon = tab.icon
            const isActive = activeTab === tab.key
            return (
              <button
                key={tab.key}
                type="button"
                onClick={() => setActiveTab(tab.key)}
                className={`flex items-center gap-2 px-5 py-3 text-sm font-medium whitespace-nowrap border-b-2 transition-colors ${
                  isActive
                    ? 'border-blue-600 text-blue-700 bg-blue-50/50'
                    : 'border-transparent text-gray-600 hover:text-gray-900 hover:bg-gray-50'
                }`}
              >
                <Icon className={`h-4 w-4 ${isActive ? 'text-blue-600' : 'text-gray-400'}`} />
                <span>{tab.label}</span>
                {'badge' in tab && tab.badge ? (
                  <span className={`ml-1 rounded-full px-1.5 py-0.5 text-[10px] font-semibold ${isActive ? 'bg-blue-600 text-white' : 'bg-gray-200 text-gray-700'}`}>
                    {tab.badge}
                  </span>
                ) : null}
                {'hasError' in tab && tab.hasError ? (
                  <span className="ml-1 h-2 w-2 rounded-full bg-red-500" title="Hay campos con error" />
                ) : null}
              </button>
            )
          })}
        </div>
      </div>

      <form onSubmit={handleSubmit(onSubmit)} className="space-y-6">
        {/* ─── TAB: General (Identificación + horarios + seguimientos + cierre + política) ─── */}
        {activeTab === 'general' && <>

        {/* Seccion 1: Identificacion */}
        <section className="rounded-lg bg-white p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-900">Identificacion</h2>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700">Nombre *</label>
              <input {...register('name')} className={inputClass} placeholder="Cobros Enero 2026" />
              {errors.name && <p className="mt-1 text-xs text-red-600">{errors.name.message}</p>}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Agente IA *</label>
              <select {...register('agentDefinitionId')} className={inputClass}>
                <option value="">-- Seleccionar --</option>
                {agents?.map(a => <option key={a.id} value={a.id}>{a.name} ({a.type})</option>)}
              </select>
              {errors.agentDefinitionId && <p className="mt-1 text-xs text-red-600">{errors.agentDefinitionId.message}</p>}
            </div>
          </div>
        </section>

        {/* Seccion 2: Horario de envio */}
        <section className="rounded-lg bg-white p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-900">Horario de envio</h2>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700">Enviar desde</label>
              <input type="time" {...register('sendFrom')} className={inputClass} />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Enviar hasta</label>
              <input type="time" {...register('sendUntil')} className={inputClass} />
            </div>
          </div>
        </section>

        {/* Seccion: Horario de atencion de asesores */}
        <section className="rounded-lg bg-white p-5 shadow-sm">
          <div className="mb-4 flex items-center gap-2">
            <Clock className="h-4 w-4 text-blue-600" />
            <h2 className="text-sm font-semibold text-gray-900">Horario de atencion de asesores</h2>
          </div>
          <p className="mb-4 text-xs text-gray-500">
            El agente usara este horario para informar al cliente cuando puede hablar con un asesor humano.
          </p>

          {/* Dias de la semana */}
          <div className="mb-4">
            <label className="mb-2 block text-sm font-medium text-gray-700">Dias de atencion</label>
            <div className="flex flex-wrap gap-2">
              {[
                { num: 1, label: 'Lun' }, { num: 2, label: 'Mar' }, { num: 3, label: 'Mie' },
                { num: 4, label: 'Jue' }, { num: 5, label: 'Vie' }, { num: 6, label: 'Sab' }, { num: 0, label: 'Dom' },
              ].map(({ num, label }) => {
                const selected = attentionDays.includes(num)
                return (
                  <button
                    key={num}
                    type="button"
                    onClick={() => setAttentionDays(prev =>
                      prev.includes(num) ? prev.filter(d => d !== num) : [...prev, num].sort()
                    )}
                    className={`rounded-lg px-4 py-2 text-sm font-semibold transition-colors ${
                      selected
                        ? 'bg-blue-600 text-white'
                        : 'border border-gray-300 bg-white text-gray-600 hover:bg-gray-50'
                    }`}
                  >
                    {label}
                  </button>
                )
              })}
            </div>
          </div>

          {/* Hora inicio y fin */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700">Hora de inicio</label>
              <input
                type="time"
                value={attentionStart}
                onChange={e => setAttentionStart(e.target.value)}
                className={inputClass}
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Hora de cierre</label>
              <input
                type="time"
                value={attentionEnd}
                onChange={e => setAttentionEnd(e.target.value)}
                className={inputClass}
              />
            </div>
          </div>
        </section>

        {/* Seccion 3: Seguimientos automaticos */}
        <section className="rounded-lg bg-white p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-900">Seguimientos automaticos</h2>
          <p className="mb-3 text-xs text-gray-500">Define los intervalos de seguimiento en horas despues del primer contacto.</p>
          <div className="mb-3 flex items-center gap-2">
            <input
              type="number" min={1} value={newHour}
              onChange={e => setNewHour(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && (e.preventDefault(), addFollowUp())}
              placeholder="Horas"
              className="w-24 rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            />
            <button type="button" onClick={addFollowUp} className="flex items-center gap-1 rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 transition-colors">
              <Plus className="h-3.5 w-3.5" /> Agregar
            </button>
          </div>
          {followUpHours.length === 0 ? (
            <p className="text-xs text-gray-400">No hay seguimientos definidos.</p>
          ) : (
            <div className="flex flex-wrap gap-2">
              {followUpHours.map(h => (
                <span key={h} className="flex items-center gap-1.5 rounded-full bg-blue-50 px-3 py-1 text-sm text-blue-700">
                  <Clock className="h-3.5 w-3.5" /> {h}h
                  <button type="button" onClick={() => removeFollowUp(h)} className="text-blue-400 hover:text-blue-700"><X className="h-3.5 w-3.5" /></button>
                </span>
              ))}
            </div>
          )}
        </section>

        {/* Seccion 3: Cierre automatico */}
        <section className="rounded-lg bg-white p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-900">Cierre automatico</h2>
          <div className="max-w-xs">
            <label className="block text-sm font-medium text-gray-700">Cerrar campana despues de (horas)</label>
            <input type="number" {...register('autoCloseHours')} min={1} max={720} className={inputClass} />
            {errors.autoCloseHours && <p className="mt-1 text-xs text-red-600">{errors.autoCloseHours.message}</p>}
            <p className="mt-1 text-xs text-gray-500">Horas despues de enviada la campana para cerrar automaticamente.</p>
          </div>
        </section>

        {/* Seccion: Politica del Cerebro — solo visible si BrainEnabled */}
        {tenant?.brainEnabled && (
          <section className="rounded-lg bg-white p-5 shadow-sm">
            <h2 className="mb-4 text-sm font-semibold text-gray-900">Comportamiento del Cerebro ante desvios</h2>
            <p className="mb-3 text-xs text-gray-500">Define que hace el Cerebro cuando el cliente habla de algo fuera del contexto de esta campana.</p>
            <div className="max-w-xs">
              <select
                value={outOfContextPolicy}
                onChange={(e) => setOutOfContextPolicy(e.target.value)}
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
              >
                <option value="Contain">Contener — el agente de esta campana gestiona todo</option>
                <option value="Transfer">Transferir — el Cerebro ruta al agente mas adecuado</option>
              </select>
            </div>
          </section>
        )}

        </>}
        {/* ─── /TAB General ─── */}

        {/* ─── Contenido de los tabs Etiquetas / Acciones / Prompt / Documentos ─── */}
        {activeTab !== 'general' && (
        <div className="rounded-lg bg-white shadow-sm">
          <div className="p-5">
            {/* ─── TAB: Etiquetas ─── */}
            {activeTab === 'labels' && (
        <div>
          <h2 className="mb-4 text-sm font-semibold text-gray-900">Etiquetas de seguimiento</h2>
          <p className="mb-3 text-xs text-gray-500">Selecciona las etiquetas que se usaran para clasificar los resultados de la campana.</p>
          {loadingLabels ? (
            <p className="text-xs text-gray-400">Cargando etiquetas...</p>
          ) : !labels?.length ? (
            <div className="rounded-md bg-amber-50 p-3">
              <p className="text-xs text-amber-700">No hay etiquetas definidas para este tenant.</p>
              <button type="button" onClick={() => navigate('/labels')} className="mt-1 text-xs font-medium text-blue-600 hover:underline">Ir a crear etiquetas</button>
            </div>
          ) : (
            <div className="space-y-2">
              {labels.filter(l => l.isActive).map(label => {
                const selected = selectedLabelIds.includes(label.id)
                return (
                  <label key={label.id} className={`flex cursor-pointer items-center gap-3 rounded-lg border p-3 transition-colors ${selected ? 'border-blue-500 bg-blue-50' : 'border-gray-200 hover:bg-gray-50'}`}>
                    <input type="checkbox" checked={selected} onChange={() => toggleLabel(label.id)} className="h-4 w-4 rounded border-gray-300 text-blue-600" />
                    <span className="h-3 w-3 rounded-full shrink-0" style={{ backgroundColor: label.color }} />
                    <div className="flex-1">
                      <span className="text-sm font-medium text-gray-900">{label.name}</span>
                      {label.keywords.length > 0 && <p className="text-xs text-gray-500">Palabras clave: {label.keywords.join(', ')}</p>}
                    </div>
                  </label>
                )
              })}
              {selectedLabelIds.length > 0 && <p className="mt-2 text-xs text-blue-600">{selectedLabelIds.length} etiqueta{selectedLabelIds.length > 1 ? 's' : ''} seleccionada{selectedLabelIds.length > 1 ? 's' : ''}</p>}
            </div>
          )}
        </div>
            )}

            {/* ─── TAB: Acciones ─── */}
            {activeTab === 'actions' && (
        <div>
          <div className="mb-4 flex items-center gap-2">
            <Zap className="h-5 w-5 text-amber-500" />
            <h2 className="text-sm font-semibold text-gray-900">Acciones vinculadas</h2>
          </div>
          <p className="mb-3 text-xs text-gray-500">Selecciona las acciones y configura sus parametros. Los campos marcados con * son obligatorios.</p>

          {hasConfigErrors && (
            <div className="mb-3 rounded-lg bg-red-50 px-4 py-2 text-sm text-red-600">
              Completa los campos requeridos de las acciones seleccionadas
            </div>
          )}

          {loadingActions ? (
            <p className="text-xs text-gray-400">Cargando acciones...</p>
          ) : !availableActions?.length ? (
            <div className="rounded-md bg-gray-50 p-3">
              <p className="text-xs text-gray-500">No hay acciones disponibles. El administrador debe crearlas desde el portal admin.</p>
            </div>
          ) : (
            <div className="space-y-2">
              {availableActions.map(action => {
                const selected = selectedActionIds.includes(action.id)
                const expanded = expandedActions.has(action.id)
                const needsConfig = action.requiresWebhook || action.sendsEmail || action.sendsSms
                const config = actionConfigs[action.id] ?? {}
                const actionErrs = configErrors[action.id] ?? {}
                const hasErrors = Object.keys(actionErrs).length > 0

                return (
                  <div key={action.id} className={`rounded-lg border overflow-hidden transition-colors ${selected ? (hasErrors ? 'border-red-400 bg-red-50/30' : 'border-amber-500 bg-amber-50') : 'border-gray-200 hover:bg-gray-50'}`}>
                    {/* Action header */}
                    <div
                      className="flex items-center gap-3 p-3 cursor-pointer select-none"
                      onClick={() => toggleAction(action.id)}
                    >
                      <input
                        type="checkbox"
                        checked={selected}
                        readOnly
                        className="h-4 w-4 rounded border-gray-300 text-amber-600 pointer-events-none"
                      />
                      <Zap className="h-4 w-4 shrink-0 text-amber-500" />
                      <div className="flex-1">
                        <div className="flex items-center gap-2">
                          <span className="text-sm font-medium text-gray-900">{getFriendlyName(action.name)}</span>
                          <div className="flex gap-1">
                            {action.requiresWebhook && <span className="rounded bg-purple-100 px-1.5 py-0.5 text-[10px] font-medium text-purple-700">Webhook</span>}
                            {action.sendsEmail && <span className="rounded bg-green-100 px-1.5 py-0.5 text-[10px] font-medium text-green-700">Email</span>}
                            {action.sendsSms && <span className="rounded bg-amber-100 px-1.5 py-0.5 text-[10px] font-medium text-amber-700">SMS</span>}
                            {action.defaultWebhookContract && <span className="rounded bg-indigo-100 px-1.5 py-0.5 text-[10px] font-medium text-indigo-700">Default ⚡</span>}
                          </div>
                        </div>
                        {action.description && <p className="text-xs text-gray-500">{action.description}</p>}
                      </div>
                      {selected && needsConfig && (
                        <button type="button" onClick={(e) => { e.stopPropagation(); setExpandedActions(prev => {
                          const n = new Set(prev)
                          n.has(action.id) ? n.delete(action.id) : n.add(action.id)
                          return n
                        }) }} className="rounded-lg p-1 text-gray-400 hover:bg-gray-100 transition-colors">
                          {expanded ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
                        </button>
                      )}
                    </div>

                    {/* Config panel */}
                    {selected && needsConfig && expanded && (
                      <div className="border-t border-amber-200 bg-amber-50/50 px-4 py-4 space-y-4">

                        {/* Webhook config */}
                        {action.requiresWebhook && (
                          <div className="space-y-3">
                            <div className="flex items-center gap-1.5 text-xs font-semibold text-purple-700 uppercase tracking-wider">
                              <Webhook className="h-3.5 w-3.5" /> Configuracion de Webhook
                            </div>
                            <div className="grid grid-cols-3 gap-3">
                              <div className="col-span-2">
                                <label className="mb-1 block text-xs font-medium text-gray-600">URL del webhook *</label>
                                <input
                                  value={config.webhookUrl ?? ''}
                                  onChange={e => updateActionConfig(action.id, 'webhookUrl', e.target.value)}
                                  placeholder="https://api.ejemplo.com/webhook"
                                  className={actionErrs.webhookUrl ? inputErrClass : inputClass}
                                />
                                {actionErrs.webhookUrl && <p className="mt-1 text-xs text-red-600">{actionErrs.webhookUrl}</p>}
                              </div>
                              <div>
                                <label className="mb-1 block text-xs font-medium text-gray-600">Metodo *</label>
                                <select
                                  value={config.webhookMethod ?? 'POST'}
                                  onChange={e => updateActionConfig(action.id, 'webhookMethod', e.target.value)}
                                  className={actionErrs.webhookMethod ? inputErrClass : inputClass}
                                >
                                  <option value="POST">POST</option>
                                  <option value="GET">GET</option>
                                  <option value="PUT">PUT</option>
                                </select>
                                {actionErrs.webhookMethod && <p className="mt-1 text-xs text-red-600">{actionErrs.webhookMethod}</p>}
                              </div>
                            </div>
                            {!(config.inputSchema || config.outputSchema) && (
                              <>
                                <div>
                                  <label className="mb-1 block text-xs font-medium text-gray-600"><Globe className="inline h-3 w-3 mr-1" />Headers (JSON)</label>
                                  <textarea
                                    value={config.webhookHeaders ?? ''}
                                    onChange={e => updateActionConfig(action.id, 'webhookHeaders', e.target.value)}
                                    rows={2}
                                    placeholder={'{\n  "Authorization": "Bearer token..."\n}'}
                                    className={`${inputClass} font-mono text-xs`}
                                  />
                                </div>
                                <div>
                                  <label className="mb-1 block text-xs font-medium text-gray-600">Plantilla JSON del payload</label>
                                  <textarea
                                    value={config.webhookPayload ?? ''}
                                    onChange={e => updateActionConfig(action.id, 'webhookPayload', e.target.value)}
                                    rows={3}
                                    placeholder={'{\n  "conversationId": "{{conversationId}}",\n  "clientPhone": "{{clientPhone}}"\n}'}
                                    className={`${inputClass} font-mono text-xs`}
                                  />
                                  <p className="mt-1 text-xs text-gray-400">Usa {'{{variable}}'} para campos dinamicos.</p>
                                </div>
                              </>
                            )}

                            {/* Webhook Contract System — Builder avanzado (Fase 5) */}
                            <div className="mt-3 pt-3 border-t border-amber-200">
                              {/* Badge de herencia: si la acción tiene DefaultWebhookContract pero este template no tiene override */}
                              {action.defaultWebhookContract && !(config.inputSchema || config.outputSchema) && (
                                <div className="mb-2 flex items-center gap-1.5 rounded-lg bg-purple-50 border border-purple-200 px-3 py-2 text-[11px] text-purple-800">
                                  <Webhook className="h-3.5 w-3.5 flex-shrink-0" />
                                  <span>
                                    <strong>Hereda contrato default</strong> de la accion. Si necesitas configuracion diferente para este maestro, usa el boton de abajo.
                                  </span>
                                </div>
                              )}
                              <button
                                type="button"
                                onClick={() => setWebhookBuilderActionId(action.id)}
                                className="flex items-center gap-2 rounded-lg bg-indigo-600 px-3 py-2 text-xs font-medium text-white hover:bg-indigo-700"
                              >
                                <Webhook className="h-3.5 w-3.5" />
                                {(config.inputSchema || config.outputSchema)
                                  ? 'Editar contrato de este maestro'
                                  : 'Personalizar contrato para este maestro'}
                                {(config.inputSchema || config.outputSchema) && (
                                  <span className="rounded-full bg-white/20 px-1.5 py-0.5 text-[10px]">
                                    Override
                                  </span>
                                )}
                              </button>
                              <p className="mt-1 text-[10px] text-gray-500">
                                {(config.inputSchema || config.outputSchema)
                                  ? 'Este maestro tiene su propia configuracion de webhook (override del default de la accion).'
                                  : 'Define un contrato especifico para este maestro. Si no lo configuras, se usa el default de la accion.'}
                              </p>
                            </div>
                          </div>
                        )}

                        {/* Email config */}
                        {action.sendsEmail && (
                          <div className="space-y-2">
                            <div className="flex items-center gap-1.5 text-xs font-semibold text-green-700 uppercase tracking-wider">
                              <Mail className="h-3.5 w-3.5" /> Configuracion de Email
                            </div>
                            <div>
                              <label className="mb-1 block text-xs font-medium text-gray-600">Correo electronico *</label>
                              <input
                                type="email"
                                value={config.emailAddress ?? ''}
                                onChange={e => updateActionConfig(action.id, 'emailAddress', e.target.value)}
                                placeholder="notificaciones@empresa.com"
                                className={actionErrs.emailAddress ? inputErrClass : inputClass}
                              />
                              {actionErrs.emailAddress && <p className="mt-1 text-xs text-red-600">{actionErrs.emailAddress}</p>}
                            </div>
                          </div>
                        )}

                        {/* SMS config */}
                        {action.sendsSms && (
                          <div className="space-y-2">
                            <div className="flex items-center gap-1.5 text-xs font-semibold text-amber-700 uppercase tracking-wider">
                              <MessageSquare className="h-3.5 w-3.5" /> Configuracion de SMS
                            </div>
                            <div>
                              <label className="mb-1 block text-xs font-medium text-gray-600">Numero de telefono *</label>
                              <div className="flex gap-2 mt-1">
                                <select
                                  value={(() => {
                                    const phone = config.smsPhoneNumber ?? ''
                                    for (const cc of countryCodes) {
                                      if (phone.startsWith(cc.code)) return cc.code
                                    }
                                    return '+507'
                                  })()}
                                  onChange={e => {
                                    const currentPhone = config.smsPhoneNumber ?? ''
                                    let digits = currentPhone
                                    for (const cc of countryCodes) {
                                      if (currentPhone.startsWith(cc.code)) { digits = currentPhone.slice(cc.code.length); break }
                                    }
                                    updateActionConfig(action.id, 'smsPhoneNumber', e.target.value + digits)
                                  }}
                                  className="w-36 rounded-md border border-gray-300 px-2 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                                >
                                  {countryCodes.map(cc => (
                                    <option key={cc.code} value={cc.code}>{cc.code} {cc.country}</option>
                                  ))}
                                </select>
                                <input
                                  type="tel"
                                  value={(() => {
                                    const phone = config.smsPhoneNumber ?? ''
                                    for (const cc of countryCodes) {
                                      if (phone.startsWith(cc.code)) return phone.slice(cc.code.length)
                                    }
                                    return phone
                                  })()}
                                  onChange={e => {
                                    const currentPhone = config.smsPhoneNumber ?? ''
                                    let code = '+507'
                                    for (const cc of countryCodes) {
                                      if (currentPhone.startsWith(cc.code)) { code = cc.code; break }
                                    }
                                    updateActionConfig(action.id, 'smsPhoneNumber', code + e.target.value.replace(/[\s-]/g, ''))
                                  }}
                                  placeholder="6000-1234"
                                  className={`flex-1 rounded-md border px-3 py-2 text-sm focus:outline-none focus:ring-1 ${actionErrs.smsPhoneNumber ? 'border-red-400 bg-red-50 focus:border-red-500 focus:ring-red-500' : 'border-gray-300 focus:border-blue-500 focus:ring-blue-500'}`}
                                />
                              </div>
                              {actionErrs.smsPhoneNumber && <p className="mt-1 text-xs text-red-600">{actionErrs.smsPhoneNumber}</p>}
                            </div>
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                )
              })}
              {selectedActionIds.length > 0 && (
                <p className="mt-2 text-xs text-amber-600">{selectedActionIds.length} accion{selectedActionIds.length > 1 ? 'es' : ''} vinculada{selectedActionIds.length > 1 ? 's' : ''}</p>
              )}
            </div>
          )}
        </div>
            )}

            {/* ─── TAB: Prompt ─── */}
            {activeTab === 'prompt' && (
        <div>
          <div className="mb-4 flex items-center gap-2">
            <FileText className="h-5 w-5 text-indigo-500" />
            <h2 className="text-sm font-semibold text-gray-900">Prompts vinculados</h2>
          </div>
          <p className="mb-3 text-xs text-gray-500">Selecciona el prompt template que el agente usara para generar mensajes en esta campana. Solo se permite un prompt por maestro.</p>
          {loadingPrompts ? (
            <p className="text-xs text-gray-400">Cargando prompts...</p>
          ) : !availablePrompts?.length ? (
            <div className="rounded-md bg-gray-50 p-3">
              <p className="text-xs text-gray-500">No hay prompts disponibles. El administrador debe crearlos desde el portal admin.</p>
            </div>
          ) : (
            <div className="space-y-2">
              {availablePrompts.map(prompt => {
                const selected = selectedPromptIds.includes(prompt.id)
                return (
                  <label key={prompt.id} className={`flex cursor-pointer items-center gap-3 rounded-lg border p-3 transition-colors ${selected ? 'border-indigo-500 bg-indigo-50' : 'border-gray-200 hover:bg-gray-50'}`}>
                    <input type="radio" name="promptTemplate" checked={selected} onChange={() => togglePrompt(prompt.id)} className="h-4 w-4 border-gray-300 text-indigo-600" />
                    <FileText className="h-4 w-4 shrink-0 text-indigo-500" />
                    <div className="flex-1">
                      <span className="text-sm font-medium text-gray-900">{prompt.name}</span>
                      {prompt.categoryName && <span className="ml-2 rounded-full bg-indigo-100 px-2 py-0.5 text-[10px] font-medium text-indigo-700">{prompt.categoryName}</span>}
                      {prompt.description && <p className="text-xs text-gray-500">{prompt.description}</p>}
                    </div>
                  </label>
                )
              })}
              {selectedPromptIds.length > 0 && <p className="mt-2 text-xs text-indigo-600">Prompt seleccionado</p>}
            </div>
          )}
        </div>
            )}

            {/* ─── TAB: Documentos ─── */}
            {activeTab === 'documents' && (
              isEdit && id ? (
                <CampaignTemplateDocumentsSection templateId={id} />
              ) : (
                <div>
                  <h2 className="mb-2 text-sm font-semibold text-gray-900">Documentos de referencia</h2>
                  <div className="flex items-center gap-2 text-xs text-gray-500">
                    <FileText className="h-4 w-4" />
                    <p>Guarda el maestro primero para poder adjuntar documentos PDF.</p>
                  </div>
                </div>
              )
            )}
          </div>
        </div>
        )}
        {/* ─── /tab content ─── */}

        {/* Actions */}
        <div className="flex justify-end gap-3">
          <button type="button" onClick={() => navigate('/campaign-templates')} className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors">
            Cancelar
          </button>
          <button type="submit" disabled={isPending} className="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors">
            {isPending ? 'Guardando...' : isEdit ? 'Actualizar' : 'Crear maestro'}
          </button>
        </div>
      </form>

      {/* Webhook Contract Builder Modal (Fase 5) */}
      {webhookBuilderActionId && (() => {
        const action = availableActions?.find(a => a.id === webhookBuilderActionId)
        const currentConfig = actionConfigs[webhookBuilderActionId] ?? {}

        const initial: Partial<WebhookContractBundle> = {
          webhookUrl: currentConfig.webhookUrl ?? '',
          webhookMethod: (currentConfig.webhookMethod as WebhookContractBundle['webhookMethod']) ?? 'POST',
          contentType: (currentConfig.contentType as WebhookContractBundle['contentType']) ?? 'application/json',
          structure: (currentConfig.structure as WebhookContractBundle['structure']) ?? 'flat',
          authType: (currentConfig.authType as WebhookContractBundle['authType']) ?? 'None',
          authValue: currentConfig.authValue,
          apiKeyHeaderName: currentConfig.apiKeyHeaderName,
          webhookHeaders: currentConfig.webhookHeaders,
          timeoutSeconds: currentConfig.timeoutSeconds ?? 10,
          inputSchema: currentConfig.inputSchema,
          outputSchema: currentConfig.outputSchema,
          // Action Trigger Protocol (Fase 5) — cargar TriggerConfig si ya existe
          triggerConfig: currentConfig.triggerConfig,
        }

        return (
          <WebhookBuilderModal
            initial={initial}
            actionName={action?.name ?? ''}
            onClose={() => setWebhookBuilderActionId(null)}
            onSave={(bundle) => {
              // Mergear el bundle dentro del actionConfigs[actionId] existente
              const actionId = webhookBuilderActionId
              setActionConfigs(prev => ({
                ...prev,
                [actionId]: {
                  ...prev[actionId],
                  webhookUrl: bundle.webhookUrl,
                  webhookMethod: bundle.webhookMethod,
                  contentType: bundle.contentType,
                  structure: bundle.structure,
                  authType: bundle.authType,
                  authValue: bundle.authValue,
                  apiKeyHeaderName: bundle.apiKeyHeaderName,
                  webhookHeaders: bundle.webhookHeaders,
                  timeoutSeconds: bundle.timeoutSeconds,
                  inputSchema: bundle.inputSchema,
                  outputSchema: bundle.outputSchema,
                  // Action Trigger Protocol (Fase 5) — persistir TriggerConfig
                  triggerConfig: bundle.triggerConfig,
                },
              }))
              setWebhookBuilderActionId(null)
            }}
          />
        )
      })()}
    </div>
  )
}
