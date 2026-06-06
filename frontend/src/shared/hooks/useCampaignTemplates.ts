import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'

export interface AvailableAction {
  id: string
  name: string
  description: string | null
  requiresWebhook: boolean
  sendsEmail: boolean
  sendsSms: boolean
  /** JSON del DefaultWebhookContract — si existe, los templates heredan sin config propia */
  defaultWebhookContract?: string | null
}

export interface AvailablePrompt {
  id: string
  name: string
  description: string | null
  categoryName: string | null
}

export interface CampaignTemplate {
  id: string
  name: string
  agentDefinitionId: string
  agentName: string | null
  /** true si el agente asociado está activo. null = no se pudo resolver. */
  agentIsActive: boolean | null
  followUpHours: number[]
  /** JSON array de mensajes de seguimiento, paralelo a followUpHours (Fase 2). NULL = sin seguimientos automáticos. */
  followUpMessagesJson: string | null
  /** JSON array de IDs de plantilla Meta de seguimiento, paralelo a followUpHours. NULL si no aplica. */
  followUpTemplateIdsJson: string | null
  autoCloseHours: number
  /** Mensaje enviado al cerrar automáticamente la campaña (Fase 2). NULL = cerrar sin avisar. */
  autoCloseMessage: string | null
  labelIds: string[]
  sendEmail: boolean
  emailAddress: string | null
  actionIds: string[]
  actionConfigs: Record<string, ActionConfig> | null
  promptTemplateIds: string[]
  systemPrompt: string
  sendFrom: string | null
  sendUntil: string | null
  maxRetries: number
  retryIntervalHours: number
  inactivityCloseHours: number
  closeConditionKeyword: string | null
  maxTokens: number
  attentionDays: number[]
  attentionStartTime: string
  attentionEndTime: string
  outOfContextPolicy: string
  /** Plantilla de correo personalizable — null si el maestro no la tiene configurada. */
  emailSubject: string | null
  emailBodyHtml: string | null
  emailBodyText: string | null
  emailTemplateUpdatedAt: string | null
  /** Umbral para cambiar a layout corporativo. Default 10. */
  umbralCorporativo: number
  /** JSON con el mapeo de columnas del archivo a los slots del email
   *  (label, titleColumn, subtitleColumn, categoryColumn, amountColumn, detailColumns[]). */
  itemsConfig: string | null
  /** Datos de muestra (archivo modelo parseado) — array JSON. */
  sampleDataJson: string | null
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export interface CampaignTemplatePayload {
  name: string
  agentDefinitionId: string
  followUpHours: number[]
  followUpMessagesJson?: string | null
  followUpTemplateIdsJson?: string | null
  autoCloseHours: number
  autoCloseMessage?: string | null
  labelIds: string[]
  sendEmail?: boolean
  emailAddress?: string | null
  actionIds: string[]
  actionConfigs?: string | null
  promptTemplateIds: string[]
  systemPrompt: string
  sendFrom?: string | null
  sendUntil?: string | null
  maxRetries: number
  retryIntervalHours: number
  inactivityCloseHours: number
  closeConditionKeyword?: string | null
  maxTokens: number
  attentionDays?: number[]
  attentionStartTime?: string
  attentionEndTime?: string
  outOfContextPolicy?: string
  emailSubject?: string | null
  emailBodyHtml?: string | null
  emailBodyText?: string | null
  umbralCorporativo?: number
  itemsConfig?: string | null
  sampleDataJson?: string | null
}

export interface ActionConfig {
  webhookUrl?: string
  webhookMethod?: string
  webhookHeaders?: string
  webhookPayload?: string
  emailAddress?: string
  smsPhoneNumber?: string

  // ── Webhook Contract System (Fase 5) ──
  // Extensiones opcionales para el contrato tipificado.
  // Se guardan dentro del mismo JSON actionConfigs[actionId] sin romper los campos legacy.
  // Tipado como `any` para evitar import cíclico con el módulo webhookBuilder.
  contentType?: string
  structure?: string
  authType?: string
  authValue?: string
  apiKeyHeaderName?: string
  timeoutSeconds?: number
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  inputSchema?: any
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  outputSchema?: any

  // ── Action Trigger Protocol (Fase 5) ──
  // Metadata que define cuándo el agente IA debe disparar esta acción.
  // Se persiste junto al resto del bundle.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  triggerConfig?: any
}

export function useCampaignTemplates() {
  return useQuery({
    queryKey: ['campaign-templates'],
    queryFn: async () => {
      const { data } = await api.get<CampaignTemplate[]>('/campaign-templates')
      return data
    },
  })
}

export function useCampaignTemplate(id: string | undefined) {
  return useQuery({
    queryKey: ['campaign-templates', id],
    queryFn: async () => {
      const { data } = await api.get<CampaignTemplate>(`/campaign-templates/${id}`)
      return data
    },
    enabled: !!id,
  })
}

/**
 * Detalles del 409 que devuelve el API cuando el agente ya tiene un maestro
 * primario y el admin no confirmó el swap. La UI lo usa para mostrar la modal.
 */
export interface PrimaryTemplateSwapConflict {
  error: 'primary_template_swap_required'
  message: string
  currentPrimaryId: string
  currentPrimaryName: string
}

export function useCreateCampaignTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ confirmSwap, ...payload }: CampaignTemplatePayload & { confirmSwap?: boolean }) => {
      const qs = confirmSwap ? '?confirmSwap=true' : ''
      const { data } = await api.post(`/campaign-templates${qs}`, payload)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['campaign-templates'] }),
  })
}

export function useUpdateCampaignTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, confirmSwap, ...payload }: CampaignTemplatePayload & { id: string; confirmSwap?: boolean }) => {
      const qs = confirmSwap ? '?confirmSwap=true' : ''
      const { data } = await api.put(`/campaign-templates/${id}${qs}`, payload)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['campaign-templates'] }),
  })
}

export function useDeleteCampaignTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const { data } = await api.delete(`/campaign-templates/${id}`)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['campaign-templates'] }),
  })
}

/**
 * Alternativa al delete cuando hay campañas vinculadas: deja IsActive=false
 * y limpia IsPrimaryForAgent sin borrar el registro.
 */
export function useDeactivateCampaignTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const { data } = await api.post(`/campaign-templates/${id}/deactivate`)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['campaign-templates'] }),
  })
}

/**
 * Detalles del 409 al borrar un maestro vinculado a campañas. La UI lo usa
 * para mostrar la modal y ofrecer inactivar.
 */
export interface DeleteTemplateBlockedConflict {
  error: string
  totalCampaigns: number
  campaigns: { id: string; name: string; status: string; createdAt: string }[]
  suggestion: 'deactivate'
  templateName: string
}

export function useDuplicateCampaignTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, name }: { id: string; name: string }) => {
      const { data } = await api.post(`/campaign-templates/${id}/duplicate`, { name })
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['campaign-templates'] }),
  })
}

export interface EmailPreviewResult {
  subject: string
  htmlBody: string
  textBody: string | null
}

export interface EmailRenderInput {
  subject?: string
  htmlBody?: string
  textBody?: string
  itemsConfig?: string | null
  umbralCorporativo?: number
  /** Datos del archivo modelo del maestro (array JSON). Si está, los previews
   *  se renderizan con esos datos en vez del sample hardcoded. */
  sampleDataJson?: string | null
}

export interface ParseEmailSampleResult {
  columns: string[]
  sampleRow: Record<string, unknown> | null
  sampleDataJson: string
  totalContacts: number
  totalRowsRead: number
  extraColumns: string[]
  warnings: string[]
}

/** Sube un archivo modelo (Excel/CSV en formato fijo) y devuelve las columnas
 *  detectadas + la primera fila como sample. El frontend usa esto para llenar
 *  los dropdowns del mapeo y persistir el sample en el maestro. */
export function useParseEmailSample() {
  return useMutation({
    mutationFn: async (file: File): Promise<ParseEmailSampleResult> => {
      const fd = new FormData()
      fd.append('file', file)
      const { data } = await api.post<ParseEmailSampleResult>(
        '/campaign-templates/email/parse-sample',
        fd,
        { headers: { 'Content-Type': 'multipart/form-data' } },
      )
      return data
    },
  })
}

/** Renderiza la plantilla con datos de ejemplo. No envía nada. */
export function usePreviewEmailTemplate() {
  return useMutation({
    mutationFn: async (payload: EmailRenderInput) => {
      const { data } = await api.post<EmailPreviewResult>('/campaign-templates/email/preview', payload)
      return data
    },
  })
}

/** Envía un correo de prueba con la plantilla y datos de ejemplo al destinatario indicado. */
export function useTestSendEmailTemplate() {
  return useMutation({
    mutationFn: async (payload: EmailRenderInput & { toEmail: string }) => {
      const { data } = await api.post<{ ok: boolean; sentTo: string }>('/campaign-templates/email/test-send', payload)
      return data
    },
  })
}

export function useAvailableActions() {
  return useQuery({
    queryKey: ['available-actions'],
    queryFn: async () => {
      const { data } = await api.get<AvailableAction[]>('/campaign-templates/available-actions')
      return data
    },
  })
}

export function useAvailablePrompts() {
  return useQuery({
    queryKey: ['available-prompts'],
    queryFn: async () => {
      const { data } = await api.get<AvailablePrompt[]>('/campaign-templates/available-prompts')
      return data
    },
  })
}

export interface AvailablePromptDetail {
  id: string
  name: string
  description: string | null
  systemPrompt: string
}

/**
 * Trae el texto completo (SystemPrompt) de un prompt template visible para el tenant.
 * Se usa para copiarlo al CampaignTemplate.SystemPrompt editable en el maestro.
 */
export async function fetchAvailablePromptDetail(id: string): Promise<AvailablePromptDetail> {
  const { data } = await api.get<AvailablePromptDetail>(`/campaign-templates/available-prompts/${id}`)
  return data
}
