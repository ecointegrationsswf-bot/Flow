import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

export interface TenantActionAssignment {
  id: string
  name: string
  description: string | null
  isActive: boolean
  requiresWebhook: boolean
  sendsEmail: boolean
  sendsSms: boolean
}

export interface TenantAssignmentsResponse {
  assignedPromptIds: string[]
  actions: TenantActionAssignment[]
}

/**
 * Trae los prompts asignados a un tenant + las acciones que ya tiene
 * (las acciones ya están scopadas por TenantId en BD).
 */
export function useAdminTenantAssignments(tenantId: string | null) {
  return useQuery<TenantAssignmentsResponse>({
    queryKey: ['admin', 'tenants', tenantId, 'assignments'],
    queryFn: () =>
      adminClient
        .get(`/admin/tenants/${tenantId}/assignments`)
        .then((r: { data: TenantAssignmentsResponse }) => r.data),
    enabled: !!tenantId,
  })
}

/**
 * Reemplaza la lista de PromptTemplates asignados al tenant.
 * Lista vacía = el tenant ve todos los prompts (retrocompat).
 */
export function useSetTenantAssignedPrompts() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({
      tenantId,
      promptIds,
    }: {
      tenantId: string
      promptIds: string[]
    }) => {
      const { data } = await adminClient.put(
        `/admin/tenants/${tenantId}/assigned-prompts`,
        { promptIds },
      )
      return data as { assignedPromptIds: string[] }
    },
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({
        queryKey: ['admin', 'tenants', variables.tenantId, 'assignments'],
      })
    },
  })
}
