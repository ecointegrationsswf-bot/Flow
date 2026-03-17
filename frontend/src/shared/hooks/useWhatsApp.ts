import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'

interface WhatsAppStatus {
  status: string
  phone: string
  instanceId: string
  provider: string
}

export function useWhatsAppStatus(enabled = true) {
  return useQuery({
    queryKey: ['whatsapp-status'],
    queryFn: () => api.get<WhatsAppStatus>('/whatsapp/status').then((r) => r.data),
    refetchInterval: (query) => {
      // Polling más rápido cuando espera QR scan, más lento cuando está conectado
      const status = query.state.data?.status
      if (status === 'qr' || status === 'loading') return 5_000
      return 10_000
    },
    enabled,
  })
}

export function useWhatsAppQr() {
  return useQuery({
    queryKey: ['whatsapp-qr'],
    queryFn: async () => {
      const response = await api.get('/whatsapp/qr', { responseType: 'blob' })
      return URL.createObjectURL(response.data)
    },
    refetchInterval: 30_000,
    staleTime: 25_000,
  })
}

export function useRestartInstance() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => api.post('/whatsapp/restart').then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['whatsapp-status'] })
    },
  })
}

export function useLogoutInstance() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => api.post('/whatsapp/logout').then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['whatsapp-status'] })
      qc.invalidateQueries({ queryKey: ['whatsapp-qr'] })
    },
  })
}
