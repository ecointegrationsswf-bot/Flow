/**
 * Mapeo único de slug → nombre amigable de las ActionDefinitions globales.
 * Usado por ActionsPage (lista admin), ScheduledJobsPage (tabla) y
 * ScheduledJobFormModal (dropdown). Cualquier nueva acción global debe
 * registrarse aquí para que aparezca con el mismo nombre en toda la app.
 */
export const ACTION_FRIENDLY_NAMES: Record<string, string> = {
  SEND_EMAIL_RESUME: 'Enviar email con resumen',
  TRANSFER_CHAT: 'Escalar a humano',
  SEND_MESSAGE: 'Enviar mensaje',
  SEND_RESUME: 'Enviar resumen',
  PREMIUM: 'Premium',
  CLOSE_CONVERSATION: 'Cerrar conversación',
  ESCALATE_TO_HUMAN: 'Escalar a ejecutivo',
  SEND_PAYMENT_LINK: 'Enviar enlace de pago',
  SEND_DOCUMENT: 'Enviar documento',
  // Acciones internas del Campaign Automation Worker
  FOLLOW_UP_MESSAGE: 'Seguimiento automático',
  AUTO_CLOSE_CAMPAIGN: 'Cierre automático de campaña',
  LABEL_CONVERSATIONS: 'Etiquetar conversaciones',
  SEND_LABELING_SUMMARY: 'Enviar resumen etiquetado',
}

export function getActionFriendlyName(slug: string | null | undefined): string {
  if (!slug) return ''
  return ACTION_FRIENDLY_NAMES[slug] ?? slug
}
