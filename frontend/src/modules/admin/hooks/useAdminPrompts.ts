import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

export interface PromptTemplate {
  id: string
  name: string
  description: string | null
  categoryId: string | null
  categoryName: string | null
  systemPrompt: string | null
  resultPrompt: string | null
  analysisPrompts: string | null
  fieldMapping: string | null
  isActive: boolean
  createdAt: string
}

export interface PromptPayload {
  name: string
  description?: string | null
  categoryId?: string | null
  systemPrompt?: string | null
  resultPrompt?: string | null
  analysisPrompts?: string | null
  fieldMapping?: string | null
}

export function useAdminPrompts() {
  return useQuery<PromptTemplate[]>({
    queryKey: ['admin-prompts'],
    queryFn: () => adminClient.get('/admin/prompts').then((r: { data: PromptTemplate[] }) => r.data),
  })
}

export function useAdminPrompt(id: string | null) {
  return useQuery<PromptTemplate>({
    queryKey: ['admin-prompt', id],
    queryFn: () => adminClient.get(`/admin/prompts/${id}`).then((r: { data: PromptTemplate }) => r.data),
    enabled: !!id,
  })
}

export function useCreatePrompt() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: PromptPayload) => adminClient.post('/admin/prompts', data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-prompts'] }),
  })
}

export function useUpdatePrompt() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: PromptPayload }) =>
      adminClient.put(`/admin/prompts/${id}`, data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-prompts'] }),
  })
}

export function useTogglePrompt() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => adminClient.put(`/admin/prompts/${id}/toggle`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-prompts'] }),
  })
}

export function useDeletePrompt() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => adminClient.delete(`/admin/prompts/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-prompts'] }),
  })
}
