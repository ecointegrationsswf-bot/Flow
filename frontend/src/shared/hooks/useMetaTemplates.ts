import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import type { MetaMessageTemplate, MetaTemplateCategory } from '@/shared/types/models'

const key = (lineId: string) => ['meta-templates', lineId]

export function useMetaTemplates(lineId: string | null | undefined) {
  return useQuery({
    queryKey: key(lineId ?? ''),
    enabled: !!lineId,
    queryFn: async () => {
      const { data } = await api.get<MetaMessageTemplate[]>('/meta-templates', { params: { lineId } })
      return data
    },
  })
}

export interface MetaTemplatePayload {
  name: string
  language: string
  category: MetaTemplateCategory
  headerText?: string | null
  bodyText: string
  footerText?: string | null
  headerSamples: string[]
  bodySamples: string[]
  submitToMeta: boolean
}

export function useCreateMetaTemplate(lineId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (payload: MetaTemplatePayload) => {
      const { data } = await api.post<MetaMessageTemplate>('/meta-templates', { lineId, ...payload })
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: key(lineId) }),
  })
}

export function useUpdateMetaTemplate(lineId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, ...payload }: MetaTemplatePayload & { id: string }) => {
      const { data } = await api.put<MetaMessageTemplate>(`/meta-templates/${id}`, payload)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: key(lineId) }),
  })
}

export function useToggleMetaTemplate(lineId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, enable }: { id: string; enable: boolean }) => {
      const { data } = await api.post<MetaMessageTemplate>(`/meta-templates/${id}/${enable ? 'enable' : 'disable'}`)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: key(lineId) }),
  })
}

// Envía un borrador existente a revisión de Meta (botón directo en la lista).
export function useSubmitMetaTemplate(lineId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const { data } = await api.post<MetaMessageTemplate>(`/meta-templates/${id}/submit`)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: key(lineId) }),
  })
}

export function useSyncMetaTemplate(lineId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const { data } = await api.post<MetaMessageTemplate>(`/meta-templates/${id}/sync`)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: key(lineId) }),
  })
}

// Genera borradores de plantilla leyendo el prompt del maestro (una por burbuja '~').
// Si el maestro no tiene proceso de descarga ni estructura, devuelve { needsStructure }
// y el front pide un Excel (columns + sampleDataJson) y reintenta.
export interface GenerateFromPromptResult {
  needsStructure?: boolean
  message?: string
  count?: number
  templates?: MetaMessageTemplate[]
}
export function useGenerateFromPrompt(lineId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (payload: {
      campaignTemplateId: string; baseName?: string
      columns?: string[]; sampleDataJson?: string
    }): Promise<GenerateFromPromptResult> => {
      const { data } = await api.post<GenerateFromPromptResult>(
        '/meta-templates/generate-from-prompt', { lineId, ...payload })
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: key(lineId) }),
  })
}

// Importa/actualiza TODAS las plantillas que existen en el WABA de Meta hacia nuestra BD.
export function useSyncAllMetaTemplates(lineId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async () => {
      const { data } = await api.post<{ imported: number; updated: number; templates: MetaMessageTemplate[] }>(
        '/meta-templates/sync-all', null, { params: { lineId } })
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: key(lineId) }),
  })
}

export function useDeleteMetaTemplate(lineId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      await api.delete(`/meta-templates/${id}`)
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: key(lineId) }),
  })
}
