import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

/**
 * Prompts del proceso de etiquetado configurables por tenant.
 * - analysisPrompt → reemplaza el system prompt hardcoded del ConversationLabelingJob.
 * - resultSchemaPrompt → describe el JSON adicional que el LLM debe extraer
 *   y que se guarda en Conversation.LabelingResultJson para mapear webhooks.
 */
export interface LabelingPrompts {
  analysisPrompt: string | null
  resultSchemaPrompt: string | null
}

export function useAdminTenantLabelingPrompts(tenantId: string | undefined) {
  return useQuery<LabelingPrompts>({
    queryKey: ['admin-tenant-labeling-prompts', tenantId],
    enabled: !!tenantId,
    queryFn: () =>
      adminClient
        .get(`/admin/tenants/${tenantId}/labeling-prompts`)
        .then((r: { data: LabelingPrompts }) => r.data),
  })
}

export function useUpdateAdminTenantLabelingPrompts(tenantId: string | undefined) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: LabelingPrompts) =>
      adminClient
        .put(`/admin/tenants/${tenantId}/labeling-prompts`, body)
        .then((r: { data: LabelingPrompts }) => r.data),
    onSuccess: () =>
      qc.invalidateQueries({ queryKey: ['admin-tenant-labeling-prompts', tenantId] }),
  })
}
