import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import type { Campaign } from '@/shared/types'

export interface ParseResult {
  columns: string[]
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
