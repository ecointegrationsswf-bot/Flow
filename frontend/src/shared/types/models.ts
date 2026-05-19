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
  /** Phase 3 — si está poblado y futuro, la campaña está en cool-down entre
   *  batches. El dispatcher la salta hasta esta fecha (UTC). El frontend lo
   *  usa para mostrar "Próximo batch en X min" en la fila de la campaña. */
  nextBatchAfterUtc?: string | null
}

/** Estado real de entrega de un mensaje saliente, reportado por UltraMsg
 *  vía webhook message_ack. NULL = sin tracking (canal no soportado o
 *  toggle del webhook deshabilitado en la instancia). */
export type DeliveryStatus =
  | 'queue'      // 0 — en cola UltraMsg, WhatsApp aún no procesó
  | 'sent'       // 1 — servidor WhatsApp recibió
  | 'delivered'  // 2 — ✓✓ gris al teléfono destino
  | 'read'       // 3 — ✓✓ azul, cliente abrió el chat
  | 'invalid'    // -1 — número no existe / cuenta restringida
  | 'failed'     // falló definitivamente
  | 'expired'    // expiró antes de despachar
  | 'unsent'     // cuenta WhatsApp desconectada

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
  /** Asunto del último correo SALIENTE de la conversación. Solo presente cuando hay
   *  al menos un email enviado al cliente. Se usa como preview en las cards Email. */
  lastEmailSubject?: string
  /** Cantidad de correos salientes (Channel=Email) emitidos en el rango de fechas
   *  del filtro. Alimenta el badge del tab Email — refleja envíos por día, no
   *  conversaciones únicas: si reenvías mañana a los mismos contactos, suma. */
  outboundEmailCount?: number
  // === Delivery status (Phase 2 — webhook message_ack) ===
  /** Status real del ÚLTIMO mensaje saliente. NULL = sin tracking. */
  lastOutboundDeliveryStatus?: DeliveryStatus | null
  /** true si el último saliente fue leído por el cliente. */
  lastOutboundRead?: boolean
  /** true si hay al menos un saliente entregado pero no leído. */
  hasUnreadOutbound?: boolean
  /** true si el cliente envió un mensaje DESPUÉS del último saliente. */
  clientResponded?: boolean
  /** true si hay al menos un saliente NO entregado (queue/invalid/etc). */
  hasUndelivered?: boolean
  /** true si el último saliente quedó en DeliveryStatus='sent'
   *  (servidor WhatsApp confirmó pero sin ACK de delivered/read). */
  lastOutboundSent?: boolean
  /** true si el último saliente no tiene info de delivery (NULL). */
  lastOutboundNoTracking?: boolean
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
  // Phase 1 outbound tracking — canal específico de este mensaje (puede
  // diferir del canal de la conversación). null/undefined = hereda Conversation.channel.
  channel?: ChannelType | null
  subject?: string | null
  recipient?: string | null
  // Phase 2 — delivery status real (UltraMsg webhook message_ack)
  /** Estado real reportado por WhatsApp. Solo poblado en outbounds via webhook. */
  deliveryStatus?: DeliveryStatus | null
  /** Último ACK numérico: -1 invalid | 0 queue | 1 sent | 2 delivered | 3 read */
  lastAck?: number | null
  deliveredAt?: string | null
  readAt?: string | null
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

export interface CampaignTemplateDocument {
  id: string
  fileName: string
  blobUrl: string
  contentType: string
  fileSizeBytes: number
  uploadedAt: string
  description: string | null
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
