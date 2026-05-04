using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

/// <summary>
/// Contacto individual dentro de una campaña.
/// Contiene los datos del cliente y el estado de su gestión.
/// </summary>
public class CampaignContact
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public Campaign Campaign { get; set; } = null!;

    // Datos del cliente
    public string PhoneNumber { get; set; } = string.Empty;     // normalizado E.164
    public string? Email { get; set; }
    public string? ClientName { get; set; }
    public string? PolicyNumber { get; set; }
    public string? InsuranceCompany { get; set; }
    public decimal? PendingAmount { get; set; }

    // Metadatos extra del archivo (columnas adicionales — formato plano)
    public Dictionary<string, string> ExtraData { get; set; } = [];

    // JSON consolidado para campañas con formato fijo (NombreCliente/Celular/CodigoPais/KeyValue + columnas extra)
    // Almacena el array de registros agrupados por número de teléfono
    public string? ContactDataJson { get; set; }

    // Estado gestión (resultado de negocio — ¿pagó, rechazó, etc.?)
    public bool IsPhoneValid { get; set; } = true;
    public int RetryCount { get; set; }
    public GestionResult Result { get; set; } = GestionResult.Pending;
    public DateTime? LastContactAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// JSON array (List<int>) de índices de seguimientos ya enviados a este contacto.
    /// Garantiza idempotencia del FollowUpExecutor: si por algún reintento Hangfire
    /// dispara dos veces el mismo job, el índice ya estará en la lista y se omitirá.
    /// Default '[]' — se inicializa vacío.
    /// </summary>
    public string? FollowUpsSentJson { get; set; } = "[]";

    // Estado de despacho (técnico — ¿llegó el mensaje?)
    public DispatchStatus DispatchStatus { get; set; } = DispatchStatus.Pending;
    public DateTime? ClaimedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public int DispatchAttempts { get; set; }
    public string? GeneratedMessage { get; set; }       // Mensaje final enviado (auditoría)
    public string? ExternalMessageId { get; set; }      // ID de UltraMsg
    public string? DispatchError { get; set; }          // Detalle del último error

    /// <summary>
    /// Cuándo el CampaignWorker puede tomar este contacto. NULL = inmediato.
    /// Lo usa el Intake para diferir contactos que excedieron el warm-up del día
    /// (ScheduledFor = mañana a las BusinessHoursStart del tenant).
    /// </summary>
    public DateTime? ScheduledFor { get; set; }
}
