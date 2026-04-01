namespace AgentFlow.Domain.Enums;

public enum CampaignStatus
{
    Pending,    // Creada, no lanzada
    Launching,  // Disparando n8n
    Running,    // n8n procesando contactos
    Paused,     // Pausada manualmente
    Completed,  // Todos los contactos procesados
    Failed      // Error al disparar n8n u otro fallo crítico
}
