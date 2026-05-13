namespace AgentFlow.Domain.Entities;

/// <summary>
/// Detalle granular por sub-unidad procesada dentro de una ScheduledWebhookJobExecution.
/// Cada executor escribe N filas (una por tenant, conversación, usuario, etc.) cuando
/// el scope del job procesa varios items y queremos atribución por item en la UI.
///
/// Política de escritura: solo se persisten los items con Status != Success, para
/// no inflar la tabla con éxitos que ya quedan reflejados en SuccessCount agregado.
/// (Los executors pueden optar por escribir también Success si lo necesitan).
/// </summary>
public class ScheduledWebhookJobExecutionItem
{
    public Guid Id { get; set; }

    public Guid ExecutionId { get; set; }
    public ScheduledWebhookJobExecution? Execution { get; set; }

    /// <summary>Tenant al que corresponde este item, si aplica. NULL si el item no es per-tenant.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Tipo de contexto. Valores usados: Tenant | Conversation | Campaign | User | Template.</summary>
    public string ContextType { get; set; } = "Tenant";

    /// <summary>Identificador del sub-item (Guid en string o cualquier id externo).</summary>
    public string? ContextId { get; set; }

    /// <summary>Nombre/label legible para mostrar en la UI (ej: nombre del tenant o del cliente).</summary>
    public string? ContextLabel { get; set; }

    /// <summary>Success | Failed | Skipped.</summary>
    public string Status { get; set; } = "Failed";

    /// <summary>Mensaje de error detallado (sin truncar). NULL si Status = Success.</summary>
    public string? ErrorMessage { get; set; }

    public int? DurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
