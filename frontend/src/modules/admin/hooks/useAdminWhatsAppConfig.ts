import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

// Vista cross-tenant de líneas WhatsApp para el panel admin. Aditivo: usa las rutas
// nuevas api/admin/whatsapp-config (no toca el CRUD legacy de líneas de prueba).
// Las operaciones por línea (status/qr/restart/logout) viven en useAdminWhatsAppLines.

export interface AdminWaConfigLine {
  id: string
  tenantId: string | null
  tenantName: string | null
  displayName: string
  phoneNumber: string
  instanceId: string
  provider: string
  metaWabaId: string | null
  metaBusinessId: string | null
  metaAccessTokenLast4: string | null
  metaAppSecretLast4: string | null
  isActive: boolean
  lastStatus: string | null
  lastStatusCheckedAt: string | null
  createdAt: string
  updatedAt?: string
}

export interface CreateWaConfigPayload {
  tenantId: string | null
  provider: string
  displayName: string
  phoneNumber: string
  instanceId: string
  apiToken?: string
  metaWabaId?: string
  metaAccessToken?: string
  metaAppSecret?: string
  metaBusinessId?: string
}

export interface UpdateWaConfigPayload extends Omit<CreateWaConfigPayload, 'tenantId'> {
  id: string
  isActive: boolean
}

const KEY = ['admin', 'whatsapp-config'] as const

export function useAdminWaConfigList() {
  return useQuery({
    queryKey: KEY,
    queryFn: async () => {
      const { data } = await adminClient.get<AdminWaConfigLine[]>('/admin/whatsapp-config')
      return data
    },
  })
}

export function useCreateWaConfig() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (payload: CreateWaConfigPayload) => {
      const { data } = await adminClient.post('/admin/whatsapp-config', payload)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: KEY }),
  })
}

export function useUpdateWaConfig() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, ...payload }: UpdateWaConfigPayload) => {
      const { data } = await adminClient.put(`/admin/whatsapp-config/${id}`, payload)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: KEY }),
  })
}

export function useDeleteWaConfig() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const { data } = await adminClient.delete(`/admin/whatsapp-config/${id}`)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: KEY }),
  })
}

export function useWaConfigTestMessage() {
  return useMutation({
    mutationFn: async ({ id, to, message }: { id: string; to: string; message: string }) => {
      const { data } = await adminClient.post(`/admin/whatsapp-config/${id}/test-message`, { to, message })
      return data
    },
  })
}
