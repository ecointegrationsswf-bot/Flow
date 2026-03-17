import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import type { ConversationLabel } from '@/shared/types'

export function useLabels() {
  return useQuery({
    queryKey: ['labels'],
    queryFn: () => api.get<ConversationLabel[]>('/labels').then((r) => r.data),
  })
}

export function useCreateLabel() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: { name: string; color: string; keywords: string[] }) =>
      api.post('/labels', data).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['labels'] }),
  })
}

export function useUpdateLabel() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, ...data }: { id: string; name?: string; color?: string; keywords?: string[]; isActive?: boolean }) =>
      api.put(`/labels/${id}`, data).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['labels'] }),
  })
}

export function useDeleteLabel() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.delete(`/labels/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['labels'] }),
  })
}
