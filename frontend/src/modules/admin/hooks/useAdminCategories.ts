import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

export interface AgentCategory {
  id: string
  name: string
  isActive: boolean
  createdAt: string
}

export function useAdminCategories() {
  return useQuery({
    queryKey: ['admin', 'agent-categories'],
    queryFn: async () => {
      const { data } = await adminClient.get<AgentCategory[]>('/admin/agent-categories')
      return data
    },
  })
}

export function useCreateCategory() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (name: string) => {
      const { data } = await adminClient.post('/admin/agent-categories', { name })
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'agent-categories'] }),
  })
}

export function useUpdateCategory() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, ...payload }: { id: string; name?: string; isActive?: boolean }) => {
      const { data } = await adminClient.put(`/admin/agent-categories/${id}`, payload)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'agent-categories'] }),
  })
}

export function useDeleteCategory() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const { data } = await adminClient.delete(`/admin/agent-categories/${id}`)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'agent-categories'] }),
  })
}
