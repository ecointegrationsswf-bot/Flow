import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

export interface AdminWhatsAppLine {
  id: string
  displayName: string
  phoneNumber: string
  instanceId: string
  provider: string
  isActive: boolean
  createdAt: string
  updatedAt?: string
}

export interface AdminLineStatus {
  status: string
  phone: string | null
  instanceId: string
  lineId: string
  displayName: string
}

// ── CRUD ─────────────────────────────────────────

export function useAdminWhatsAppLines() {
  return useQuery({
    queryKey: ['admin', 'whatsapp-lines'],
    queryFn: async () => {
      const { data } = await adminClient.get<AdminWhatsAppLine[]>('/admin/whatsapp-lines')
      return data
    },
  })
}

export function useAdminCreateWhatsAppLine() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (payload: { displayName: string; phoneNumber: string; instanceId: string; apiToken: string }) => {
      const { data } = await adminClient.post('/admin/whatsapp-lines', payload)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'whatsapp-lines'] }),
  })
}

export function useAdminUpdateWhatsAppLine() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, ...payload }: { id: string; displayName: string; phoneNumber: string; instanceId?: string; apiToken?: string; isActive: boolean }) => {
      const { data } = await adminClient.put(`/admin/whatsapp-lines/${id}`, payload)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'whatsapp-lines'] }),
  })
}

export function useAdminDeleteWhatsAppLine() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (lineId: string) => {
      const { data } = await adminClient.delete(`/admin/whatsapp-lines/${lineId}`)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'whatsapp-lines'] }),
  })
}

// ── Per-line status & operations ─────────────────

export function useAdminLineStatus(lineId: string, enabled = true) {
  return useQuery<AdminLineStatus>({
    queryKey: ['admin', 'whatsapp-lines', lineId, 'status'],
    queryFn: async () => {
      const { data } = await adminClient.get<AdminLineStatus>(`/admin/whatsapp-lines/${lineId}/status`)
      return data
    },
    enabled: !!lineId && enabled,
    refetchInterval: (query) => {
      if (query.state.error) return 30_000
      const status = query.state.data?.status
      if (status === 'qr' || status === 'loading') return 5_000
      if (status === 'authenticated') return 15_000
      return 10_000
    },
    retry: 1,
  })
}

export function useAdminLineQr(lineId: string, enabled = true) {
  return useQuery<string>({
    queryKey: ['admin', 'whatsapp-lines', lineId, 'qr'],
    queryFn: async () => {
      try {
        const response = await adminClient.get(`/admin/whatsapp-lines/${lineId}/qr`, { responseType: 'blob' })
        const contentType = response.headers['content-type'] || ''
        if (contentType.includes('application/json')) {
          const text = await (response.data as Blob).text()
          const json = JSON.parse(text)
          if (json.error) return ''
          return ''
        }
        return URL.createObjectURL(response.data as Blob)
      } catch (err: unknown) {
        const axiosErr = err as { response?: { status?: number } }
        if (axiosErr.response?.status === 400) return ''
        throw err
      }
    },
    enabled: !!lineId && enabled,
    refetchInterval: 15_000,
    staleTime: 10_000,
    retry: 1,
  })
}

export function useAdminRestartLine() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (lineId: string) => {
      const { data } = await adminClient.post(`/admin/whatsapp-lines/${lineId}/restart`)
      return data
    },
    onSuccess: (_data, lineId) => {
      qc.invalidateQueries({ queryKey: ['admin', 'whatsapp-lines', lineId, 'status'] })
    },
  })
}

export function useAdminLogoutLine() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (lineId: string) => {
      const { data } = await adminClient.post(`/admin/whatsapp-lines/${lineId}/logout`)
      return data
    },
    onSuccess: (_data, lineId) => {
      qc.invalidateQueries({ queryKey: ['admin', 'whatsapp-lines', lineId, 'status'] })
      qc.invalidateQueries({ queryKey: ['admin', 'whatsapp-lines', lineId, 'qr'] })
    },
  })
}
