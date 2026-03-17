import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import type { AgentDocument } from '@/shared/types/models'

export function useAgentDocuments(agentId?: string) {
  return useQuery({
    queryKey: ['agent-documents', agentId],
    queryFn: () =>
      api.get<AgentDocument[]>(`/agents/${agentId}/documents`).then((r) => r.data),
    enabled: !!agentId,
  })
}

export function useUploadDocument(agentId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return api
        .post<AgentDocument>(`/agents/${agentId}/documents`, form, {
          headers: { 'Content-Type': 'multipart/form-data' },
        })
        .then((r) => r.data)
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['agent-documents', agentId] })
    },
  })
}

export function useDeleteDocument(agentId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (docId: string) =>
      api.delete(`/agents/${agentId}/documents/${docId}`).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['agent-documents', agentId] })
    },
  })
}
