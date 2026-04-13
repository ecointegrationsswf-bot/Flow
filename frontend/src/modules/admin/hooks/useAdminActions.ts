import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

export interface ActionDefinition {
  id: string
  tenantId: string
  name: string
  description: string | null
  requiresWebhook: boolean
  sendsEmail: boolean
  sendsSms: boolean
  webhookUrl: string | null
  webhookMethod: string | null
  isActive: boolean
  createdAt: string
  /** Action Trigger Protocol — TriggerConfig por defecto. JSON serializado. */
  defaultTriggerConfig: string | null
  /** Contrato webhook completo por defecto. JSON serializado del bundle. */
  defaultWebhookContract: string | null
}

export interface ActionPayload {
  tenantId: string
  name: string
  description?: string | null
  requiresWebhook: boolean
  sendsEmail: boolean
  sendsSms: boolean
  webhookUrl?: string | null
  webhookMethod?: string | null
  defaultTriggerConfig?: string | null
  defaultWebhookContract?: string | null
}

export function useAdminActions(tenantId?: string) {
  return useQuery<ActionDefinition[]>({
    queryKey: ['admin-actions', tenantId],
    queryFn: () =>
      adminClient
        .get('/admin/actions', { params: tenantId ? { tenantId } : {} })
        .then((r: { data: ActionDefinition[] }) => r.data),
  })
}

export function useCreateAction() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: ActionPayload) => adminClient.post('/admin/actions', data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-actions'] }),
  })
}

export function useUpdateAction() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: ActionPayload }) =>
      adminClient.put(`/admin/actions/${id}`, data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-actions'] }),
  })
}

export function useToggleAction() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => adminClient.put(`/admin/actions/${id}/toggle`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-actions'] }),
  })
}

export function useDeleteAction() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => adminClient.delete(`/admin/actions/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-actions'] }),
  })
}
