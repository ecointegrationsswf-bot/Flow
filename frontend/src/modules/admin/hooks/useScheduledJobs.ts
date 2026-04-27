import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

export type TriggerType = 'Cron' | 'EventBased' | 'DelayFromEvent'
export type JobScope = 'AllTenants' | 'PerCampaign' | 'PerConversation'

export interface ScheduledJob {
  id: string
  actionDefinitionId: string
  actionName: string | null
  triggerType: TriggerType
  cronExpression: string | null
  triggerEvent: string | null
  delayMinutes: number | null
  scope: JobScope
  isActive: boolean
  nextRunAt: string | null
  lastRunAt: string | null
  lastRunStatus: string | null
  lastRunSummary: string | null
  consecutiveFailures: number
  createdAt: string
  updatedAt: string
}

export interface ScheduledJobUpsert {
  actionDefinitionId: string
  triggerType: TriggerType
  cronExpression?: string | null
  triggerEvent?: string | null
  delayMinutes?: number | null
  scope: JobScope
  isActive: boolean
}

export interface JobExecution {
  id: string
  jobId: string
  startedAt: string
  completedAt: string
  status: string
  totalRecords: number
  successCount: number
  failureCount: number
  errorDetail: string | null
  triggeredBy: string | null
  contextId: string | null
}

export interface CronPreview {
  valid: boolean
  nextOccurrencesUtc?: string[]
  error?: string
}

export const TRIGGER_EVENTS = [
  'CampaignStarted',
  'CampaignFinished',
  'CampaignContactSent',
  'ConversationClosed',
  'ConversationEscalated',
  'ConversationLabeled',
] as const

export function useScheduledJobs() {
  return useQuery({
    queryKey: ['scheduled-jobs'],
    queryFn: async () => {
      const { data } = await adminClient.get<ScheduledJob[]>('/scheduled-jobs')
      return data
    },
  })
}

export function useScheduledJobExecutions(jobId: string | null) {
  return useQuery({
    queryKey: ['scheduled-jobs', jobId, 'executions'],
    enabled: !!jobId,
    queryFn: async () => {
      const { data } = await adminClient.get<JobExecution[]>(`/scheduled-jobs/${jobId}/executions`)
      return data
    },
  })
}

export function useCreateScheduledJob() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (req: ScheduledJobUpsert) => {
      const { data } = await adminClient.post<{ id: string }>('/scheduled-jobs', req)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['scheduled-jobs'] }),
  })
}

export function useUpdateScheduledJob() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, ...req }: ScheduledJobUpsert & { id: string }) => {
      await adminClient.put(`/scheduled-jobs/${id}`, req)
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['scheduled-jobs'] }),
  })
}

export function useDeleteScheduledJob() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      await adminClient.delete(`/scheduled-jobs/${id}`)
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['scheduled-jobs'] }),
  })
}

export function useRunScheduledJobNow() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const { data } = await adminClient.post(`/scheduled-jobs/${id}/run-now`)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['scheduled-jobs'] }),
  })
}

export async function previewCron(expression: string): Promise<CronPreview> {
  const { data } = await adminClient.post<CronPreview>('/scheduled-jobs/preview-cron', { expression })
  return data
}

export interface AdminAction {
  id: string
  name: string
  description: string | null
  isActive: boolean
}

export function useAdminActions() {
  return useQuery({
    queryKey: ['admin-actions-for-jobs'],
    queryFn: async () => {
      const { data } = await adminClient.get<AdminAction[]>('/admin/actions')
      return data
    },
  })
}
