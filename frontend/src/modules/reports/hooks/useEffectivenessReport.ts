import { useMutation, useQuery } from '@tanstack/react-query'
import { api } from '@/shared/api/client'

// ── Tipos del DTO devuelto por GET /api/reports/effectiveness ──
// Refleja exactamente AgentFlow.Application.Modules.Reports.EffectivenessReportDto.

export interface ReportFilters {
  fromUtc: string
  toUtc: string
  tenantName: string
  /** Maestros de campaña seleccionados (null = todos los del rango). */
  campaignTemplateNames: string[] | null
  generatedAtUtc: string
  /** URL pública del logo del tenant — se inyecta en el header del PDF (no en la preview JSON). */
  tenantLogoUrl: string | null
}

export interface ReportSummary {
  totalCampaigns: number
  totalContactsSent: number
  uniqueClients: number
  averageContactsPerClient: number
  clientsWhoResponded: number
  engagementRate: number
  clientsConfirmedPayment: number
  confirmedPaymentRate: number
  clientsWithPromise: number
  clientsNegotiating: number
  clientsWithDispute: number
  clientsRequestingCancel: number
  clientsSilent: number
  effectiveManagementRate: number
}

export interface ContactDistributionBucket {
  contactCount: number
  clients: number
  percentageOfUniqueClients: number
}

export interface ResultDistributionBucket {
  label: string
  clients: number
  percentage: number
  category: 'Conversión' | 'Compromiso' | 'Negociación' | 'Disputa' | 'Cancelación' | 'Sin Respuesta'
}

export interface CampaignBreakdown {
  campaignId: string
  campaignName: string
  launchedAtUtc: string
  totalContacts: number
  uniqueClients: number
  responded: number
  confirmedPayment: number
}

export interface EffectivenessReport {
  filters: ReportFilters
  summary: ReportSummary
  contactDistribution: { buckets: ContactDistributionBucket[] }
  resultDistribution: { buckets: ResultDistributionBucket[] }
  campaigns: CampaignBreakdown[]
}

/**
 * Maestro de campaña con metadata agregada para el filtro multi-select del frontend.
 * Solo aparecen maestros que tienen al menos una campaña creada dentro del rango.
 */
export interface CampaignTemplateForFilter {
  id: string
  name: string
  /** Cuántas campañas (corridas) tiene este maestro dentro del rango. */
  campaignCount: number
  /** Suma de contactos de todas las campañas del maestro en el rango. */
  totalContacts: number
  /** Fecha de creación de la última campaña del maestro (para ordenar). */
  lastCreatedAt: string
}

export interface EffectivenessFilters {
  from: string   // ISO date
  to: string     // ISO date
  campaignTemplateIds?: string[]
}

// ── Hooks ──────────────────────────────────────────────────────────

/**
 * Carga el listado de MAESTROS de campaña del tenant para poblar el filtro
 * multi-select. Solo devuelve maestros que tienen al menos una campaña creada
 * dentro del rango — un maestro sin corridas no aporta al reporte.
 */
export function useCampaignTemplatesForFilter(from?: string, to?: string) {
  return useQuery({
    queryKey: ['reports', 'templates-for-filter', from, to],
    queryFn: async () => {
      const params: Record<string, string> = {}
      if (from) params.from = from
      if (to)   params.to = to
      const { data } = await api.get<CampaignTemplateForFilter[]>('/reports/templates-for-filter', { params })
      return data
    },
    staleTime: 30_000,
  })
}

/**
 * Genera el reporte JSON. Lo usa la página de informes para mostrar la vista
 * previa antes de descargar el PDF. Mutation (no auto-query) porque se dispara
 * solo cuando el usuario hace click en "Generar vista previa".
 */
export function useGenerateEffectivenessReport() {
  return useMutation({
    mutationFn: async (filters: EffectivenessFilters): Promise<EffectivenessReport> => {
      const { data } = await api.get<EffectivenessReport>('/reports/effectiveness', {
        params: {
          from: filters.from,
          to: filters.to,
          campaignTemplateIds: filters.campaignTemplateIds,
        },
        paramsSerializer: { indexes: null },  // axios: campaignTemplateIds=A&campaignTemplateIds=B
      })
      return data
    },
  })
}

/**
 * Filtros del informe "Detalle de Conversaciones" (descarga directa a Excel).
 * - <c>campaignTemplateId</c> es OPCIONAL — null/undefined = todos los maestros.
 * - <c>includeInboundWithoutCampaign</c>: si está prendido, agrega al Excel
 *   las conversaciones inbound espontáneas que no tienen campaña asociada
 *   (clientes que nos escribieron sin que les hayamos mandado nada primero).
 */
export interface ConversationDetailsFilters {
  from: string
  to: string
  campaignTemplateId?: string | null
  includeInboundWithoutCampaign?: boolean
}

/**
 * Descarga el Excel "Detalle de Conversaciones" — mismo formato que el correo
 * automático de cada mañana pero con filtros del usuario.
 */
export function useDownloadConversationDetailsExcel() {
  return useMutation({
    mutationFn: async (filters: ConversationDetailsFilters) => {
      const resp = await api.get('/reports/conversation-details/export', {
        params: {
          from: filters.from,
          to: filters.to,
          ...(filters.campaignTemplateId ? { campaignTemplateId: filters.campaignTemplateId } : {}),
          includeInboundWithoutCampaign: filters.includeInboundWithoutCampaign ?? false,
        },
        responseType: 'blob',
      })
      const blob = resp.data as Blob
      const cd = resp.headers['content-disposition'] as string | undefined
      const match = cd?.match(/filename="?([^";]+)"?/)
      const filename = match?.[1] ?? `detalle-conversaciones_${filters.from}_${filters.to}.xlsx`
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = filename
      document.body.appendChild(a)
      a.click()
      document.body.removeChild(a)
      URL.revokeObjectURL(url)
    },
  })
}

/**
 * Descarga el PDF nativo del reporte. Hace GET binario y dispara el download
 * en el browser usando un blob temporal. El Content-Disposition del backend
 * trae el filename sugerido, pero forzamos uno legible por las dudas.
 */
export function useDownloadEffectivenessPdf() {
  return useMutation({
    mutationFn: async (filters: EffectivenessFilters) => {
      const resp = await api.get('/reports/effectiveness/pdf', {
        params: {
          from: filters.from,
          to: filters.to,
          campaignTemplateIds: filters.campaignTemplateIds,
        },
        paramsSerializer: { indexes: null },
        responseType: 'blob',
      })

      // Extraer filename del Content-Disposition (si llega).
      const cd = resp.headers['content-disposition'] as string | undefined
      let filename = `informe-efectividad_${filters.from.slice(0,10)}_${filters.to.slice(0,10)}.pdf`
      if (cd) {
        const m = /filename="?([^";]+)"?/i.exec(cd)
        if (m) filename = m[1]
      }

      const blob = new Blob([resp.data], { type: 'application/pdf' })
      const url = window.URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = filename
      document.body.appendChild(a)
      a.click()
      a.remove()
      window.URL.revokeObjectURL(url)
    },
  })
}
