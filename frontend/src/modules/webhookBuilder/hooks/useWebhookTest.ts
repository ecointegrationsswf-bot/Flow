import { useMutation } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import type { TestWebhookRequest, TestWebhookResponse } from '../types'

/**
 * Hook para probar un webhook desde el Builder de UI.
 * POST /api/actions/test-webhook
 */
export function useWebhookTest() {
  return useMutation<TestWebhookResponse, Error, TestWebhookRequest>({
    mutationFn: async (req) => {
      const { data } = await api.post<TestWebhookResponse>('/actions/test-webhook', req)
      return data
    },
  })
}
