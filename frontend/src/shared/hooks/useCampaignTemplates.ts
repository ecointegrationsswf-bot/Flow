import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'

export interface AvailableAction {
  id: string
  name: string
  description: string | null
  requiresWebhook: boolean
  sendsEmail: boolean
  sendsSms: boolean
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
  followUpHours: number[]
  autoCloseHours: number
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
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export interface CampaignTemplatePayload {
  name: string
  agentDefinitionId: string
  followUpHours: number[]
  autoCloseHours: number
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

export function useCreateCampaignTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (payload: CampaignTemplatePayload) => {
      const { data } = await api.post('/campaign-templates', payload)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['campaign-templates'] }),
  })
}

export function useUpdateCampaignTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, ...payload }: CampaignTemplatePayload & { id: string }) => {
      const { data } = await api.put(`/campaign-templates/${id}`, payload)
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
