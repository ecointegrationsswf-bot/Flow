using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

/// <summary>
/// Definición de un agente IA: prompt, canal, comportamiento y condiciones.
/// Un tenant puede tener múltiples agentes; el Context Dispatcher elige cuál activar.
/// </summary>
public class AgentDefinition
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public AgentType Type { get; set; }
    public bool IsActive { get; set; } = true;

    // Prompt e identidad
    public string SystemPrompt { get; set; } = string.Empty;
    public string? Tone { get; set; }           // formal, amigable, neutro
    public string Language { get; set; } = "es";
    public string? AvatarName { get; set; }     // nombre que usa el agente ("Sofía", "Alejandro")

    // Canales habilitados
    public List<ChannelType> EnabledChannels { get; set; } = [];

    // Comportamiento
    public TimeOnly? SendFrom { get; set; }
    public TimeOnly? SendUntil { get; set; }
    public int MaxRetries { get; set; } = 3;
    public int RetryIntervalHours { get; set; } = 24;
    public int InactivityCloseHours { get; set; } = 72;

    // Condición de cierre automático
    public string? CloseConditionKeyword { get; set; }  // ej: "pagó", "compromiso"

    // LLM config
    public string LlmModel { get; set; } = "claude-sonnet-4-6";
    public double Temperature { get; set; } = 0.3;
    public int MaxTokens { get; set; } = 1024;

    // Linea de WhatsApp asociada (opcional)
    public Guid? WhatsAppLineId { get; set; }
    public WhatsAppLine? WhatsAppLine { get; set; }

    // Plantilla de origen (para actualizaciones desde admin)
    public Guid? SourceTemplateId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Documentos de referencia (PDFs)
    public List<AgentDocument> Documents { get; set; } = [];
}
