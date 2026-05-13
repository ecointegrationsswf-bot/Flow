namespace AgentFlow.Domain.Enums;

public enum DispatchStatus
{
    Pending,    // Creado, intake aún no procesó (estado transitorio antes del CampaignIntakeService)
    Queued,     // Pasó intake (válido, no duplicado, dentro del warm-up); listo para que el CampaignWorker lo tome
    Claimed,    // El despachador lo tomó — en proceso ahora mismo
    Sent,       // Mensaje entregado al provider exitosamente
    Error,      // Falló definitivamente (agotó reintentos)
    Retry,      // Falló, programado para reintento
    Skipped,    // Teléfono inválido u omitido intencionalmente
    Deferred,   // Excedió el warm-up del día; programado para mañana (ScheduledFor)
    Duplicate   // Mismo teléfono ya tiene una campaña activa para este tenant
}
