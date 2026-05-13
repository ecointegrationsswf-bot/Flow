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
  isGlobal: boolean
  originTenantId: string | null
  originTenantName: string | null
}

export interface TenantAssignmentsResponse {
  assignedPromptIds: string[]
  assignedActionIds: string[]
  actions: TenantActionAssignment[]
}

export interface AssignmentConflict {
  templateId: string
  templateName: string
  usedIds: string[]
  isActive: boolean
}

export interface AssignmentConflictError {
  error: string
  conflicts: AssignmentConflict[]
}

/**
 * Trae los prompts/acciones asignados a un tenant + el catálogo de acciones del tenant.
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

/**
 * Reemplaza la lista de ActionDefinitions asignadas al tenant.
 * Lista vacía = el tenant ve todas sus acciones activas (retrocompat).
 */
export function useSetTenantAssignedActions() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({
      tenantId,
      actionIds,
    }: {
      tenantId: string
      actionIds: string[]
    }) => {
      const { data } = await adminClient.put(
        `/admin/tenants/${tenantId}/assigned-actions`,
        { actionIds },
      )
      return data as { assignedActionIds: string[] }
    },
    onSuccess: (_data, variables) => {
      // Invalida assignments Y el config de webhooks (tab Webhooks usa query separada)
      qc.invalidateQueries({
        queryKey: ['admin', 'tenants', variables.tenantId, 'assignments'],
      })
      qc.invalidateQueries({
        queryKey: ['admin-tenant-actions-config', variables.tenantId],
      })
    },
  })
}
