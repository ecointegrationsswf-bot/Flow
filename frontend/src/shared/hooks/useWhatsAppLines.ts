import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import type { WhatsAppLine } from '@/shared/types'

// ── CRUD ─────────────────────────────────────────

export function useWhatsAppLines() {
  return useQuery<WhatsAppLine[]>({
    queryKey: ['whatsapp-lines'],
    queryFn: async () => {
      const { data } = await api.get('/whatsapp-lines')
      return data
    },
  })
}

interface CreateLinePayload {
  displayName: string
  phoneNumber: string
  instanceId: string
  apiToken: string
}

export function useCreateWhatsAppLine() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (payload: CreateLinePayload) => {
      const { data } = await api.post('/whatsapp-lines', payload)
      return data as WhatsAppLine
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['whatsapp-lines'] }),
  })
}

interface UpdateLinePayload {
  id: string
  displayName: string
  phoneNumber: string
  instanceId?: string
  apiToken?: string
  isActive: boolean
}

export function useUpdateWhatsAppLine() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, ...payload }: UpdateLinePayload) => {
      const { data } = await api.put(`/whatsapp-lines/${id}`, payload)
      return data as WhatsAppLine
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['whatsapp-lines'] }),
  })
}

export function useDeleteWhatsAppLine() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      await api.delete(`/whatsapp-lines/${id}`)
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['whatsapp-lines'] }),
  })
}

// ── Per-line status & operations ─────────────────

export interface LineStatus {
  status: string
  phone: string | null
  instanceId: string
  lineId: string
  displayName: string
}

export function useLineStatus(lineId: string, enabled = true) {
  return useQuery<LineStatus>({
    queryKey: ['whatsapp-line-status', lineId],
    queryFn: async () => {
      const { data } = await api.get(`/whatsapp-lines/${lineId}/status`)
      return data
    },
    refetchInterval: (query) => {
      // Si hay error, no seguir haciendo polling agresivo
      if (query.state.error) return 30_000
      const status = query.state.data?.status
      if (status === 'qr' || status === 'loading') return 5_000
      if (status === 'authenticated') return 15_000
      return 10_000
    },
    enabled,
    retry: 1,
  })
}

export function useLineQr(lineId: string, enabled = true) {
  return useQuery<string>({
    queryKey: ['whatsapp-line-qr', lineId],
    queryFn: async () => {
      try {
        const response = await api.get(`/whatsapp-lines/${lineId}/qr`, { responseType: 'blob' })
        return URL.createObjectURL(response.data)
      } catch (err: unknown) {
        const axiosErr = err as { response?: { status?: number } }
        // 400 = QR no disponible (instancia no esta en estado QR)
        if (axiosErr.response?.status === 400) {
          return '' // No hay QR disponible
        }
        throw err
      }
    },
    refetchInterval: 30_000,
    staleTime: 25_000,
    enabled,
    retry: 1,
  })
}

export function useRestartLine() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (lineId: string) => {
      const { data } = await api.post(`/whatsapp-lines/${lineId}/restart`)
      return data
    },
    onSuccess: (_data, lineId) => {
      qc.invalidateQueries({ queryKey: ['whatsapp-line-status', lineId] })
    },
  })
}

export function useLogoutLine() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (lineId: string) => {
      const { data } = await api.post(`/whatsapp-lines/${lineId}/logout`)
      return data
    },
    onSuccess: (_data, lineId) => {
      qc.invalidateQueries({ queryKey: ['whatsapp-line-status', lineId] })
      qc.invalidateQueries({ queryKey: ['whatsapp-line-qr', lineId] })
    },
  })
}

export function useSendTestMessage() {
  return useMutation({
    mutationFn: async ({ lineId, to, message }: { lineId: string; to: string; message: string }) => {
      const { data } = await api.post(`/whatsapp-lines/${lineId}/test-message`, { to, message })
      return data as { message: string; externalId: string; to: string }
    },
  })
}
