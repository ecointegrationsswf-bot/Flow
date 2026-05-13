import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

export interface ActionDefinition {
  id: string
  /** null = acción global (catálogo). Guid = acción legacy scopada a ese tenant. */
  tenantId: string | null
  name: string
  description: string | null
  requiresWebhook: boolean
  sendsEmail: boolean
  sendsSms: boolean
  /** Acción interna del backend (Worker). No envía webhook/email/SMS — la lógica vive en un IScheduledJobExecutor. */
  isProcess: boolean
  /** Marca la acción como descarga de morosidad — aparece en /admin/morosidad y /morosidad. */
  isDelinquencyDownload: boolean
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
  /** Omitir o null para crear acción global. Guid para legacy scoped. */
  tenantId?: string | null
  name: string
  description?: string | null
  requiresWebhook: boolean
  sendsEmail: boolean
  sendsSms: boolean
  isProcess: boolean
  isDelinquencyDownload: boolean
  webhookUrl?: string | null
  webhookMethod?: string | null
  defaultTriggerConfig?: string | null
  defaultWebhookContract?: string | null
}

export function useAdminActions(params?: { tenantId?: string; scope?: 'global' }) {
  return useQuery<ActionDefinition[]>({
    queryKey: ['admin-actions', params?.tenantId ?? params?.scope ?? 'all'],
    queryFn: () =>
      adminClient
        .get('/admin/actions', { params: params ?? {} })
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
