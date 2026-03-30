import { z } from 'zod'

export const agentSchema = z.object({
  name: z.string().min(1, 'El nombre es requerido').max(100),
  type: z.enum(['Cobros', 'Reclamos', 'Renovaciones', 'General']),
  isActive: z.boolean().default(true),
  systemPrompt: z.string().default(''),
  tone: z.string().nullable().default(null),
  language: z.string().default('es'),
  avatarName: z.string().nullable().default(null),
  enabledChannels: z.array(z.enum(['WhatsApp', 'Email', 'Sms'])).min(1, 'Selecciona al menos un canal'),
  sendFrom: z.string().nullable().default(null),
  sendUntil: z.string().nullable().default(null),
  maxRetries: z.coerce.number().min(1).max(10).default(3),
  retryIntervalHours: z.coerce.number().min(1).max(168).default(24),
  inactivityCloseHours: z.coerce.number().min(1).max(720).default(72),
  closeConditionKeyword: z.string().nullable().default(null),
  llmModel: z.string().default('claude-sonnet-4-6'),
  temperature: z.coerce.number().min(0).max(1).default(0.3),
  maxTokens: z.coerce.number().min(256).max(4096).default(1024),
  whatsAppLineId: z.string().nullable().default(null),
})

export type AgentFormData = z.infer<typeof agentSchema>

export const agentDefaults: AgentFormData = {
  name: '',
  type: 'Cobros',
  isActive: true,
  systemPrompt: '',
  tone: 'amigable',
  language: 'es',
  avatarName: null,
  enabledChannels: ['WhatsApp'],
  sendFrom: '08:00',
  sendUntil: '17:00',
  maxRetries: 3,
  retryIntervalHours: 24,
  inactivityCloseHours: 72,
  closeConditionKeyword: null,
  llmModel: 'claude-sonnet-4-6',
  temperature: 0.3,
  maxTokens: 1024,
  whatsAppLineId: null,
}
