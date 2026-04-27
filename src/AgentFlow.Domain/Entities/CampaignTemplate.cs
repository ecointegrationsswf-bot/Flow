using AgentFlow.Domain.Enums;

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

    /// <summary>
    /// JSON array (List<string>) de mensajes de seguimiento, paralelo a FollowUpHours.
    /// Índice i del array → mensaje a enviar al cumplirse FollowUpHours[i].
    /// Soporta variables {nombre}, {poliza}, {aseguradora}, {monto_pendiente}.
    /// NULL = no envía seguimientos automáticos (FollowUpExecutor lo trata como skip).
    /// </summary>
    public string? FollowUpMessagesJson { get; set; }

    // Cierre automático — horas después de enviada
    public int AutoCloseHours { get; set; } = 72;

    /// <summary>
    /// Mensaje enviado al cliente cuando la campaña se cierra automáticamente
    /// por AutoCloseHours sin actividad. NULL = cierra sin avisar.
    /// </summary>
    public string? AutoCloseMessage { get; set; }

    // ── Fase 3 — Etiquetado IA + Webhook de resultado ─────────────────────
    /// <summary>
    /// Hora UTC (0-23) en que el job diario clasifica las conversaciones cerradas
    /// no etiquetadas de campañas que usan este maestro. NULL = etiquetado deshabilitado.
    /// </summary>
    public int? LabelingJobHourUtc { get; set; }

    /// <summary>
    /// Endpoint del cliente que recibe el resultado de cada conversación etiquetada.
    /// NULL = no se envía webhook (solo se asigna etiqueta internamente).
    /// </summary>
    public string? ResultWebhookUrl { get; set; }

    /// <summary>
    /// JSON OutputSchema (formato Webhook Contract System) que define los campos
    /// del payload enviado al cliente. NULL = se envía un payload mínimo por defecto.
    /// </summary>
    public string? ResultOutputSchema { get; set; }

    // Etiquetas de seguimiento (IDs del maestro de etiquetas)
    public List<Guid> LabelIds { get; set; } = [];

    // Configuración de email (usa SendGrid del Tenant)
    public bool SendEmail { get; set; }
    public string? EmailAddress { get; set; }

    // ── Horario de atención de asesores ───────────────────────────────────
    // Días de la semana en que los asesores atienden (0=Domingo … 6=Sábado)
    // Ej: [1,2,3,4,5] = lunes a viernes
    public List<int> AttentionDays { get; set; } = [1, 2, 3, 4, 5];

    // Hora de inicio y fin de atención en formato "HH:mm" (hora de Panamá)
    public string AttentionStartTime { get; set; } = "08:00";
    public string AttentionEndTime { get; set; } = "17:00";

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

    /// <summary>
    /// Política del Cerebro cuando el cliente se desvía del contexto de la campaña.
    /// Se copia a Campaign al crear una campaña desde este maestro.
    /// </summary>
    public OutOfContextPolicy OutOfContextPolicy { get; set; } = OutOfContextPolicy.Contain;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Documentos de referencia (PDFs) — el agente los usa como contexto al responder.
    public List<CampaignTemplateDocument> Documents { get; set; } = [];
}
