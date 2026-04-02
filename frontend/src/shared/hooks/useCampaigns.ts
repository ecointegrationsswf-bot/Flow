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
