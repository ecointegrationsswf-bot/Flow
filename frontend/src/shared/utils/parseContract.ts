import type { WebhookContractBundle } from '@/modules/webhookBuilder/types'

/**
 * Deserializa el `DefaultWebhookContract` (string JSON) que viene del backend
 * en una estructura `Partial<WebhookContractBundle>` que entiende el
 * `WebhookBuilderModal` (campos como webhookUrl, authType, inputSchema, etc.).
 *
 * Es la fuente ÚNICA para todo el frontend — quien necesite inyectar el contract
 * vigente del tenant a un modal/preview debe usar este helper, no implementar
 * su propio JSON.parse local. Esto garantiza que el editor del super admin
 * (TenantActionsConfigTab), el editor del maestro de campaña del tenant
 * (CampaignTemplateFormPage) y la lista de acciones del tenant (TenantActionsPage)
 * lean siempre los mismos datos.
 *
 * El backend devuelve este campo desde `/api/tenant-actions` y
 * `/api/campaign-templates/available-actions`, leyendo el clon tenant-specific
 * de la acción cuando existe (no la fila global, que suele estar vacía).
 *
 * Tolerante a fallos: si el contract es null, undefined o JSON inválido,
 * devuelve `{}` para no romper el render del modal.
 */
export function parseContract(json: string | null | undefined): Partial<WebhookContractBundle> {
  if (!json) return {}
  try {
    return JSON.parse(json) as Partial<WebhookContractBundle>
  } catch {
    return {}
  }
}
