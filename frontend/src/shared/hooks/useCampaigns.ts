import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import type { Campaign } from '@/shared/types'

export interface ParseResult {
  detectedColumns: string[]
  previewRows: Record<string, string>[]
  totalRows: number
  tempFilePath: string
}

export interface CreateCampaignFromFileRequest {
  name: string
  agentId: string
  channel: string
  scheduledAt?: string
  tempFilePath: string
  columnMapping: Record<string, string>
  campaignTemplateId?: string
}

export function useCampaigns() {
  return useQuery({
    queryKey: ['campaigns'],
    queryFn: () => api.get<Campaign[]>('/campaigns').then((r) => r.data),
    // Refresca cada 8 segundos para mostrar progreso en tiempo real
    refetchInterval: 8000,
  })
}

export function useUploadCampaign() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (formData: FormData) =>
      api.post<{ campaignId: string }>('/campaigns/upload', formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
      }).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['campaigns'] }),
  })
}

export function useParseCampaignFile() {
  return useMutation({
    mutationFn: (formData: FormData) =>
      api.post<ParseResult>('/campaigns/parse', formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
      }).then((r) => r.data),
  })
}

export function useCreateCampaignFromFile() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: CreateCampaignFromFileRequest) =>
      api.post<{ campaignId: string }>('/campaigns/create', data).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['campaigns'] }),
  })
}

export interface FixedContactPreview {
  phone: string
  nombreCliente: string
  keyValue: string
  totalRegistros: number
  contactDataJson: string
}

export interface FixedFormatPreviewResult {
  contacts: FixedContactPreview[]
  totalRowsRead: number
  extraColumns: string[]
  warnings: string[]
}

export function usePreviewFixedFormat() {
  return useMutation({
    mutationFn: (formData: FormData) =>
      api.post<FixedFormatPreviewResult>('/campaigns/preview-fixed', formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
      }).then((r) => r.data),
  })
}

export interface FixedFormatUploadResult {
  campaignId: string
  contactCount: number
  totalRowsRead: number
  extraColumns: string[]
  warnings: string[]
}

export function useUploadFixedFormat() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (formData: FormData) =>
      api.post<FixedFormatUploadResult>('/campaigns/upload-fixed', formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
      }).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['campaigns'] }),
  })
}

export function useLaunchCampaign() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (campaignId: string) =>
      api.post<{ success: boolean; message: string }>(`/campaigns/${campaignId}/launch`).then((r) => r.data),
    // Refrescar siempre — tanto en éxito como en error (ej: 409 ya en ejecución)
    onSettled: () => qc.invalidateQueries({ queryKey: ['campaigns'] }),
  })
}

export function usePauseCampaign() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (campaignId: string) =>
      api.post<{ message: string }>(`/campaigns/${campaignId}/pause`).then((r) => r.data),
    onSettled: () => qc.invalidateQueries({ queryKey: ['campaigns'] }),
  })
}

export function useResumeCampaign() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (campaignId: string) =>
      api.post<{ message: string }>(`/campaigns/${campaignId}/resume`).then((r) => r.data),
    onSettled: () => qc.invalidateQueries({ queryKey: ['campaigns'] }),
  })
}

/**
 * Cancela una campaña de forma IRREVERSIBLE. Los contactos pendientes
 * (Pending/Queued/Deferred/Retry) pasan a Skipped. Los Claimed se respetan
 * (su envío en curso se completa). Sent/Error no se tocan.
 * NO cierra conversaciones abiertas con clientes que ya respondieron.
 */
export function useCancelCampaign() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (campaignId: string) =>
      api.post<{ message: string; skippedCount: number }>(
        `/campaigns/${campaignId}/cancel`,
      ).then((r) => r.data),
    onSettled: () => qc.invalidateQueries({ queryKey: ['campaigns'] }),
  })
}

// ── Listado de contactos por campaña ──────────────────────────────────────

export type ContactStatusFilter = 'All' | 'Sent' | 'Pending' | 'Failed' | 'Discarded'

export interface CampaignContactRow {
  id: string
  phoneNumber: string
  clientName: string | null
  policyNumber: string | null
  insuranceCompany: string | null
  pendingAmount: number | null
  generatedMessage: string | null
  dispatchStatus:
    | 'Pending' | 'Queued' | 'Claimed' | 'Sent' | 'Error'
    | 'Retry' | 'Skipped' | 'Deferred' | 'Duplicate'
  sentAt: string | null
  externalMessageId: string | null
  dispatchError: string | null
  isPhoneValid: boolean
}

export interface ContactsCounts {
  all: number
  sent: number
  pending: number
  failed: number
  discarded: number
}

export interface CampaignContactsResponse {
  total: number
  page: number
  pageSize: number
  items: CampaignContactRow[]
  counts: ContactsCounts
}

export interface CampaignContactsQueryParams {
  campaignId: string
  status: ContactStatusFilter
  q: string
  page: number
  pageSize: number
}

export function useCampaignContacts(params: CampaignContactsQueryParams, enabled: boolean = true) {
  return useQuery({
    queryKey: ['campaign-contacts', params.campaignId, params.status, params.q, params.page, params.pageSize],
    enabled,
    queryFn: () => api.get<CampaignContactsResponse>(
      `/campaigns/${params.campaignId}/contacts`,
      {
        params: {
          status: params.status,
          q: params.q || undefined,
          page: params.page,
          pageSize: params.pageSize,
        },
      },
    ).then((r) => r.data),
    // Refresca cada 8s — útil mientras una campaña está corriendo
    refetchInterval: 8000,
  })
}

export interface CampaignContactMessage {
  id: string
  content: string
  isFromAgent: boolean
  direction: 'Inbound' | 'Outbound'
  sentAt: string
  externalMessageId: string | null
  agentName: string | null
  detectedIntent: string | null
  channel: 'WhatsApp' | 'Email' | 'Sms' | null
  subject: string | null
  recipient: string | null
  status: 'Sent' | 'Delivered' | 'Read' | 'Failed'
}

export interface CampaignContactMessagesResponse {
  conversationId: string | null
  items: CampaignContactMessage[]
}

/**
 * Trae todos los mensajes (WhatsApp + Email + SMS) asociados a un contacto de
 * campaña. Si la campaña aún no tiene conversación abierta para ese teléfono,
 * devuelve items vacío. El modal del "ojo" lo consume para mostrar el correo
 * enviado además del mensaje inicial.
 */
export function useCampaignContactMessages(
  campaignId: string, contactId: string | null
) {
  return useQuery({
    queryKey: ['campaign-contact-messages', campaignId, contactId],
    enabled: !!contactId,
    queryFn: () => api
      .get<CampaignContactMessagesResponse>(
        `/campaigns/${campaignId}/contacts/${contactId}/messages`
      )
      .then(r => r.data),
    staleTime: 10_000,
  })
}

export function useExportCampaignContacts() {
  return useMutation({
    mutationFn: async ({ campaignId, status, q }: { campaignId: string; status: ContactStatusFilter; q: string }) => {
      const resp = await api.get(`/campaigns/${campaignId}/contacts/export`, {
        params: { status, q: q || undefined },
        responseType: 'blob',
      })
      const blob = resp.data as Blob
      const cd = resp.headers['content-disposition'] as string | undefined
      const match = cd?.match(/filename="?([^";]+)"?/)
      const filename = match?.[1] ?? `contactos_${campaignId.slice(0, 8)}.xlsx`
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = filename
      document.body.appendChild(a)
      a.click()
      document.body.removeChild(a)
      URL.revokeObjectURL(url)
    },
  })
}

export function useCampaignById(campaignId: string | undefined) {
  return useQuery({
    queryKey: ['campaign', campaignId],
    enabled: !!campaignId,
    queryFn: () => api.get(`/campaigns/${campaignId}`).then((r) => r.data),
  })
}
