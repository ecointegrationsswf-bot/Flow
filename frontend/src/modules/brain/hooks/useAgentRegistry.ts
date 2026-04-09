import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'

export interface AgentRegistryEntry {
  id: string
  slug: string
  name: string
  capabilities: string
  campaignTemplateId: string
  campaignTemplateName: string | null
  agentName: string | null
  isWelcome: boolean
  isActive: boolean
  createdAt: string
}

export interface AgentRegistryPayload {
  slug: string
  name: string
  capabilities: string
  campaignTemplateId: string
  isWelcome: boolean
}

export interface AvailableTemplate {
  id: string
  name: string
  agentName: string | null
  labelCount: number
}

export function useAgentRegistry() {
  return useQuery<AgentRegistryEntry[]>({
    queryKey: ['agent-registry'],
    queryFn: () => api.get('/agent-registry').then(r => r.data),
  })
}

export function useAvailableTemplatesForRegistry() {
  return useQuery<AvailableTemplate[]>({
    queryKey: ['agent-registry-available-templates'],
    queryFn: () => api.get('/agent-registry/available-templates').then(r => r.data),
  })
}

export function useCreateRegistryEntry() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: AgentRegistryPayload) => api.post('/agent-registry', data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['agent-registry'] }),
  })
}

export function useUpdateRegistryEntry() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: AgentRegistryPayload }) =>
      api.put(`/agent-registry/${id}`, data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['agent-registry'] }),
  })
}

export function useToggleRegistryEntry() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.put(`/agent-registry/${id}/toggle`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['agent-registry'] }),
  })
}

export function useDeleteRegistryEntry() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.delete(`/agent-registry/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['agent-registry'] }),
  })
}
