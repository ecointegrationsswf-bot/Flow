import type {
  AgentType, ChannelType, ConversationStatus, CampaignTrigger,
  GestionResult, ProviderType, UserRole, MessageDirection, MessageStatus,
} from './enums'

export interface Tenant {
  id: string
  name: string
  slug: string
  isActive: boolean
  whatsAppProvider: ProviderType
  whatsAppPhoneNumber: string
  whatsAppInstanceId?: string
  businessHoursStart: string
  businessHoursEnd: string
  timeZone: string
  createdAt: string
}

export interface AgentDefinition {
  id: string
  tenantId: string
  name: string
  type: AgentType
  isActive: boolean
  systemPrompt: string
  tone: string | null
  language: string
  avatarName: string | null
  enabledChannels: ChannelType[]
  sendFrom: string | null
  sendUntil: string | null
  maxRetries: number
  retryIntervalHours: number
  inactivityCloseHours: number
  closeConditionKeyword: string | null
  llmModel: string
  temperature: number
  maxTokens: number
  whatsAppLineId: string | null
  whatsAppLineName?: string | null
  whatsAppLinePhone?: string | null
  createdAt: string
  updatedAt: string
}

export interface Campaign {
  id: string
  tenantId: string
  agentDefinitionId: string
  agentName?: string
  name: string
  trigger: CampaignTrigger
  channel: ChannelType
  isActive: boolean
  sourceFileName: string | null
  totalContacts: number
  processedContacts: number
  scheduledAt: string | null
  startedAt: string | null
  completedAt: string | null
  createdAt: string
  createdByUserId: string
  launchedByUserId?: string | null
  status?: string
  launchedAt?: string | null
}

export interface ConversationSummary {
  id: string
  clientPhone: string
  clientName?: string
  policyNumber?: string
  agentType: string
  agentName?: string
  status: ConversationStatus
  channel: string
  isHumanHandled: boolean
  lastActivityAt: string
  lastMessagePreview?: string
}

export interface Conversation {
  id: string
  tenantId: string
  clientPhone: string
  clientName?: string
  policyNumber?: string
  channel: ChannelType
  activeAgentId?: string
  campaignId?: string
  campaignName?: string
  status: ConversationStatus
  isHumanHandled: boolean
  handledByUserId?: string
  gestionResult: GestionResult
  closingNote?: string
  startedAt: string
  closedAt?: string
  lastActivityAt: string
  messages: Message[]
}

export interface Message {
  id: string
  conversationId: string
  direction: MessageDirection
  status: MessageStatus
  content: string
  externalMessageId?: string
  isFromAgent: boolean
  agentName?: string
  tokensUsed?: number
  confidenceScore?: number
  detectedIntent?: string
  sentAt: string
}

export interface GestionEvent {
  id: string
  conversationId: string
  result: GestionResult
  notes?: string
  origin: string
  occurredAt: string
}

export interface AppUser {
  id: string
  tenantId: string
  fullName: string
  email: string
  role: UserRole
  isActive: boolean
  canEditPhone: boolean
  allowedAgentIds: string[]
  permissions: string[]
  avatarUrl?: string | null
  createdAt: string
  lastLoginAt?: string
}

export interface AgentDocument {
  id: string
  fileName: string
  blobUrl: string
  contentType: string
  fileSizeBytes: number
  uploadedAt: string
}

export interface ConversationLabel {
  id: string
  tenantId: string
  name: string
  color: string
  keywords: string[]
  isActive: boolean
  createdAt: string
}

export interface WhatsAppLine {
  id: string
  tenantId: string
  displayName: string
  phoneNumber: string
  instanceId: string
  provider: ProviderType
  isActive: boolean
  createdAt: string
  updatedAt?: string
}

export interface DashboardStats {
  totalConversations: number
  activeAgents: number
  activeCampaigns: number
  escalatedCount: number
  gestionByResult: Record<string, number>
  recentConversations: ConversationSummary[]
}
