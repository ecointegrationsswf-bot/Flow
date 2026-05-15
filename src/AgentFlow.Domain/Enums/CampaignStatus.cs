namespace AgentFlow.Domain.Enums;

public enum CampaignStatus
{
    Pending,    // Creada, no lanzada
    Launching,  // Disparando n8n
    Running,    // n8n procesando contactos
    Paused,     // Pausada manualmente
    Completed,  // Todos los contactos iniciales procesados — sigue VIVA para follow-ups hasta AutoCloseHours
    Failed,     // Error al disparar n8n u otro fallo crítico
    Cancelled,  // Cancelada manualmente — IRREVERSIBLE. Los contactos pendientes pasan a Skipped.
    Closed      // Cerrada automáticamente por AutoCloseSweep al cumplir AutoCloseHours desde CompletedAt. No más follow-ups.
}
