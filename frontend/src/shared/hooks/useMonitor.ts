import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import type { ConversationSummary, Conversation } from '@/shared/types'

export function useConversations() {
  return useQuery({
    queryKey: ['conversations'],
    queryFn: () => api.get<ConversationSummary[]>('/monitor/conversations').then((r) => r.data),
    refetchInterval: 15_000,
  })
}

export function useConversationDetail(id: string | null) {
  return useQuery({
    queryKey: ['conversations', id],
    queryFn: () => api.get<Conversation>(`/monitor/conversations/${id}`).then((r) => r.data),
    enabled: !!id,
  })
}

export function useTakeConversation() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.post(`/monitor/conversations/${id}/take`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['conversations'] }),
  })
}

export function useSendReply() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, message }: { id: string; message: string }) =>
      api.post(`/monitor/conversations/${id}/reply`, { message }),
    onSuccess: (_, { id }) => qc.invalidateQueries({ queryKey: ['conversations', id] }),
  })
}
