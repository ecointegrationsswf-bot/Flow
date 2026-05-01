import { useQuery, useMutation, useQueryClient, keepPreviousData } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import type { ConversationSummary, Conversation } from '@/shared/types'

export interface ConversationsFilter {
  fromIso?: string | null
  toIso?: string | null
  launchedByUserId?: string | null
}

export function useConversations(filter: ConversationsFilter = {}) {
  return useQuery({
    queryKey: ['conversations', filter.fromIso ?? null, filter.toIso ?? null, filter.launchedByUserId ?? null],
    queryFn: () => api.get<ConversationSummary[]>('/monitor/conversations', {
      params: {
        from: filter.fromIso || undefined,
        to:   filter.toIso || undefined,
        launchedByUserId: filter.launchedByUserId || undefined,
      },
    }).then((r) => r.data),
    refetchInterval: 1000,
  })
}

export interface CampaignLauncher { key: string; label: string }
export function useCampaignLaunchers() {
  return useQuery<CampaignLauncher[]>({
    queryKey: ['campaign-launchers'],
    queryFn: () => api.get<CampaignLauncher[]>('/monitor/campaign-launchers').then((r) => r.data),
    staleTime: 60_000,
  })
}

export function useConversationDetail(id: string | null) {
  return useQuery({
    queryKey: ['conversations', id],
    queryFn: () => api.get<Conversation>(`/monitor/conversations/${id}`).then((r) => r.data),
    enabled: !!id,
    staleTime: 0,
    refetchInterval: 100,
    refetchIntervalInBackground: true,
    placeholderData: keepPreviousData,
  })
}

export function useTakeConversation() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.post(`/monitor/conversations/${id}/take`),
    onSuccess: (response: any, id) => {
      const conv = response?.data?.conversation
      if (conv) qc.setQueryData(['conversations', id], conv)
      qc.invalidateQueries({ queryKey: ['conversations'] })
      qc.invalidateQueries({ queryKey: ['conversations', id] })
    },
  })
}

export function useSendReply() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, message }: { id: string; message: string }) =>
      api.post(`/monitor/conversations/${id}/reply`, { message }),

    onSuccess: (response: any, { id }) => {
      const conv = response?.data?.conversation
      if (conv) {
        // Actualizar cache directamente con la conversación fresca del servidor
        qc.setQueryData(['conversations', id], conv)
      }
      qc.invalidateQueries({ queryKey: ['conversations'] })
    },

    onError: (err: any) => {
      const detail = err?.response?.data?.error ?? err?.message ?? 'Error desconocido'
      alert(`Error al enviar: ${detail}`)
    },

    onSettled: (_, __, { id }) => {
      // Forzar refetch del detalle para sincronizar
      qc.invalidateQueries({ queryKey: ['conversations', id] })
    },
  })
}

export function useReactivateAgent() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.post(`/monitor/conversations/${id}/reactivate`),
    onSuccess: (response: any, id) => {
      const conv = response?.data?.conversation
      if (conv) qc.setQueryData(['conversations', id], conv)
      qc.invalidateQueries({ queryKey: ['conversations'] })
      qc.invalidateQueries({ queryKey: ['conversations', id] })
    },
  })
}

export function useSendFile() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, file }: { id: string; file: File }) => {
      const formData = new FormData()
      formData.append('file', file)
      // No establecer Content-Type manualmente — Axios lo setea con el boundary correcto
      return api.post(`/monitor/conversations/${id}/file`, formData)
    },
    onSuccess: (response: any, { id }) => {
      const conv = response?.data?.conversation
      if (conv) qc.setQueryData(['conversations', id], conv)
      qc.invalidateQueries({ queryKey: ['conversations', id] })
    },
    onError: (err: any) => {
      const detail = err?.response?.data?.error ?? err?.message ?? 'Error desconocido'
      alert(`Error al enviar archivo: ${detail}`)
    },
  })
}
