import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

/**
 * Config completa de un tenant que administra el super admin desde el tab
 * "Configuración" del modal Editar Cliente. Espejo de lo que el tenant veía
 * antes en /settings — ahora centralizado en admin.
 */
export interface AdminTenantConfig {
  id: string
  name: string
  slug: string
  country: string
  isActive: boolean
  monthlyBillingAmount: number
  whatsAppProvider: string
  whatsAppPhoneNumber: string
  businessHoursStart: string
  businessHoursEnd: string
  timeZone: string
  llmProvider: string
  llmApiKey: string | null
  llmModel: string
  sendGridApiKey: string | null
  senderEmail: string | null
  campaignMessageDelaySeconds: number
  brainEnabled: boolean
  webhookContractEnabled: boolean
  referenceDocumentsEnabled: boolean
  messageBufferSeconds: number
}

export function useAdminTenantConfig(tenantId: string | null | undefined) {
  return useQuery<AdminTenantConfig>({
    queryKey: ['admin-tenant-config', tenantId],
    queryFn: () =>
      adminClient.get(`/admin/tenants/${tenantId}/config`).then((r: { data: AdminTenantConfig }) => r.data),
    enabled: !!tenantId,
  })
}

function invalidateConfig(qc: ReturnType<typeof useQueryClient>, tenantId: string) {
  qc.invalidateQueries({ queryKey: ['admin-tenant-config', tenantId] })
  qc.invalidateQueries({ queryKey: ['admin-tenants'] })
}

export function useAdminUpdateTenantTimezone() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ tenantId, timeZone }: { tenantId: string; timeZone: string }) =>
      adminClient.put(`/admin/tenants/${tenantId}/timezone`, { timeZone }).then((r: { data: unknown }) => r.data),
    onSuccess: (_d, v) => invalidateConfig(qc, v.tenantId),
  })
}

export function useAdminUpdateTenantLlm() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({
      tenantId, llmProvider, llmApiKey, llmModel,
    }: { tenantId: string; llmProvider: string; llmApiKey: string | null; llmModel: string }) =>
      adminClient.put(`/admin/tenants/${tenantId}/llm`, { llmProvider, llmApiKey, llmModel }).then((r: { data: unknown }) => r.data),
    onSuccess: (_d, v) => invalidateConfig(qc, v.tenantId),
  })
}

export function useAdminUpdateTenantSendGrid() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({
      tenantId, sendGridApiKey, senderEmail,
    }: { tenantId: string; sendGridApiKey: string | null; senderEmail: string | null }) =>
      adminClient.put(`/admin/tenants/${tenantId}/sendgrid`, { sendGridApiKey, senderEmail }).then((r: { data: unknown }) => r.data),
    onSuccess: (_d, v) => invalidateConfig(qc, v.tenantId),
  })
}

export function useAdminUpdateTenantCampaignDelay() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ tenantId, delaySeconds }: { tenantId: string; delaySeconds: number }) =>
      adminClient.put(`/admin/tenants/${tenantId}/campaign-delay`, { delaySeconds }).then((r: { data: unknown }) => r.data),
    onSuccess: (_d, v) => invalidateConfig(qc, v.tenantId),
  })
}

export function useAdminUpdateTenantBrain() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ tenantId, enabled }: { tenantId: string; enabled: boolean }) =>
      adminClient.put(`/admin/tenants/${tenantId}/brain`, { enabled }).then((r: { data: unknown }) => r.data),
    onSuccess: (_d, v) => invalidateConfig(qc, v.tenantId),
  })
}

export function useAdminUpdateTenantWebhookContract() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ tenantId, enabled }: { tenantId: string; enabled: boolean }) =>
      adminClient.put(`/admin/tenants/${tenantId}/webhook-contract`, { enabled }).then((r: { data: unknown }) => r.data),
    onSuccess: (_d, v) => invalidateConfig(qc, v.tenantId),
  })
}

export function useAdminUpdateTenantReferenceDocs() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ tenantId, enabled }: { tenantId: string; enabled: boolean }) =>
      adminClient.put(`/admin/tenants/${tenantId}/reference-documents`, { enabled }).then((r: { data: unknown }) => r.data),
    onSuccess: (_d, v) => invalidateConfig(qc, v.tenantId),
  })
}

export function useAdminUpdateTenantMessageBuffer() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ tenantId, seconds }: { tenantId: string; seconds: number }) =>
      adminClient.put(`/admin/tenants/${tenantId}/message-buffer`, { seconds }).then((r: { data: unknown }) => r.data),
    onSuccess: (_d, v) => invalidateConfig(qc, v.tenantId),
  })
}
