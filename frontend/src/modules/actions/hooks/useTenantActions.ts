import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'

export interface TenantAction {
  id: string
  name: string
  description: string | null
  requiresWebhook: boolean
  sendsEmail: boolean
  sendsSms: boolean
  defaultWebhookContract: string | null
  defaultTriggerConfig: string | null
  hasWebhookContract: boolean
}

export function useTenantActions() {
  return useQuery<TenantAction[]>({
    queryKey: ['tenant-actions'],
    queryFn: () => api.get<TenantAction[]>('/tenant-actions').then(r => r.data),
  })
}

export function useUpdateWebhookContract() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, contract }: { id: string; contract: string | null }) =>
      api.put(`/tenant-actions/${id}/webhook-contract`, { contract }).then((r: { data: unknown }) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['tenant-actions'] }),
  })
}
