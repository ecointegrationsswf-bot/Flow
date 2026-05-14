import { useQuery } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

// === Types ===

export interface OutboundSummary {
  sinceUtc: string
  totalSent: number
  byChannel: { channel: string | null; count: number }[]
  byStatus:  { status: string;       count: number }[]
}

export interface OutboundItem {
  id: string
  conversationId: string
  tenantId: string
  tenantName: string | null
  clientName: string | null
  recipient: string
  channel: string
  status: string
  subject: string | null
  preview: string
  agentName: string | null
  campaignId: string | null
  externalId: string | null
  sentAt: string
}

export interface OutboundItemsResponse {
  total: number
  take: number
  skip: number
  fromUtc: string
  toUtc: string
  items: OutboundItem[]
}

export interface OutboundItemDetail {
  id: string
  conversationId: string
  tenantId: string
  tenantName: string | null
  clientName: string | null
  clientPhone: string | null
  recipient: string | null
  channel: string
  status: string
  subject: string | null
  content: string
  agentName: string | null
  campaignId: string | null
  externalId: string | null
  sentAt: string
}

export interface OutboundFilterArgs {
  channel?: string
  status?: string
  tenantId?: string
  recipient?: string
  from?: string
  to?: string
  take?: number
  skip?: number
}

// === Hooks ===

/** KPIs agregados de mensajes salientes — total + agrupados por canal y status. */
export function useOutboundSummary(hours = 24) {
  return useQuery({
    queryKey: ['admin', 'outbox', 'summary', hours],
    queryFn: async () => {
      const { data } = await adminClient.get<OutboundSummary>('/admin/outbox/summary', {
        params: { hours },
      })
      return data
    },
    refetchInterval: 60_000, // refresca cada minuto — la vista es para monitoreo
  })
}

/** Listado paginado de mensajes salientes con filtros. */
export function useOutboundItems(args: OutboundFilterArgs) {
  return useQuery({
    queryKey: ['admin', 'outbox', 'items', args],
    queryFn: async () => {
      const params: Record<string, string | number> = {}
      if (args.channel)   params.channel   = args.channel
      if (args.status)    params.status    = args.status
      if (args.tenantId)  params.tenantId  = args.tenantId
      if (args.recipient) params.recipient = args.recipient
      if (args.from)      params.from      = args.from
      if (args.to)        params.to        = args.to
      if (args.take !== undefined) params.take = args.take
      if (args.skip !== undefined) params.skip = args.skip
      const { data } = await adminClient.get<OutboundItemsResponse>('/admin/outbox/items', { params })
      return data
    },
    refetchInterval: 30_000,
    placeholderData: (prev) => prev,
  })
}

/** Detalle completo de un mensaje saliente — contenido sin truncar. */
export function useOutboundItemDetail(id: string | null) {
  return useQuery({
    queryKey: ['admin', 'outbox', 'detail', id],
    queryFn: async () => {
      const { data } = await adminClient.get<OutboundItemDetail>(`/admin/outbox/${id}/detail`)
      return data
    },
    enabled: !!id,
  })
}
