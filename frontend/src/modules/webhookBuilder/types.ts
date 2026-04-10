// Types TypeScript del Webhook Contract System
// Refleja los POCOs de AgentFlow.Domain.Webhooks

export type ExecutionMode = 'Inline' | 'FireAndForget' | 'Scheduled'
export type ParamSource = 'SystemOnly' | 'ConversationOnly' | 'Mixed'
export type ConversationImpact = 'BlocksResponse' | 'Transparent' | 'UpdatesContext'

export type ContentType = 'application/json' | 'application/x-www-form-urlencoded' | 'multipart/form-data'
export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH'
export type SchemaStructure = 'flat' | 'nested'
export type SourceType = 'system' | 'conversation' | 'static'
export type InputDataType = 'string' | 'number' | 'boolean' | 'date' | 'array'
export type OutputDataType = 'string' | 'number' | 'boolean' | 'date' | 'url' | 'base64' | 'array' | 'object'
export type OutputAction = 'send_to_agent' | 'send_whatsapp_media' | 'inject_context' | 'log_only' | 'trigger_escalation'
export type AuthType = 'None' | 'ApiKey' | 'Bearer'

export interface InputField {
  fieldPath: string
  sourceType: SourceType
  sourceKey?: string
  staticValue?: string
  dataType: InputDataType
  required: boolean
  defaultValue?: string
}

export interface InputSchema {
  contentType: ContentType
  httpMethod: HttpMethod
  structure: SchemaStructure
  fields: InputField[]
}

export interface OutputField {
  fieldPath: string
  dataType: OutputDataType
  mimeType?: string
  outputAction: OutputAction
  label: string
  required: boolean
}

export interface OutputSchema {
  fields: OutputField[]
}

export interface WebhookEndpointConfig {
  webhookUrl: string
  webhookMethod: HttpMethod
  authType: AuthType
  authValue?: string
  apiKeyHeaderName?: string
  webhookHeaders?: string
  timeoutSeconds: number
}

/**
 * Bundle completo de configuración de una acción.
 * Se guarda dentro del JSON CampaignTemplates.ActionConfigs[actionId].
 * Coexiste con los campos legacy (webhookUrl, webhookMethod, etc.) que
 * el formulario antiguo ya maneja.
 */
export interface WebhookContractBundle extends WebhookEndpointConfig {
  contentType: ContentType
  structure: SchemaStructure
  inputSchema?: InputSchema
  outputSchema?: OutputSchema
}

// ── Test endpoint ──

export interface TestWebhookRequest {
  webhookUrl: string
  webhookMethod: HttpMethod
  contentType: ContentType
  authType: AuthType
  authValue?: string
  apiKeyHeaderName?: string
  webhookHeaders?: string
  timeoutSeconds: number
  samplePayload?: Record<string, unknown>
}

export interface DetectedFieldDto {
  fieldPath: string
  dataType: string
}

export interface TestWebhookResponse {
  success: boolean
  httpStatus: number
  responseBody?: unknown
  rawBody?: string
  detectedFields?: DetectedFieldDto[]
  errorMessage?: string
  durationMs: number
}

// ── Catálogo de sourceKeys del sistema (para el dropdown del editor) ──

export const SYSTEM_SOURCE_KEYS: { key: string; label: string; group: string }[] = [
  { key: 'contact.phone', label: 'Teléfono del contacto', group: 'Contacto' },
  { key: 'contact.name', label: 'Nombre del contacto', group: 'Contacto' },
  { key: 'contact.email', label: 'Email del contacto', group: 'Contacto' },
  { key: 'contact.policyNumber', label: 'Número de póliza', group: 'Contacto' },
  { key: 'contact.insuranceCompany', label: 'Aseguradora', group: 'Contacto' },
  { key: 'contact.pendingAmount', label: 'Monto pendiente', group: 'Contacto' },

  { key: 'conversation.id', label: 'ID de conversación', group: 'Conversación' },
  { key: 'conversation.createdAt', label: 'Fecha inicio conversación', group: 'Conversación' },

  { key: 'session.id', label: 'ID de sesión', group: 'Sesión' },
  { key: 'session.origin', label: 'Origen (Inbound/Campaign)', group: 'Sesión' },
  { key: 'session.agentSlug', label: 'Slug del agente activo', group: 'Sesión' },

  { key: 'campaign.id', label: 'ID de campaña', group: 'Campaña' },
  { key: 'campaign.name', label: 'Nombre de campaña', group: 'Campaña' },

  { key: 'tenant.id', label: 'ID del tenant', group: 'Tenant' },
  { key: 'tenant.name', label: 'Nombre del tenant', group: 'Tenant' },
  { key: 'tenant.slug', label: 'Slug del tenant', group: 'Tenant' },
]
