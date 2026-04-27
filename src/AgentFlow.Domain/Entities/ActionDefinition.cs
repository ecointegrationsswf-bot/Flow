using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

public class ActionDefinition
{
    public Guid Id { get; set; }

    /// <summary>
    /// NULL = acción global (catálogo). El super admin asigna globales a cada tenant
    /// desde Tenant.AssignedActionIds. Las acciones con TenantId no-null son legacy
    /// per-tenant (previas a la promoción a globales) y siguen visibles solo en su tenant.
    /// </summary>
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; set; }

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

    /// <summary>
    /// Action Trigger Protocol — TriggerConfig por defecto a nivel de tenant.
    /// JSON serializado de TriggerConfig (description, triggerExamples, requiresConfirmation, clarificationPrompt).
    /// Si un CampaignTemplate tiene su propio triggerConfig en ActionConfigs, el del template gana.
    /// Si no, este default se hereda automáticamente en todos los maestros que incluyan esta acción.
    /// NULL = la acción no se inyecta al prompt del agente a menos que el template la configure.
    /// NOTA: Si DefaultWebhookContract está configurado y contiene triggerConfig, ese tiene prioridad
    /// sobre este campo (el contract es más completo). Este campo se mantiene por retrocompat.
    /// </summary>
    public string? DefaultTriggerConfig { get; set; }

    /// <summary>
    /// Contrato webhook completo por defecto a nivel de tenant. JSON serializado con el mismo
    /// formato que un entry de CampaignTemplate.ActionConfigs[actionId]:
    /// { webhookUrl, webhookMethod, contentType, structure, authType, authValue, apiKeyHeaderName,
    ///   webhookHeaders, timeoutSeconds, inputSchema, outputSchema, triggerConfig }
    ///
    /// Si un CampaignTemplate tiene su propia config en ActionConfigs para esta acción, el template gana.
    /// Si no, este default se hereda automáticamente en TODOS los maestros del tenant.
    ///
    /// Cada tenant configura su propio DefaultWebhookContract — la misma acción (ej: SEND_TEST_EMAIL)
    /// puede tener URL, schemas y trigger diferentes en cada tenant.
    /// NULL = no hay default, el maestro debe configurar el webhook individualmente.
    /// </summary>
    public string? DefaultWebhookContract { get; set; }

    /// <summary>
    /// Configuración de scheduling JSON usada por el ScheduledWebhookWorker para
    /// crear jobs automáticos al asociar esta acción a un trigger cron o evento.
    /// Estructura: { "triggerType": "Cron|EventBased|DelayFromEvent",
    ///               "cronExpression": "0 23 * * *",
    ///               "triggerEvent": "ConversationClosed",
    ///               "delayMinutes": 60,
    ///               "scope": "AllTenants|PerCampaign|PerConversation" }
    /// NULL = la acción no tiene scheduling default; se invoca solo bajo demanda
    /// por el agente o por jobs creados manualmente desde la UI.
    /// </summary>
    public string? ScheduleConfig { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
