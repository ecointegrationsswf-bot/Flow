import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api as client } from '@/shared/api/client'

export interface InvalidNumber {
  id: string
  phoneNumber: string
  reason: string
  source: string                  // ultramsg-precheck | dispatch-error | manual | campaign-import
  firstDetectedAt: string
  lastCheckedAt: string
  occurrenceCount: number
  tenantId: string | null
  lastTenantId: string | null
  lastCampaignId: string | null
  notes: string | null
  isActive: boolean
  scope: 'tenant' | 'global'
}

export interface InvalidNumbersListResponse {
  total: number
  page: number
  pageSize: number
  items: InvalidNumber[]
}

export interface InvalidNumbersFilter {
  q?: string
  source?: string
  isActive?: boolean
  page?: number
  pageSize?: number
}

export function useInvalidNumbers(filter: InvalidNumbersFilter = {}) {
  return useQuery({
    queryKey: ['invalid-numbers', filter],
    queryFn: async () => {
      const params: Record<string, string | number | boolean> = {}
      if (filter.q) params.q = filter.q
      if (filter.source) params.source = filter.source
      if (filter.isActive !== undefined) params.isActive = filter.isActive
      if (filter.page !== undefined) params.page = filter.page
      if (filter.pageSize !== undefined) params.pageSize = filter.pageSize
      const { data } = await client.get<InvalidNumbersListResponse>('/api/invalid-numbers', { params })
      return data
    },
    placeholderData: (prev) => prev,
  })
}

export function useRestoreInvalidNumber() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const { data } = await client.post(`/api/invalid-numbers/${id}/restore`)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['invalid-numbers'] }),
  })
}

export function useAddInvalidNumber() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (payload: { phoneNumber: string; reason?: string; isGlobal?: boolean }) => {
      const { data } = await client.post('/api/invalid-numbers', payload)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['invalid-numbers'] }),
  })
}
