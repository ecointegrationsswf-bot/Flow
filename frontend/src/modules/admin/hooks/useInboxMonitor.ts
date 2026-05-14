import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

export type InboxSummary = {
  sinceUtc: string
  byStatus: { status: string; count: number }[]
  stuckCount: number
  oldestUnresolved: {
    id: string
    status: string
    firstReceivedAt: string
    tenantId: string
    tenantName: string | null
    fromPhone: string
  } | null
  failedSamples: {
    id: string
    attemptCount: number
    lastErrorStep: string | null
    lastError: string | null
  }[]
}

export type InboxItem = {
  id: string
  tenantId: string
  tenantName: string | null
  fromPhone: string
  channel: string
  status: string
  firstReceivedAt: string
  lastReceivedAt: string
  completedAt: string | null
  attemptCount: number
  lastErrorStep: string | null
  lastError: string | null
  claimedBy: string | null
  ageSec: number
}

export type InboxItemsResponse = {
  total: number
  take: number
  skip: number
  fromUtc: string
  toUtc: string
  items: InboxItem[]
}

export type TenantLite = { id: string; name: string }

export type InboxFilters = {
  status?: string
  tenantId?: string
  phone?: string
  from?: string  // ISO UTC
  to?: string    // ISO UTC
  take?: number
  skip?: number
}

/** Resumen KPIs — refresh 10s. */
export function useInboxSummary(hours = 24) {
  return useQuery<InboxSummary>({
    queryKey: ['inbox-summary', hours],
    queryFn: async () => {
      const { data } = await adminClient.get<InboxSummary>('/admin/inbox/summary', { params: { hours } })
      return data
    },
    refetchInterval: 10_000,
  })
}

/** Listado con filtros — refresh 15s. */
export function useInboxItems(filters: InboxFilters) {
  return useQuery<InboxItemsResponse>({
    queryKey: ['inbox-items', filters],
    queryFn: async () => {
      const params: Record<string, string | number> = {}
      if (filters.status)   params.status = filters.status
      if (filters.tenantId) params.tenantId = filters.tenantId
      if (filters.phone)    params.phone = filters.phone
      if (filters.from)     params.from = filters.from
      if (filters.to)       params.to = filters.to
      params.take = filters.take ?? 50
      params.skip = filters.skip ?? 0
      const { data } = await adminClient.get<InboxItemsResponse>('/admin/inbox/items', { params })
      return data
    },
    refetchInterval: 15_000,
  })
}

/** Catálogo de tenants para el dropdown. Cache largo, raramente cambia. */
export function useTenantsLite() {
  return useQuery<TenantLite[]>({
    queryKey: ['inbox-tenants-lite'],
    queryFn: async () => {
      const { data } = await adminClient.get<TenantLite[]>('/admin/inbox/tenants-lite')
      return data
    },
    staleTime: 5 * 60_000,
  })
}

export type InboxItemDetail = {
  item: {
    id: string
    tenantId: string
    fromPhone: string
    channel: string
    whatsAppLineId: string | null
    clientName: string | null
    externalMessageId: string | null
    messagesJson: string
    firstReceivedAt: string
    lastReceivedAt: string
    bufferSeconds: number
    status: string
    claimedAt: string | null
    claimedBy: string | null
    startedAt: string | null
    completedAt: string | null
    attemptCount: number
    lastError: string | null
    lastErrorStep: string | null
    outboundMessageId: string | null
    escalatedAt: string | null
    escalatedToUserId: string | null
  }
  tenantName: string | null
  burst: Array<{
    Content: string
    ExternalId: string | null
    MediaUrl: string | null
    MediaType: string | null
    ReceivedAt: string
  }>
  conversation: {
    id: string
    clientPhone: string
    clientName: string | null
    channel: string
    status: string
    isHumanHandled: boolean
    handledByUserId: string | null
    gestionResult: string
    startedAt: string
    lastActivityAt: string
    closedAt: string | null
    activeAgentId: string | null
  } | null
  messages: Array<{
    id: string
    direction: 'Inbound' | 'Outbound'
    status: string
    content: string
    externalMessageId: string | null
    isFromAgent: boolean
    agentName: string | null
    detectedIntent: string | null
    sentAt: string
  }>
  gestionEvents: Array<{
    id: string
    origin: string
    result: string
    notes: string | null
    occurredAt: string
  }>
}

/** Detalle completo para la modal — sin auto-refresh. */
export function useInboxItemDetail(id: string | null) {
  return useQuery<InboxItemDetail>({
    queryKey: ['inbox-item-detail', id],
    queryFn: async () => {
      const { data } = await adminClient.get<InboxItemDetail>(`/admin/inbox/${id}/detail`)
      return data
    },
    enabled: !!id,
  })
}

export function useRetryInboxItem() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const { data } = await adminClient.post<{ ok: boolean; requeued: string }>(`/admin/inbox/${id}/retry`)
      return data
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['inbox-summary'] })
      qc.invalidateQueries({ queryKey: ['inbox-items'] })
    },
  })
}
