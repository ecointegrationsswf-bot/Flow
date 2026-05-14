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
    queryFn: async () => {
      try {
        const { data } = await api.get<WhatsAppStatus>('/whatsapp/status')
        return data
      } catch (err) {
        // Si el tenant no tiene línea WhatsApp configurada el API devuelve 400.
        // Eso no es un "error" para mostrar al usuario — significa "este tenant
        // no usa WhatsApp". Devolvemos status='disabled' y se trata como un
        // estado válido (el polling se ralentiza, no spamea la consola).
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const status = (err as any)?.response?.status
        if (status && status >= 400 && status < 500) {
          return { status: 'disabled', phone: '', instanceId: '', provider: '' }
        }
        throw err
      }
    },
    retry: false,
    refetchInterval: (query) => {
      const status = query.state.data?.status
      if (status === 'qr' || status === 'loading') return 5_000
      // Si el tenant no tiene WhatsApp configurado, polling lento (5 min)
      // para detectar configuración posterior sin spammear cada 10s.
      if (status === 'disabled') return 5 * 60_000
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
