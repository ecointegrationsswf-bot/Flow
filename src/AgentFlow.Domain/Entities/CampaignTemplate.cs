namespace AgentFlow.Domain.Entities;

/// <summary>
/// Mapeo de campos para webhooks: define cómo se mapean campos externos a campos internos.
/// </summary>
public class WebhookFieldMapping
{
    /// <summary>Campo externo que llega en el JSON del webhook (ej: "numero_poliza")</summary>
    public string ExternalField { get; set; } = string.Empty;

    /// <summary>Campo interno al que se mapea (ej: "policyNumber", "clientPhone", "amount")</summary>
    public string InternalField { get; set; } = string.Empty;

    /// <summary>Tipo de dato esperado: string | number | date | boolean</summary>
    public string DataType { get; set; } = "string";

    /// <summary>Si el campo es obligatorio</summary>
    public bool IsRequired { get; set; }
}

/// <summary>
/// Maestro de campaña: define las reglas de seguimiento, cierre y etiquetas.
/// Las campañas se crean a partir de un maestro + archivo de contactos.
/// </summary>
public class CampaignTemplate
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public Guid AgentDefinitionId { get; set; }
    public AgentDefinition AgentDefinition { get; set; } = null!;

    // Seguimientos automáticos — lista de horas (ej: [24, 36, 72])
    public List<int> FollowUpHours { get; set; } = [];

    // Cierre automático — horas después de enviada
    public int AutoCloseHours { get; set; } = 72;

    // Etiquetas de seguimiento (IDs del maestro de etiquetas)
    public List<Guid> LabelIds { get; set; } = [];

    // Configuración de email (usa SendGrid del Tenant)
    public bool SendEmail { get; set; }
    public string? EmailAddress { get; set; }

    // ── Prompt y comportamiento del agente para esta campaña ──────────────
    public string SystemPrompt { get; set; } = string.Empty;
    public string? SendFrom { get; set; }
    public string? SendUntil { get; set; }
    public int MaxRetries { get; set; } = 3;
    public int RetryIntervalHours { get; set; } = 24;
    public int InactivityCloseHours { get; set; } = 72;
    public string? CloseConditionKeyword { get; set; }
    public int MaxTokens { get; set; } = 1024;

    // ── Acciones y Prompts vinculados ─────────────────────
    // IDs de acciones globales (definidas en admin) vinculadas a este maestro
    public List<Guid> ActionIds { get; set; } = [];

    // Configuraciones por accion (webhook URL, email, telefono SMS) — JSON serializado
    // Formato: { "actionId": { "webhookUrl": "...", "webhookMethod": "POST", "webhookHeaders": "...", "webhookPayload": "...", "emailAddress": "...", "smsPhoneNumber": "..." } }
    public string? ActionConfigs { get; set; }

    // IDs de prompt templates globales (definidos en admin) vinculados a este maestro
    public List<Guid> PromptTemplateIds { get; set; } = [];

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
