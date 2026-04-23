import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import type { CampaignTemplateDocument } from '@/shared/types/models'

export function useCampaignTemplateDocuments(templateId?: string) {
  return useQuery({
    queryKey: ['campaign-template-documents', templateId],
    queryFn: () =>
      api
        .get<CampaignTemplateDocument[]>(`/campaign-templates/${templateId}/documents`)
        .then((r) => r.data),
    enabled: !!templateId,
  })
}

export function useUploadCampaignTemplateDocument(templateId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return api
        .post<CampaignTemplateDocument>(
          `/campaign-templates/${templateId}/documents`,
          form,
          { headers: { 'Content-Type': 'multipart/form-data' } },
        )
        .then((r) => r.data)
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['campaign-template-documents', templateId] })
    },
  })
}

export function useDeleteCampaignTemplateDocument(templateId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (docId: string) =>
      api
        .delete(`/campaign-templates/${templateId}/documents/${docId}`)
        .then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['campaign-template-documents', templateId] })
    },
  })
}
