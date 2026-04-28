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
    mutationFn: ({ file, description }: { file: File; description?: string | null }) => {
      const form = new FormData()
      form.append('file', file)
      if (description && description.trim().length > 0) {
        form.append('description', description.trim())
      }
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

export function useUpdateCampaignTemplateDocumentDescription(templateId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ docId, description }: { docId: string; description: string | null }) =>
      api
        .patch(`/campaign-templates/${templateId}/documents/${docId}`, { description })
        .then((r: { data: unknown }) => r.data),
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
