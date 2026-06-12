import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

// Motor de flujos — Fase 4. Hooks del CRUD de flujos visuales (workflows) por tenant.

export interface BoundTemplate {
  id: string
  name: string
}

export interface FlowListItem {
  id: string
  tenantId: string
  name: string
  description: string | null
  isActive: boolean
  createdAt: string
  updatedAt: string | null
  /** Maestros que apuntan a este flujo (el binding REAL del motor). Vacío = flujo inerte. */
  boundTemplates: BoundTemplate[]
}

export interface TemplateForBind {
  id: string
  name: string
  activeFlowId: string | null
  isPrimaryForAgent: boolean
}

export interface FlowDetail extends FlowListItem {
  flowJson: string
}

export function useAdminFlows(tenantId: string | undefined) {
  return useQuery<FlowListItem[]>({
    queryKey: ['admin-flows', tenantId],
    enabled: !!tenantId,
    queryFn: async () =>
      (await adminClient.get('/admin/flows', { params: { tenantId } })).data,
  })
}

export function useAdminFlow(id: string | undefined) {
  return useQuery<FlowDetail>({
    queryKey: ['admin-flow', id],
    enabled: !!id,
    queryFn: async () => (await adminClient.get(`/admin/flows/${id}`)).data,
  })
}

export function useCreateFlow() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (p: { tenantId: string; name: string; description?: string; flowJson?: string }) =>
      (await adminClient.post('/admin/flows', p)).data as FlowDetail,
    onSuccess: (_d, v) => qc.invalidateQueries({ queryKey: ['admin-flows', v.tenantId] }),
  })
}

export function useUpdateFlow() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (p: { id: string; name?: string; description?: string; flowJson?: string; isActive?: boolean }) =>
      (await adminClient.put(`/admin/flows/${p.id}`, p)).data as FlowDetail,
    onSuccess: (d) => {
      qc.invalidateQueries({ queryKey: ['admin-flows', d.tenantId] })
      qc.invalidateQueries({ queryKey: ['admin-flow', d.id] })
    },
  })
}

export function useDeleteFlow() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      await adminClient.delete(`/admin/flows/${id}`)
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-flows'] }),
  })
}

/** Maestros del tenant con su flujo vinculado actual — para el selector de vínculo. */
export function useAdminFlowTemplates(tenantId: string | undefined) {
  return useQuery<TemplateForBind[]>({
    queryKey: ['admin-flow-templates', tenantId],
    enabled: !!tenantId,
    queryFn: async () =>
      (await adminClient.get('/admin/flows/templates', { params: { tenantId } })).data,
  })
}

/** Vincula (flowId) o desvincula (flowId=null) un flujo a un maestro. */
export function useBindFlow() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (p: { campaignTemplateId: string; flowId: string | null }) =>
      (await adminClient.put('/admin/flows/bind', p)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-flows'] })
      qc.invalidateQueries({ queryKey: ['admin-flow-templates'] })
    },
  })
}
