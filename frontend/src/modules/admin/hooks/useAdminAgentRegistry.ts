import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

export interface AgentRegistryEntry {
  id: string
  tenantId: string
  slug: string
  name: string
  capabilities: string
  agentDefinitionId: string
  agentName: string | null
  isWelcome: boolean
  isActive: boolean
  createdAt: string
}

export interface AgentRegistryPayload {
  tenantId: string
  slug: string
  name: string
  capabilities: string
  agentDefinitionId: string
  isWelcome: boolean
}

export function useAdminAgentRegistry(tenantId?: string) {
  return useQuery<AgentRegistryEntry[]>({
    queryKey: ['admin-agent-registry', tenantId],
    queryFn: () =>
      adminClient
        .get('/admin/agent-registry', { params: tenantId ? { tenantId } : {} })
        .then((r: { data: AgentRegistryEntry[] }) => r.data),
  })
}

export function useCreateAgentRegistryEntry() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: AgentRegistryPayload) => adminClient.post('/admin/agent-registry', data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-agent-registry'] }),
  })
}

export function useUpdateAgentRegistryEntry() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: AgentRegistryPayload }) =>
      adminClient.put(`/admin/agent-registry/${id}`, data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-agent-registry'] }),
  })
}

export function useToggleAgentRegistryEntry() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => adminClient.put(`/admin/agent-registry/${id}/toggle`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-agent-registry'] }),
  })
}

export function useDeleteAgentRegistryEntry() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => adminClient.delete(`/admin/agent-registry/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-agent-registry'] }),
  })
}
