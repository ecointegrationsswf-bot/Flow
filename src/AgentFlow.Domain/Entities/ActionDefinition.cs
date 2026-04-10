using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

public class ActionDefinition
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public string Name { get; set; } = string.Empty; // SEND_MESSAGE, SEND_RESUME, TRANSFER_CHAT, PREMIUM, etc.
    public string? Description { get; set; }
    public bool RequiresWebhook { get; set; }
    public bool SendsEmail { get; set; }
    public bool SendsSms { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookMethod { get; set; } // GET, POST, PUT

    // ── Taxonomía del Webhook Contract System (3 dimensiones) ──
    /// <summary>Cuándo se ejecuta el webhook. Default FireAndForget para compatibilidad con acciones existentes.</summary>
    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.FireAndForget;

    /// <summary>De dónde vienen los parámetros de entrada. Default SystemOnly.</summary>
    public ParamSource ParamSource { get; set; } = ParamSource.SystemOnly;

    /// <summary>Qué pasa en la conversación después de ejecutar. Default Transparent.</summary>
    public ConversationImpact ConversationImpact { get; set; } = ConversationImpact.Transparent;

    /// <summary>
    /// JSON array de RequiredParam. NULL cuando ParamSource = SystemOnly.
    /// Define qué datos el agente debe recolectar del cliente antes de ejecutar la acción.
    /// </summary>
    public string? RequiredParams { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
