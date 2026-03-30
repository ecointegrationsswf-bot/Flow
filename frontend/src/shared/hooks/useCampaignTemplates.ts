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
}

export interface ActionConfig {
  webhookUrl?: string
  webhookMethod?: string
  webhookHeaders?: string
  webhookPayload?: string
  emailAddress?: string
  smsPhoneNumber?: string
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
