import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'

// ─── Types ────────────────────────────────────────────────────────────────────

export interface LogicalField {
  id: string
  key: string
  displayName: string
  description: string | null
  dataType: string
  isRequired: boolean
}

export interface DelinquencyConfig {
  id: string
  actionDefinitionId: string
  codigoPais: string
  itemsJsonPath: string | null
  autoCrearCampanas: boolean
  campaignTemplateId: string | null
  agentDefinitionId: string | null
  campaignNamePattern: string | null
  notificationEmail: string | null
  isActive: boolean
}

export interface DelinquencyConfigPayload {
  codigoPais: string
  itemsJsonPath: string | null
  autoCrearCampanas: boolean
  campaignTemplateId: string | null
  agentDefinitionId: string | null
  campaignNamePattern: string | null
  notificationEmail: string | null
  isActive: boolean
}

export type FieldRole =
  | 'None'
  | 'Phone'
  | 'ClientName'
  | 'KeyValue'
  | 'Amount'
  | 'PolicyNumber'

export interface FieldMapping {
  id: string
  columnKey: string
  displayName: string
  jsonPath: string
  role: FieldRole
  roleLabel: string | null
  dataType: string
  sortOrder: number
  defaultValue: string | null
  isEnabled: boolean
}

export interface FieldMappingPayload {
  columnKey: string
  displayName: string
  jsonPath: string
  role: FieldRole
  roleLabel: string | null
  dataType: string
  sortOrder: number
  defaultValue: string | null
  isEnabled: boolean
}

export interface DelinquencyExecution {
  id: string
  actionDefinitionId: string
  status: string
  startedAt: string
  completedAt: string | null
  totalItems: number
  processedItems: number
  discardedItems: number
  groupsCreated: number
  campaignsCreated: number
  errorMessage: string | null
}

export interface ContactGroup {
  id: string
  phoneNormalized: string
  clientName: string | null
  totalAmount: number
  itemCount: number
  status: string
  campaignId: string | null
  createdAt: string
  firstMessageSentAt: string | null
  firstClientReplyAt: string | null
}

export interface DelinquencyItemDetail {
  id: string
  rowIndex: number
  policyNumber: string | null
  keyValue: string | null
  amount: number | null
  clientName: string | null
  phoneRaw: string | null
  phoneNormalized: string | null
  status: string
  discardReason: string | null
  extractedDataJson: string | null
}

export interface PaginatedResponse<T> {
  total: number
  page: number
  pageSize: number
  items?: T[]
  groups?: T[]
}

// ─── Catálogo de campos lógicos ───────────────────────────────────────────────

export function useLogicalFields() {
  return useQuery<LogicalField[]>({
    queryKey: ['morosidad', 'fields'],
    queryFn: () => api.get('/morosidad/fields').then((r) => r.data),
    staleTime: 1000 * 60 * 10, // catálogo estático, cache 10 min
  })
}

// ─── Config por acción ────────────────────────────────────────────────────────

export function useDelinquencyConfig(actionId: string | null) {
  return useQuery<DelinquencyConfig | null>({
    queryKey: ['morosidad', 'config', actionId],
    queryFn: () =>
      api
        .get(`/morosidad/config/${actionId}`)
        .then((r) => r.data)
        .catch((e) => (e?.response?.status === 404 ? null : Promise.reject(e))),
    enabled: !!actionId,
  })
}

export function useUpsertDelinquencyConfig(actionId: string | null) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: DelinquencyConfigPayload) =>
      api.put(`/morosidad/config/${actionId}`, payload).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['morosidad', 'config', actionId] })
    },
  })
}

// ─── Field Mappings ───────────────────────────────────────────────────────────

export function useFieldMappings(actionId: string | null) {
  return useQuery<FieldMapping[]>({
    queryKey: ['morosidad', 'mappings', actionId],
    queryFn: () =>
      api.get(`/morosidad/mappings/${actionId}`).then((r) => r.data),
    enabled: !!actionId,
  })
}

export function useSetFieldMappings(actionId: string | null) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (mappings: FieldMappingPayload[]) =>
      api
        .put(`/morosidad/mappings/${actionId}`, { mappings })
        .then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['morosidad', 'mappings', actionId] })
    },
  })
}

// ─── Historial de ejecuciones ─────────────────────────────────────────────────

export function useDelinquencyExecutions(
  actionId: string | null,
  page = 1,
  from?: string,
  to?: string,
) {
  return useQuery<PaginatedResponse<DelinquencyExecution>>({
    queryKey: ['morosidad', 'executions', actionId, page, from, to],
    queryFn: () =>
      api
        .get('/morosidad/executions', {
          params: { actionId, page, pageSize: 20, from: from || undefined, to: to || undefined },
        })
        .then((r) => r.data),
    enabled: !!actionId,
  })
}

export function useContactGroups(executionId: string | null, page = 1) {
  return useQuery<PaginatedResponse<ContactGroup>>({
    queryKey: ['morosidad', 'groups', executionId, page],
    queryFn: () =>
      api
        .get(`/morosidad/executions/${executionId}/groups`, {
          params: { page, pageSize: 50 },
        })
        .then((r) => r.data),
    enabled: !!executionId,
  })
}

export function useGroupItems(executionId: string | null, groupId: string | null) {
  return useQuery<DelinquencyItemDetail[]>({
    queryKey: ['morosidad', 'group-items', executionId, groupId],
    queryFn: () =>
      api
        .get(`/morosidad/executions/${executionId}/groups/${groupId}/items`)
        .then((r) => r.data),
    enabled: !!executionId && !!groupId,
  })
}

// ─── Disparo manual (testing) ─────────────────────────────────────────────────

export function useProcessManual(actionId: string | null) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (jsonPayload: string) =>
      api
        .post(`/morosidad/process/${actionId}`, { jsonPayload })
        .then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['morosidad', 'executions', actionId] })
    },
  })
}
