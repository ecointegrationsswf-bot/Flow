namespace AgentFlow.Domain.Entities;

/// <summary>
/// Integración Ludo CRM — Fase 1 (consumida en Fase 4). Cola de reintento para las
/// llamadas de SALIDA hacia Ludo (Dirección B: registrar_oportunidad / mover_fase) que
/// fallaron. Garantiza DEGRADACIÓN SUAVE: si la API de Ludo está caída durante una
/// conversación, la conversación NO se bloquea y el evento de negocio no se pierde —
/// queda encolado aquí y un job (ScheduledWebhookWorker) lo reintenta.
///
/// No reemplaza al circuit breaker del ActionExecutorService: lo complementa. El breaker
/// evita martillar a Ludo dentro del turno; este outbox asegura la entrega eventual.
/// </summary>
public class LudoOutboxItem
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Conversación de origen (contexto/auditoría). Nullable por si el evento no la tiene.</summary>
    public Guid? ConversationId { get; set; }

    /// <summary>Teléfono normalizado E.164 — clave con la que Ludo resuelve el prospecto/oportunidad.</summary>
    public string PhoneE164 { get; set; } = string.Empty;

    /// <summary>Slug de la acción a reintentar: registrar_oportunidad | mover_fase.</summary>
    public string ActionSlug { get; set; } = string.Empty;

    /// <summary>Parámetros serializados de la acción (lo que se reintentará al webhook de Ludo).</summary>
    public string PayloadJson { get; set; } = "{}";

    /// <summary>Pending | Sent | Failed (agotó reintentos).</summary>
    public string Status { get; set; } = "Pending";

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    /// <summary>Momento a partir del cual el job puede reintentar (backoff). Null = inmediato.</summary>
    public DateTime? NextAttemptAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
