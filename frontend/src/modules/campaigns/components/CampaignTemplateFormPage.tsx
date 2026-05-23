import { useState, useEffect } from 'react'
// Mapeo unificado slug → nombre amigable vive en @/shared/actionLabels.
// Cualquier acción nueva se registra ahí — NO duplicar mappings locales.
import { getActionFriendlyName } from '@/shared/actionLabels'

import { useTenant } from '@/shared/hooks/useTenant'
import { useNavigate, useParams } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { ArrowLeft, Plus, X, Clock, Zap, FileText, Webhook, Mail, MessageSquare, ChevronDown, ChevronUp, Globe, Tag, Paperclip, Maximize2 } from 'lucide-react'
import { useAgents } from '@/shared/hooks/useAgents'
import { useLabels } from '@/shared/hooks/useLabels'
import { WebhookBuilderModal } from '@/modules/webhookBuilder/components/WebhookBuilderModal'
import { CampaignTemplateDocumentsSection } from './CampaignTemplateDocumentsSection'
import { EmailTemplateTab, parseItemsConfig, DEFAULT_ITEMS_CONFIG, type ItemsConfigShape } from './EmailTemplateTab'
import { ConfirmDialog } from '@/shared/components/ConfirmDialog'
import { useToast, ToastContainer } from '@/shared/components/Toast'
import { MessageDialog, type MessageDialogKind } from '@/shared/components/MessageDialog'
import type { WebhookContractBundle } from '@/modules/webhookBuilder/types'
import { parseContract } from '@/shared/utils/parseContract'
import {
  useCampaignTemplate,
  useCreateCampaignTemplate,
  useUpdateCampaignTemplate,
  useAvailableActions,
  useAvailablePrompts,
  fetchAvailablePromptDetail,
  type ActionConfig,
  type PrimaryTemplateSwapConflict,
} from '@/shared/hooks/useCampaignTemplates'

const schema = z.object({
  name: z.string().min(1, 'El nombre es requerido'),
  agentDefinitionId: z.string().min(1, 'Selecciona un agente'),
  autoCloseHours: z.coerce.number().min(1).max(720).default(72),
  autoCloseMessage: z.string().nullable().default(null),
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
  const { toasts, remove, toast } = useToast()

  const { data: existing, isLoading: loadingTemplate, isError: templateError } = useCampaignTemplate(id)
  const { data: agents, isLoading: loadingAgents } = useAgents()
  const { data: labels, isLoading: loadingLabels } = useLabels()
  const { data: availableActions, isLoading: loadingActions } = useAvailableActions()
  const { data: availablePrompts, isLoading: loadingPrompts } = useAvailablePrompts()
  const createMut = useCreateCampaignTemplate()
  const updateMut = useUpdateCampaignTemplate()
  const { data: tenant } = useTenant()

  const [followUpHours, setFollowUpHours] = useState<number[]>(existing?.followUpHours ?? [])
  const [newHour, setNewHour] = useState('')
  /** Mensajes paralelos a followUpHours. Inicializa desde JSON existente si está. */
  const [followUpMessages, setFollowUpMessages] = useState<string[]>(() => {
    if (!existing?.followUpMessagesJson) return existing?.followUpHours.map(() => '') ?? []
    try {
      const parsed = JSON.parse(existing.followUpMessagesJson)
      if (Array.isArray(parsed)) return parsed.map(s => String(s ?? ''))
    } catch { /* ignore */ }
    return existing.followUpHours.map(() => '')
  })
  const [selectedLabelIds, setSelectedLabelIds] = useState<string[]>(existing?.labelIds ?? [])
  const [selectedActionIds, setSelectedActionIds] = useState<string[]>(existing?.actionIds ?? [])
  const [selectedPromptIds, setSelectedPromptIds] = useState<string[]>(existing?.promptTemplateIds ?? [])
  // Copia local del prompt — editable por el tenant sin afectar el template global.
  // El runtime prioriza este valor sobre el global en ProcessIncomingMessageCommand.
  const [localSystemPrompt, setLocalSystemPrompt] = useState<string>(existing?.systemPrompt ?? '')
  // Texto original del template al momento de cargar; usado para detectar ediciones locales
  // y para el botón "Re-sincronizar desde template".
  const [sourcePromptText, setSourcePromptText] = useState<string>('')
  const [loadingPromptDetail, setLoadingPromptDetail] = useState(false)
  const [promptSaveMessage, setPromptSaveMessage] = useState<string | null>(null)
  const [promptExpanded, setPromptExpanded] = useState(false)
  const [confirmAction, setConfirmAction] = useState<{
    title: string
    description: string
    confirmLabel: string
    run: () => void | Promise<void>
  } | null>(null)
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
  // Tab activa — General / Etiquetas / Acciones / Prompt / Documentos / Correo
  const [activeTab, setActiveTab] = useState<'general' | 'labels' | 'actions' | 'prompt' | 'documents' | 'email'>('general')
  // Plantilla de correo (Fase 6) — solo se persiste si hay acción con sendsEmail vinculada.
  const [localEmailSubject, setLocalEmailSubject] = useState<string>(existing?.emailSubject ?? '')
  const [localEmailBodyHtml, setLocalEmailBodyHtml] = useState<string>(existing?.emailBodyHtml ?? '')
  // Fase A — mapeo del dataset + umbral corporativo
  const [localItemsConfig, setLocalItemsConfig] = useState<ItemsConfigShape>(() => parseItemsConfig(existing?.itemsConfig ?? null))
  const [localUmbralCorporativo, setLocalUmbralCorporativo] = useState<number>(existing?.umbralCorporativo ?? 10)
  // Datos del archivo modelo (JSON crudo) — null si no se subió nada.
  const [localSampleDataJson, setLocalSampleDataJson] = useState<string | null>(existing?.sampleDataJson ?? null)

  // Sync all state when existing template loads (edit mode)
  useEffect(() => {
    if (!existing) return
    if (existing.followUpHours.length > 0) setFollowUpHours(existing.followUpHours)
    // Sincronizar mensajes paralelos a las horas. Sin esto, el state local arranca
    // con [] cuando el template carga async y los textos previos no aparecen en los inputs,
    // lo que provoca que al guardar se mande null y se pierdan.
    if (existing.followUpMessagesJson) {
      try {
        const parsed = JSON.parse(existing.followUpMessagesJson)
        if (Array.isArray(parsed)) {
          const normalized = parsed.map(s => String(s ?? ''))
          while (normalized.length < existing.followUpHours.length) normalized.push('')
          setFollowUpMessages(normalized.slice(0, existing.followUpHours.length))
        }
      } catch { /* ignore parse errors */ }
    } else if (existing.followUpHours.length > 0) {
      setFollowUpMessages(existing.followUpHours.map(() => ''))
    }
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
    if (existing.systemPrompt) setLocalSystemPrompt(existing.systemPrompt)
    setLocalEmailSubject(existing.emailSubject ?? '')
    setLocalEmailBodyHtml(existing.emailBodyHtml ?? '')
    setLocalItemsConfig(parseItemsConfig(existing.itemsConfig ?? null))
    setLocalUmbralCorporativo(existing.umbralCorporativo ?? 10)
    setLocalSampleDataJson(existing.sampleDataJson ?? null)
  }, [existing?.id])

  // Cuando hay un prompt seleccionado, trae el texto original del template global
  // para mostrar el botón "Re-sincronizar" y detectar si la copia local está modificada.
  useEffect(() => {
    const id = selectedPromptIds[0]
    if (!id) { setSourcePromptText(''); return }
    let cancelled = false
    setLoadingPromptDetail(true)
    fetchAvailablePromptDetail(id)
      .then((d) => { if (!cancelled) setSourcePromptText(d.systemPrompt ?? '') })
      .catch(() => { if (!cancelled) setSourcePromptText('') })
      .finally(() => { if (!cancelled) setLoadingPromptDetail(false) })
    return () => { cancelled = true }
  }, [selectedPromptIds])

  useEffect(() => {
    if (!promptSaveMessage) return
    const t = setTimeout(() => setPromptSaveMessage(null), 3000)
    return () => clearTimeout(t)
  }, [promptSaveMessage])

  // Reactiva al agente seleccionado para mostrar/ocultar el tab "Correo".
  // Lo declaramos antes del useForm porque watch() depende del control,
  // pero queremos que esté arriba de cualquier early-return.
  const { register, handleSubmit, watch, formState: { errors } } = useForm<FormData>({
    resolver: zodResolver(schema),
    values: isEdit && existing
      ? {
          name: existing.name,
          agentDefinitionId: existing.agentDefinitionId,
          autoCloseHours: existing.autoCloseHours,
          autoCloseMessage: existing.autoCloseMessage ?? null,
          sendFrom: existing.sendFrom ?? null,
          sendUntil: existing.sendUntil ?? null,
        }
      : {
          name: '', agentDefinitionId: '', autoCloseHours: 72,
          autoCloseMessage: null,
          sendFrom: null, sendUntil: null,
        },
  })

  // Tab "Correo" se habilita si el agente seleccionado tiene 'Email' en sus canales.
  // El criterio antes era "acción con sendsEmail" pero resultaba muy generico
  // (acciones como SEND_EMAIL_RESUME se enlazan a maestros que en realidad no
  // mandan correo). El canal del agente refleja la intención del usuario.
  const watchedAgentId = watch('agentDefinitionId')
  const selectedAgent = agents?.find(a => a.id === watchedAgentId)
  const hasEmailChannel = (selectedAgent?.enabledChannels ?? []).includes('Email')

  // Si el usuario quita el canal Email del agente mientras está parado en el tab
  // Correo, devolvemos al tab General. Debe estar antes del early-return.
  useEffect(() => {
    if (!hasEmailChannel && activeTab === 'email') setActiveTab('general')
  }, [hasEmailChannel, activeTab])

  const addFollowUp = () => {
    const h = parseInt(newHour)
    if (!h || h < 1 || followUpHours.includes(h)) return
    // Insertamos preservando el orden ascendente; mensajes paralelos quedan alineados.
    const merged = [...followUpHours, h].map((hour, i) => ({ hour, msg: i < followUpHours.length ? followUpMessages[i] ?? '' : '' }))
    merged.sort((a, b) => a.hour - b.hour)
    setFollowUpHours(merged.map(x => x.hour))
    setFollowUpMessages(merged.map(x => x.msg))
    setNewHour('')
  }

  const removeFollowUp = (h: number) => {
    const idx = followUpHours.indexOf(h)
    if (idx < 0) return
    setFollowUpHours(followUpHours.filter((_, i) => i !== idx))
    setFollowUpMessages(followUpMessages.filter((_, i) => i !== idx))
  }

  const updateFollowUpMessage = (index: number, value: string) => {
    setFollowUpMessages(prev => {
      const next = [...prev]
      while (next.length < followUpHours.length) next.push('')
      next[index] = value
      return next
    })
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

  const doChangePrompt = async (promptId: string) => {
    setSelectedPromptIds([promptId])
    try {
      setLoadingPromptDetail(true)
      const detail = await fetchAvailablePromptDetail(promptId)
      setLocalSystemPrompt(detail.systemPrompt ?? '')
      setSourcePromptText(detail.systemPrompt ?? '')
    } catch {
      // silencioso — el usuario puede editar a mano si la carga falla
    } finally {
      setLoadingPromptDetail(false)
    }
  }

  const togglePrompt = (promptId: string) => {
    const wasSelected = selectedPromptIds.includes(promptId)
    // Deseleccionar: no tocamos el texto local — el usuario puede mantenerlo o editarlo.
    if (wasSelected) {
      setSelectedPromptIds([])
      return
    }
    const localHasContent = localSystemPrompt.trim().length > 0
    const localIsModified = localHasContent && localSystemPrompt !== sourcePromptText
    const hadPreviousSelection = selectedPromptIds.length > 0
    // Confirmación elegante si se va a sobrescribir una copia modificada o una selección previa.
    if (localIsModified || hadPreviousSelection) {
      setConfirmAction({
        title: 'Cambiar prompt del maestro',
        description: localIsModified
          ? 'Este maestro tiene un prompt personalizado. Al cambiar de template se sobrescribirá con el contenido del nuevo. ¿Continuar?'
          : 'Vas a cambiar el prompt del maestro. El texto del nuevo template reemplazará al actual. ¿Continuar?',
        confirmLabel: 'Sí, cambiar',
        run: async () => {
          await doChangePrompt(promptId)
        },
      })
      return
    }
    void doChangePrompt(promptId)
  }

  const resyncPromptFromTemplate = () => {
    const id = selectedPromptIds[0]
    if (!id) return
    const doResync = async () => {
      try {
        setLoadingPromptDetail(true)
        const detail = await fetchAvailablePromptDetail(id)
        setLocalSystemPrompt(detail.systemPrompt ?? '')
        setSourcePromptText(detail.systemPrompt ?? '')
        setPromptSaveMessage('Prompt re-sincronizado desde el template.')
      } finally {
        setLoadingPromptDetail(false)
      }
    }
    if (localSystemPrompt !== sourcePromptText && localSystemPrompt.trim().length > 0) {
      setConfirmAction({
        title: 'Re-sincronizar desde template',
        description: 'Vas a reemplazar tus cambios locales con el texto original del template. Esta acción no se puede deshacer. ¿Continuar?',
        confirmLabel: 'Sí, re-sincronizar',
        run: doResync,
      })
      return
    }
    void doResync()
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
        if (!config.smsPhoneNumber?.trim()) { actionErrors.smsPhoneNumber = 'El número es obligatorio'; valid = false }
      }

      if (Object.keys(actionErrors).length > 0) {
        errors[actionId] = actionErrors
        setExpandedActions(prev => new Set(prev).add(actionId))
      }
    }

    setConfigErrors(errors)
    return valid
  }

  // Modal de confirmación cuando el agente ya tiene un maestro primario. El API
  // devuelve 409 con detalles; reintentamos con confirmSwap=true si el admin acepta.
  const [swapConflict, setSwapConflict] = useState<PrimaryTemplateSwapConflict | null>(null)
  const [pendingPayload, setPendingPayload] = useState<Record<string, unknown> | null>(null)

  // Modal para mostrar resultado del save (success/error) — más visible que un toast.
  const [messageDialog, setMessageDialog] = useState<{
    kind: MessageDialogKind
    title: string
    description?: string
    detail?: string
  } | null>(null)

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const extractSwapConflict = (err: any): PrimaryTemplateSwapConflict | null => {
    const data = err?.response?.data
    if (data?.error === 'primary_template_swap_required' && data?.currentPrimaryId) {
      return data as PrimaryTemplateSwapConflict
    }
    return null
  }

  const submitTemplate = (
    payload: Record<string, unknown>,
    confirmSwap: boolean
  ) => {
    const onConflict = (err: unknown) => {
      const conflict = extractSwapConflict(err)
      if (conflict) {
        setSwapConflict(conflict)
        setPendingPayload(payload)
        return
      }
      // El interceptor global de axios (errorInterceptor.ts) ya mostró una
      // modal con el mensaje de validación del backend. No duplicamos.
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      if ((err as any)?.handledByGlobalDialog) {
        console.error('[CampaignTemplate save error — handled by global dialog]', err)
        return
      }
      // Fallback por si el interceptor no se activó (ej. error muy raro sin
      // body): mostrar igual modal local para que el usuario no quede colgado.
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const anyErr = err as any
      const status = anyErr?.response?.status
      const apiMsg = anyErr?.response?.data?.error || anyErr?.response?.data?.title || anyErr?.message
      const detailJson = anyErr?.response?.data
        ? JSON.stringify(anyErr.response.data, null, 2)
        : (anyErr?.stack as string | undefined)
      console.error('[CampaignTemplate save error]', err)
      setMessageDialog({
        kind: 'error',
        title: 'No se pudo guardar el maestro',
        description: apiMsg
          ? `${status ? `HTTP ${status} — ` : ''}${apiMsg}`
          : `${status ? `HTTP ${status} — ` : ''}Error inesperado al guardar. Verificá los datos e intentá de nuevo.`,
        detail: detailJson,
      })
    }
    const onSaveOk = () => {
      // Mostramos modal de éxito en vez de navegar silencioso, así el usuario
      // ve la confirmación. Al cerrar se redirige al listado.
      setMessageDialog({
        kind: 'success',
        title: isEdit ? 'Maestro actualizado' : 'Maestro creado',
        description: isEdit
          ? 'Los cambios se guardaron correctamente.'
          : 'El maestro fue creado y ya está disponible para campañas.',
      })
    }
    if (isEdit && id) {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      updateMut.mutate(
        { id, confirmSwap, ...(payload as any) },
        { onSuccess: onSaveOk, onError: onConflict }
      )
    } else {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      createMut.mutate(
        { confirmSwap, ...(payload as any) },
        { onSuccess: onSaveOk, onError: onConflict }
      )
    }
  }

  const onSubmit = (data: FormData) => {
    if (!validateActionConfigs()) return

    // El SystemPrompt es OBLIGATORIO — el CampaignTemplate es la única fuente
    // del prompt del agente. Sin esto, las campañas que usen este maestro
    // responden con canned + escalación humana, no con el agente IA.
    if (!localSystemPrompt.trim()) {
      toast.error('El SystemPrompt es obligatorio. Sin él, el agente no podrá responder a los clientes de esta campaña.')
      return
    }

    // Solo serializar followUpMessagesJson si hay al menos un mensaje no vacío;
    // si todos están vacíos, lo dejamos en null para no inyectar seguimientos al executor.
    const trimmedMessages = followUpMessages.slice(0, followUpHours.length).map(m => m ?? '')
    const followUpMessagesJson = trimmedMessages.some(m => m.trim().length > 0)
      ? JSON.stringify(trimmedMessages)
      : null

    const payload = {
      ...data,
      followUpHours,
      followUpMessagesJson,
      autoCloseMessage: data.autoCloseMessage?.trim() ? data.autoCloseMessage : null,
      labelIds: selectedLabelIds,
      actionIds: selectedActionIds,
      actionConfigs: Object.keys(actionConfigs).length > 0 ? JSON.stringify(actionConfigs) : null,
      promptTemplateIds: selectedPromptIds,
      sendFrom: data.sendFrom || null,
      sendUntil: data.sendUntil || null,
      attentionDays,
      attentionStartTime: attentionStart,
      attentionEndTime: attentionEnd,
      systemPrompt: localSystemPrompt,
      maxRetries: 3,
      retryIntervalHours: 24,
      inactivityCloseHours: 72,
      maxTokens: 1024,
      outOfContextPolicy,
      // Plantilla de correo (Fase 6). Si el agente no tiene canal Email habilitado
      // mandamos null para limpiar configuración previa que ya no aplica.
      emailSubject: hasEmailChannel && localEmailSubject.trim() ? localEmailSubject : null,
      emailBodyHtml: hasEmailChannel && localEmailBodyHtml.trim() ? localEmailBodyHtml : null,
      emailBodyText: null,
      // Fase A — solo se persisten si hay canal email habilitado. Si no, defaults
      // razonables (10 + null) para no contaminar maestros sin email.
      umbralCorporativo: hasEmailChannel ? Math.max(1, localUmbralCorporativo) : 10,
      itemsConfig: hasEmailChannel && JSON.stringify(localItemsConfig) !== JSON.stringify(DEFAULT_ITEMS_CONFIG)
        ? JSON.stringify(localItemsConfig)
        : null,
      sampleDataJson: hasEmailChannel ? localSampleDataJson : null,
    }
    submitTemplate(payload, /*confirmSwap*/ false)
  }

  const handleConfirmSwap = () => {
    if (!pendingPayload) return
    setSwapConflict(null)
    submitTemplate(pendingPayload, /*confirmSwap*/ true)
    setPendingPayload(null)
  }

  const handleCancelSwap = () => {
    setSwapConflict(null)
    setPendingPayload(null)
  }

  // No renderizar el form hasta tener: (a) el template existente si es edit,
  // y (b) la lista de agentes — sin esa lista el <select> de Agente IA
  // muestra "-- Seleccionar --" en vez del agente vinculado porque la option
  // correspondiente al value todavía no existe en el DOM.
  if ((isEdit && loadingTemplate) || loadingAgents)
    return <div className="py-12 text-center text-gray-400">Cargando...</div>
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

  // Tab "Acciones" — el usuario del tenant elige qué acciones de las habilitadas
  // por el super admin (Tenant.AssignedActionIds) se activan en ESTE maestro.
  // Va como último tab para que el flujo natural sea: configurar general → labels
  // → prompt → documentos → correo → y finalmente decidir qué acciones se disparan.
  const tabs = [
    { key: 'general' as const, label: 'General', icon: Globe, hasError: hasGeneralErrors },
    { key: 'labels' as const, label: 'Etiquetas', icon: Tag, badge: selectedLabelIds.length },
    { key: 'prompt' as const, label: 'Prompt', icon: FileText, badge: selectedPromptIds.length },
    { key: 'documents' as const, label: 'Documentos', icon: Paperclip },
    ...(hasEmailChannel
      ? [{ key: 'email' as const, label: 'Correo', icon: Mail, badge: localEmailBodyHtml ? 1 : 0 }]
      : []),
    // Badge: solo contamos las acciones que ESTÁN en availableActions (las asignadas
    // al tenant). Si selectedActionIds tiene Ids huérfanos (acciones borradas o
    // desasignadas del tenant), no se muestran en la lista pero quedarían contados
    // por error. El filtro garantiza que el badge refleje el número real de checks.
    { key: 'actions' as const, label: 'Acciones', icon: Zap,
      badge: selectedActionIds.filter(id => availableActions?.some(a => a.id === id)).length },
  ]

  return (
    <div className="mx-auto max-w-3xl">
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-900">{isEdit ? 'Editar Maestro' : 'Nuevo Maestro de Campaña'}</h1>
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

        {/* Seccion 2: Horario de envío */}
        <section className="rounded-lg bg-white p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-900">Horario de envío</h2>
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
          <h2 className="mb-1 text-sm font-semibold text-gray-900">Seguimientos automaticos</h2>
          <p className="mb-3 text-xs text-gray-500">
            Define los intervalos en horas después del primer contacto. Cada intervalo puede tener su propio mensaje.
            Variables disponibles: <code className="rounded bg-gray-100 px-1 text-[11px]">{'{nombre}'}</code>{' '}
            <code className="rounded bg-gray-100 px-1 text-[11px]">{'{poliza}'}</code>{' '}
            <code className="rounded bg-gray-100 px-1 text-[11px]">{'{aseguradora}'}</code>{' '}
            <code className="rounded bg-gray-100 px-1 text-[11px]">{'{monto_pendiente}'}</code>.
          </p>
          <div className="mb-4 flex items-center gap-2">
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
            <div className="space-y-3">
              {followUpHours.map((h, idx) => (
                <div key={h} className="rounded-md border border-gray-200 bg-gray-50 p-3">
                  <div className="mb-2 flex items-center justify-between">
                    <span className="flex items-center gap-1.5 rounded-full bg-blue-100 px-2.5 py-0.5 text-xs font-medium text-blue-700">
                      <Clock className="h-3.5 w-3.5" /> Seguimiento {idx + 1} · a las {h}h
                    </span>
                    <button
                      type="button"
                      onClick={() => removeFollowUp(h)}
                      title="Quitar este seguimiento"
                      className="rounded p-1 text-gray-400 hover:bg-gray-200 hover:text-red-600"
                    >
                      <X className="h-3.5 w-3.5" />
                    </button>
                  </div>
                  <textarea
                    value={followUpMessages[idx] ?? ''}
                    onChange={e => updateFollowUpMessage(idx, e.target.value)}
                    rows={3}
                    placeholder={`Mensaje a enviar a las ${h}h. Ej: "Hola {nombre}, queriamos saber si recibiste nuestro mensaje sobre la poliza {poliza}."`}
                    className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                  />
                  <p className="mt-1 text-[11px] text-gray-500">
                    Si dejas el mensaje vacio, este seguimiento se omitira.
                  </p>
                </div>
              ))}
            </div>
          )}
        </section>

        {/* Seccion 3: Cierre automático */}
        <section className="rounded-lg bg-white p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-900">Cierre automático</h2>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <div>
              <label className="block text-sm font-medium text-gray-700">Cerrar campaña después de (horas)</label>
              <input type="number" {...register('autoCloseHours')} min={1} max={720} className={inputClass} />
              {errors.autoCloseHours && <p className="mt-1 text-xs text-red-600">{errors.autoCloseHours.message}</p>}
              <p className="mt-1 text-xs text-gray-500">Horas después de enviada la campaña para cerrar automáticamente.</p>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Mensaje al cerrar (opcional)</label>
              <textarea
                {...register('autoCloseMessage')}
                rows={3}
                placeholder='Ej: "Cerramos esta gestión por inactividad. Si necesitas algo escribenos."'
                className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
              />
              <p className="mt-1 text-xs text-gray-500">Vacio = cierra sin enviar mensaje al cliente.</p>
            </div>
          </div>
        </section>

        {/* Seccion: Politica del Cerebro — solo visible si BrainEnabled */}
        {tenant?.brainEnabled && (
          <section className="rounded-lg bg-white p-5 shadow-sm">
            <h2 className="mb-4 text-sm font-semibold text-gray-900">Comportamiento del Cerebro ante desvios</h2>
            <p className="mb-3 text-xs text-gray-500">Define que hace el Cerebro cuando el cliente habla de algo fuera del contexto de esta campaña.</p>
            <div className="max-w-xs">
              <select
                value={outOfContextPolicy}
                onChange={(e) => setOutOfContextPolicy(e.target.value)}
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
              >
                <option value="Contain">Contener — el agente de esta campaña gestiona todo</option>
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
          <p className="mb-3 text-xs text-gray-500">Selecciona las etiquetas que se usaran para clasificar los resultados de la campaña.</p>
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
                          <span className="text-sm font-medium text-gray-900">{getActionFriendlyName(action.name)}</span>
                          <div className="flex gap-1">
                            {action.requiresWebhook && <span className="rounded bg-purple-100 px-1.5 py-0.5 text-[10px] font-medium text-purple-700">Webhook</span>}
                            {action.sendsEmail && <span className="rounded bg-green-100 px-1.5 py-0.5 text-[10px] font-medium text-green-700">Email</span>}
                            {action.sendsSms && <span className="rounded bg-amber-100 px-1.5 py-0.5 text-[10px] font-medium text-amber-700">SMS</span>}
                            {action.defaultWebhookContract && <span className="rounded bg-green-100 px-1.5 py-0.5 text-[10px] font-medium text-green-700">Configurado ✓</span>}
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

                        {/* Webhook config — modo SOLO LECTURA desde el tenant.
                            Si la acción tiene DefaultWebhookContract significa que el super admin
                            ya configuró este webhook para el cliente. El tenant solo puede VER el
                            contrato (botón "Ver contrato configurado") nunca modificarlo.
                            Si no hay default contract (caso raro: acción asignada pero sin configurar),
                            mostramos un aviso para que solicite al admin la configuración. */}
                        {action.requiresWebhook && (
                          <div className="space-y-3">
                            {action.defaultWebhookContract ? (
                              <>
                                <div className="flex items-center gap-2 rounded-lg bg-green-50 border border-green-200 px-3 py-2.5 text-xs text-green-800">
                                  <Webhook className="h-4 w-4 flex-shrink-0 text-green-600" />
                                  <span>
                                    <strong>Configurado por el administrador.</strong> Este webhook fue parametrizado en el panel de administración para este cliente.
                                    Si necesitas cambios, contactá al super administrador.
                                  </span>
                                </div>
                                <button
                                  type="button"
                                  onClick={() => setWebhookBuilderActionId(action.id)}
                                  className="flex items-center gap-2 rounded-lg border border-gray-300 bg-white px-3 py-2 text-xs font-medium text-gray-700 hover:bg-gray-50"
                                >
                                  <Webhook className="h-3.5 w-3.5" />
                                  Ver contrato configurado
                                </button>
                              </>
                            ) : (
                              <div className="flex items-center gap-2 rounded-lg bg-amber-50 border border-amber-200 px-3 py-2.5 text-xs text-amber-800">
                                <Webhook className="h-4 w-4 flex-shrink-0 text-amber-600" />
                                <span>
                                  <strong>Sin configurar.</strong> Esta acción aún no tiene el webhook configurado.
                                  Solicita al super administrador que lo configure desde el panel de administración antes de usarla.
                                </span>
                              </div>
                            )}
                          </div>
                        )}

                        {/* Email config */}
                        {action.sendsEmail && (
                          <div className="space-y-2">
                            <div className="flex items-center gap-1.5 text-xs font-semibold text-green-700 uppercase tracking-wider">
                              <Mail className="h-3.5 w-3.5" /> Configuración de Email
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
                              <MessageSquare className="h-3.5 w-3.5" /> Configuración de SMS
                            </div>
                            <div>
                              <label className="mb-1 block text-xs font-medium text-gray-600">Número de teléfono *</label>
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
                <p className="mt-2 text-xs text-amber-600">{selectedActionIds.length} acción{selectedActionIds.length > 1 ? 'es' : ''} vinculada{selectedActionIds.length > 1 ? 's' : ''}</p>
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
          <p className="mb-3 text-xs text-gray-500">
            Selecciona el prompt template que servirá de base. Podés editar el texto debajo — tus cambios
            quedan guardados en este maestro y no afectan al template original ni a otros maestros.
          </p>
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
            </div>
          )}

          {/* Editor de la copia local del prompt */}
          {selectedPromptIds.length > 0 && (() => {
            const source = availablePrompts?.find(p => p.id === selectedPromptIds[0])
            const isModified = localSystemPrompt !== sourcePromptText
            return (
              <div className="mt-6 rounded-lg border border-gray-200 bg-white p-4">
                <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                  <div className="flex items-center gap-2">
                    <h3 className="text-sm font-semibold text-gray-900">Prompt del maestro (copia editable)</h3>
                    {source && (
                      <span className="rounded-full bg-indigo-100 px-2 py-0.5 text-[10px] font-medium text-indigo-700">
                        Copia de: {source.name}
                      </span>
                    )}
                    {isModified && (
                      <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-medium text-amber-700">
                        Editado localmente
                      </span>
                    )}
                  </div>
                  <div className="flex items-center gap-2">
                    <button
                      type="button"
                      onClick={() => setPromptExpanded(true)}
                      className="inline-flex items-center gap-1 rounded-md border border-gray-300 px-3 py-1 text-xs font-medium text-gray-700 hover:bg-gray-50"
                      title="Ver y editar el prompt en una ventana grande."
                    >
                      <Maximize2 className="h-3.5 w-3.5" /> Expandir
                    </button>
                    <button
                      type="button"
                      onClick={resyncPromptFromTemplate}
                      disabled={loadingPromptDetail}
                      className="rounded-md border border-gray-300 px-3 py-1 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                      title="Vuelve a copiar el texto original del template, descartando tus cambios locales."
                    >
                      Re-sincronizar desde template
                    </button>
                  </div>
                </div>
                <textarea
                  value={localSystemPrompt}
                  onChange={(e) => setLocalSystemPrompt(e.target.value)}
                  placeholder={loadingPromptDetail ? 'Cargando texto del template...' : 'El agente usará este texto en todas las conversaciones de este maestro.'}
                  rows={12}
                  className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm font-mono focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                />
                <p className="mt-1 text-xs text-gray-500">
                  {localSystemPrompt.length} caracteres · Cambios se guardan al presionar {isEdit ? 'Actualizar' : 'Crear maestro'}.
                </p>
                {promptSaveMessage && (
                  <p className="mt-2 text-xs text-emerald-700">{promptSaveMessage}</p>
                )}
              </div>
            )
          })()}

          {/* Modal expandido: edición a pantalla completa */}
          {promptExpanded && selectedPromptIds.length > 0 && (() => {
            const source = availablePrompts?.find(p => p.id === selectedPromptIds[0])
            const isModified = localSystemPrompt !== sourcePromptText
            return (
              <div
                className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
                onKeyDown={(e) => { if (e.key === 'Escape') setPromptExpanded(false) }}
              >
                <div className="flex h-[92vh] w-full max-w-6xl flex-col overflow-hidden rounded-xl bg-white shadow-2xl">
                  <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
                    <div className="flex items-center gap-2">
                      <FileText className="h-5 w-5 text-indigo-500" />
                      <h2 className="text-base font-semibold text-gray-900">Prompt del maestro (copia editable)</h2>
                      {source && (
                        <span className="rounded-full bg-indigo-100 px-2 py-0.5 text-[10px] font-medium text-indigo-700">
                          Copia de: {source.name}
                        </span>
                      )}
                      {isModified && (
                        <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-medium text-amber-700">
                          Editado localmente
                        </span>
                      )}
                    </div>
                    <div className="flex items-center gap-2">
                      <button
                        type="button"
                        onClick={resyncPromptFromTemplate}
                        disabled={loadingPromptDetail}
                        className="rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                      >
                        Re-sincronizar desde template
                      </button>
                      <button
                        type="button"
                        onClick={() => setPromptExpanded(false)}
                        className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
                        title="Cerrar"
                      >
                        <X className="h-5 w-5" />
                      </button>
                    </div>
                  </div>
                  <div className="flex-1 overflow-hidden p-4">
                    <textarea
                      value={localSystemPrompt}
                      onChange={(e) => setLocalSystemPrompt(e.target.value)}
                      placeholder={loadingPromptDetail ? 'Cargando texto del template...' : 'El agente usará este texto en todas las conversaciones de este maestro.'}
                      className="h-full w-full resize-none rounded-md border border-gray-300 px-3 py-2 text-sm font-mono leading-relaxed focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                    />
                  </div>
                  <div className="flex items-center justify-between border-t border-gray-200 bg-gray-50 px-6 py-3">
                    <p className="text-xs text-gray-500">
                      {localSystemPrompt.length} caracteres · Los cambios se guardan al presionar {isEdit ? 'Actualizar' : 'Crear maestro'}.
                    </p>
                    <button
                      type="button"
                      onClick={() => setPromptExpanded(false)}
                      className="rounded-md border border-gray-300 bg-white px-4 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50"
                    >
                      Cerrar sin guardar
                    </button>
                    <button
                      type="button"
                      onClick={() => {
                        setPromptExpanded(false)
                        handleSubmit(onSubmit)()
                      }}
                      disabled={updateMut.isPending || createMut.isPending}
                      className="flex items-center gap-2 rounded-md bg-indigo-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
                    >
                      {(updateMut.isPending || createMut.isPending) && <span className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-white/50 border-t-white" />}
                      Guardar y cerrar
                    </button>
                  </div>
                </div>
              </div>
            )
          })()}
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

            {/* ─── TAB: Correo (plantilla HTML personalizable) ─── */}
            {activeTab === 'email' && hasEmailChannel && (
              <EmailTemplateTab
                subject={localEmailSubject}
                htmlBody={localEmailBodyHtml}
                itemsConfig={localItemsConfig}
                umbralCorporativo={localUmbralCorporativo}
                sampleDataJson={localSampleDataJson}
                onSubjectChange={setLocalEmailSubject}
                onHtmlChange={setLocalEmailBodyHtml}
                onItemsConfigChange={setLocalItemsConfig}
                onUmbralChange={setLocalUmbralCorporativo}
                onSampleDataChange={setLocalSampleDataJson}
              />
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

      {/* Webhook Contract Builder Modal (modo solo-consulta desde el tenant).
          MISMA FUENTE de datos que el panel del super admin (TenantActionsConfigTab)
          y la lista del tenant (TenantActionsPage): usa el helper compartido
          `parseContract` para deserializar `action.defaultWebhookContract`. Sin esto,
          los campos saldrían vacíos aunque el admin haya configurado todo. */}
      {webhookBuilderActionId && (() => {
        const action = availableActions?.find(a => a.id === webhookBuilderActionId)
        const initial: Partial<WebhookContractBundle> = parseContract(action?.defaultWebhookContract)

        return (
          <WebhookBuilderModal
            initial={initial}
            actionName={action?.name ?? ''}
            availableSlugs={(availableActions ?? [])
              .filter((a) => a.requiresWebhook && a.id !== webhookBuilderActionId)
              .map((a) => a.name)}
            // El editor de maestro de campaña permite ver el contrato vigente de la acción
            // pero NO editarlo desde el lado del tenant. La edición del contrato se hace
            // exclusivamente desde "Editar Cliente → Webhooks" (panel del super admin).
            // El backend también bloquea con 403 si llega un PUT desde el tenant.
            readOnly
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

      {/* Confirmación elegante para cambios destructivos del prompt */}
      <ConfirmDialog
        open={!!confirmAction}
        onClose={() => setConfirmAction(null)}
        onConfirm={() => {
          const action = confirmAction
          setConfirmAction(null)
          if (action) void action.run()
        }}
        title={confirmAction?.title ?? ''}
        description={confirmAction?.description ?? ''}
        confirmLabel={confirmAction?.confirmLabel ?? 'Confirmar'}
        variant="danger"
      />

      {/* Confirmación de swap de maestro primario.
          Surge cuando el agente seleccionado ya tiene un maestro primario y
          el admin intentó crear/editar otro para el mismo agente. Al aceptar,
          el maestro anterior pierde el flag IsPrimaryForAgent (NO se desactiva
          — las campañas vivas siguen funcionando con su prompt actual). */}
      <ConfirmDialog
        open={!!swapConflict}
        onClose={handleCancelSwap}
        onConfirm={handleConfirmSwap}
        title="Cambiar maestro primario del agente"
        description={
          swapConflict
            ? `Este agente ya tiene un maestro primario asignado: "${swapConflict.currentPrimaryName}". `
              + 'Si continúas, ese maestro perderá el rol primario y este pasará a ser el que '
              + 'responde a los mensajes orgánicos (sin campaña activa). Las campañas vivas del '
              + 'maestro anterior seguirán funcionando sin cambios.'
            : ''
        }
        confirmLabel="Sí, cambiar primario"
        variant="default"
      />
      <ToastContainer toasts={toasts} onRemove={remove} />

      <MessageDialog
        open={!!messageDialog}
        onClose={() => {
          const wasSuccess = messageDialog?.kind === 'success'
          setMessageDialog(null)
          // Al confirmar el modal de éxito, volvemos al listado.
          if (wasSuccess) navigate('/campaign-templates')
        }}
        kind={messageDialog?.kind ?? 'info'}
        title={messageDialog?.title ?? ''}
        description={messageDialog?.description}
        detail={messageDialog?.detail}
      />
    </div>
  )
}
