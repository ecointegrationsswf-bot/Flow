import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'

export interface TenantInfo {
  id: string
  name: string
  slug: string
  country: string
  whatsAppProvider: string
  whatsAppPhoneNumber: string
  businessHoursStart: string
  businessHoursEnd: string
  timeZone: string
  isActive: boolean
  monthlyBillingAmount: number
  llmProvider: string
  llmApiKey: string | null
  llmModel: string
  sendGridApiKey: string | null
  senderEmail: string | null
  campaignMessageDelaySeconds: number
}

export function useTenant() {
  return useQuery<TenantInfo>({
    queryKey: ['tenant-info'],
    queryFn: async () => {
      const { data } = await api.get<TenantInfo>('/auth/tenant')
      return data
    },
  })
}

export function useUpdateTenantSendGrid() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: { sendGridApiKey: string | null; senderEmail: string | null }) =>
      api.put('/auth/tenant/sendgrid', data).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['tenant-info'] }),
  })
}

export function useUpdateTenantLlm() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: { llmProvider: string; llmApiKey: string | null; llmModel: string }) =>
      api.put('/auth/tenant/llm', data).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['tenant-info'] }),
  })
}

export function useUpdateTenantTimezone() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (timeZone: string) =>
      api.put('/auth/tenant/timezone', { timeZone }).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['tenant-info'] }),
  })
}

export function useUpdateCampaignDelay() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (delaySeconds: number) =>
      api.put('/auth/tenant/campaign-delay', { delaySeconds }).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['tenant-info'] }),
  })
}
