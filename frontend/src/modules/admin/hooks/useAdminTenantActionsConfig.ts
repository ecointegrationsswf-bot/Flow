import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

/**
 * Acciones asignadas al tenant con su estado de configuración webhook.
 * Espejo del endpoint /api/tenant-actions pero invocado desde el panel admin
 * para gestionar el contract sin necesidad de loguearse como tenant.
 */
export interface TenantActionConfig {
  id: string
  name: string
  description: string | null
  requiresWebhook: boolean
  sendsEmail: boolean
  sendsSms: boolean
  isProcess: boolean
  defaultWebhookContract: string | null
  defaultTriggerConfig: string | null
  hasWebhookContract: boolean
}

export function useAdminTenantActionsConfig(tenantId: string | undefined) {
  return useQuery<TenantActionConfig[]>({
    queryKey: ['admin-tenant-actions-config', tenantId],
    enabled: !!tenantId,
    queryFn: () =>
      adminClient
        .get(`/admin/tenants/${tenantId}/actions-config`)
        .then((r: { data: TenantActionConfig[] }) => r.data),
  })
}

export function useAdminUpdateTenantActionContract(tenantId: string | undefined) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ actionId, contract }: { actionId: string; contract: string | null }) =>
      adminClient
        .put(`/admin/tenants/${tenantId}/actions/${actionId}/webhook-contract`, { contract })
        .then((r: { data: unknown }) => r.data),
    onSuccess: () =>
      qc.invalidateQueries({ queryKey: ['admin-tenant-actions-config', tenantId] }),
  })
}
