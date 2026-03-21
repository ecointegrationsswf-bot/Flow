import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

export interface AgentTemplate {
  id: string
  name: string
  category: string
  isActive: boolean
  systemPrompt: string
  tone: string | null
  language: string
  avatarName: string | null
  sendFrom: string | null
  sendUntil: string | null
  maxRetries: number
  retryIntervalHours: number
  inactivityCloseHours: number
  closeConditionKeyword: string | null
  llmModel: string
  temperature: number
  maxTokens: number
  createdAt: string
  updatedAt: string
}

export interface AgentTemplatePayload {
  name: string
  category: string
  isActive: boolean
  systemPrompt: string
  tone: string | null
  language: string
  avatarName: string | null
  sendFrom: string | null
  sendUntil: string | null
  maxRetries: number
  retryIntervalHours: number
  inactivityCloseHours: number
  closeConditionKeyword: string | null
  llmModel: string
  temperature: number
  maxTokens: number
}

export function useAdminAgentTemplates() {
  return useQuery({
    queryKey: ['admin', 'agent-templates'],
    queryFn: async () => {
      const { data } = await adminClient.get<AgentTemplate[]>('/admin/agent-templates')
      return data
    },
  })
}

export function useAdminAgentTemplate(id: string | undefined) {
  return useQuery({
    queryKey: ['admin', 'agent-templates', id],
    queryFn: async () => {
      const { data } = await adminClient.get<AgentTemplate>(`/admin/agent-templates/${id}`)
      return data
    },
    enabled: !!id,
  })
}

export function useCreateAgentTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (payload: AgentTemplatePayload) => {
      const { data } = await adminClient.post('/admin/agent-templates', payload)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'agent-templates'] }),
  })
}

export function useUpdateAgentTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, ...payload }: AgentTemplatePayload & { id: string }) => {
      const { data } = await adminClient.put(`/admin/agent-templates/${id}`, payload)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'agent-templates'] }),
  })
}

export function useDeleteAgentTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const { data } = await adminClient.delete(`/admin/agent-templates/${id}`)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'agent-templates'] }),
  })
}

export function useMigrateAgentTemplate() {
  return useMutation({
    mutationFn: async ({ templateId, tenantId, update = false }: { templateId: string; tenantId: string; update?: boolean }) => {
      const { data } = await adminClient.post(`/admin/agent-templates/${templateId}/migrate`, { tenantId, update })
      return data
    },
  })
}
