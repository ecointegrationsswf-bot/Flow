import { useQuery } from '@tanstack/react-query'
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
