namespace AgentFlow.Domain.Entities;

/// <summary>
/// Entrada del registro de agentes por tenant.
/// El ClassifierService del Cerebro usa las Capabilities en lenguaje natural
/// para decidir a qué agente rutear cada mensaje.
/// </summary>
public class AgentRegistryEntry
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    /// <summary>Identificador único por tenant, usado en decisiones del Cerebro (ej: "cobros", "welcome").</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Nombre legible (ej: "Agente de Cobros").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Descripción en lenguaje natural de las capacidades del agente para que Claude tome decisiones de routing.</summary>
    public string Capabilities { get; set; } = string.Empty;

    /// <summary>FK al maestro de campaña completo (prompt, acciones, etiquetas). El Cerebro necesita el paquete completo.</summary>
    public Guid CampaignTemplateId { get; set; }
    public CampaignTemplate CampaignTemplate { get; set; } = null!;

    /// <summary>Exactamente uno por tenant debe ser true. Primer punto de contacto para inbound fríos.</summary>
    public bool IsWelcome { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
