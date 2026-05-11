import { useMutation, useQuery } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import type { DashboardStats } from '@/shared/types'

const emptyStats: DashboardStats = {
  totalConversations: 0,
  activeAgents: 0,
  activeCampaigns: 0,
  escalatedCount: 0,
  gestionByResult: {},
  recentConversations: [],
}

export function useDashboardStats() {
  return useQuery({
    queryKey: ['dashboard'],
    queryFn: async () => {
      try {
        const r = await api.get<DashboardStats>('/dashboard/stats')
        return r.data
      } catch {
        return emptyStats
      }
    },
    placeholderData: emptyStats,
  })
}

// ── Distribución de etiquetas por mes/año ─────────────────────────────────────

export interface LabelingSummaryItem {
  labelId: string
  name: string
  color: string
  count: number
  percentage: number
}

export interface LabelingSummary {
  year: number
  month: number
  totalConversations: number
  taggedCount: number
  taggedPercentage: number
  unlabeledCount: number
  unlabeledPercentage: number
  campaignsInReport: number
  breakdown: LabelingSummaryItem[]
}

export function useLabelingSummary(year: number, month: number) {
  return useQuery<LabelingSummary>({
    queryKey: ['dashboard', 'labeling-summary', year, month],
    queryFn: () => api.get<LabelingSummary>('/dashboard/labeling-summary', {
      params: { year, month },
    }).then((r) => r.data),
  })
}

// ── Informe gerencial comparativo ──────────────────────────────────────────────

export interface ReportLabelBreakdown {
  labelId: string
  name: string
  color: string
  isEffective: boolean
  count: number
  percentage: number
}

export interface ReportPeriod {
  label: string
  start: string
  end: string
  total: number
  effectiveness: number
  unlabeledCount: number
  unlabeledPercentage: number
  breakdown: ReportLabelBreakdown[]
}

export interface ManagementReport {
  tenantName: string
  from: string
  to: string
  granularity: 'monthly' | 'biweekly'
  campaignTemplateId: string | null
  campaignTemplateName: string | null
  effectiveLabelIds: string[]
  totalAll: number
  effectivenessAll: number
  periods: ReportPeriod[]
}

export interface ManagementReportQuery {
  from: string         // YYYY-MM-DD
  to: string           // YYYY-MM-DD
  granularity: 'monthly' | 'biweekly'
  campaignTemplateId?: string | null
}

export function useManagementReport(params: ManagementReportQuery, enabled: boolean) {
  return useQuery<ManagementReport>({
    queryKey: ['dashboard', 'management-report', params.from, params.to, params.granularity, params.campaignTemplateId ?? null],
    enabled,
    queryFn: () => api.get<ManagementReport>('/dashboard/management-report', {
      params: {
        from: params.from,
        to: params.to,
        granularity: params.granularity,
        ...(params.campaignTemplateId ? { campaignTemplateId: params.campaignTemplateId } : {}),
      },
    }).then((r) => r.data),
  })
}

/**
 * Descarga el informe en Excel (.xlsx) con 4 hojas:
 * Resumen · Por período · Conversaciones (data cruda) · Etiquetas.
 */
export function useExportManagementReport() {
  return useMutation({
    mutationFn: async (params: ManagementReportQuery) => {
      const resp = await api.get('/dashboard/management-report/export', {
        params: {
          from: params.from,
          to: params.to,
          granularity: params.granularity,
          ...(params.campaignTemplateId ? { campaignTemplateId: params.campaignTemplateId } : {}),
        },
        responseType: 'blob',
      })
      const blob = resp.data as Blob
      const cd = resp.headers['content-disposition'] as string | undefined
      const match = cd?.match(/filename="?([^";]+)"?/)
      const filename = match?.[1] ?? `informe_gerencial_${params.from}_${params.to}.xlsx`
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
