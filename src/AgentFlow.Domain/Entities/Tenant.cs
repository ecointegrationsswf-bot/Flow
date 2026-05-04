using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

/// <summary>
/// Representa un cliente/corredor que usa la plataforma.
/// Cada tenant tiene su propio número de WhatsApp y configuración de proveedor.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;           // identificador URL-safe
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Canal WhatsApp
    public ProviderType WhatsAppProvider { get; set; }
    public string WhatsAppPhoneNumber { get; set; } = string.Empty;
    public string WhatsAppApiToken { get; set; } = string.Empty;  // cifrado en BD
    public string? WhatsAppInstanceId { get; set; }               // UltraMsg instanceId

    // Facturación y país
    public string Country { get; set; } = string.Empty;
    public decimal MonthlyBillingAmount { get; set; }

    // Configuración horaria
    public TimeOnly BusinessHoursStart { get; set; } = new(8, 0);
    public TimeOnly BusinessHoursEnd { get; set; }   = new(17, 0);
    public string TimeZone { get; set; } = "America/Panama";

    // Configuración LLM — cada tenant elige su proveedor de IA y su API key
    public LlmProviderType LlmProvider { get; set; } = LlmProviderType.Anthropic;
    public string? LlmApiKey { get; set; }            // cifrado en BD, nunca se expone completo
    public string LlmModel { get; set; } = "claude-sonnet-4-6";  // modelo por defecto

    // Configuración SendGrid para envío de emails
    public string? SendGridApiKey { get; set; }
    public string? SenderEmail { get; set; }

    // Throttling de campañas — segundos de espera entre cada mensaje enviado
    // Valor mínimo: 3. Valor máximo: 120. Default: 10 (seguro para WhatsApp).
    public int CampaignMessageDelaySeconds { get; set; } = 10;

    /// <summary>
    /// Tope de mensajes por minuto que el CampaignWorker puede emitir para este tenant.
    /// Default 6 (un mensaje cada 10s, alineado con CampaignMessageDelaySeconds).
    /// </summary>
    public int CampaignMessagesPerMinute { get; set; } = 6;

    /// <summary>
    /// Tope de mensajes por hora por tenant. Reemplaza la constante hardcoded
    /// MaxPerHour=200 en CampaignDispatcherService cuando se migra al worker v2.
    /// </summary>
    public int CampaignMaxPerHour { get; set; } = 200;

    /// <summary>
    /// Tope diario de mensajes por tenant. Aplica al total enviado por todas las
    /// campañas activas del tenant en el día calendario (timezone del tenant).
    /// </summary>
    public int CampaignMaxPerDay { get; set; } = 1000;

    /// <summary>
    /// Off-switch del despacho v2 a nivel tenant. Cuando es false, el CampaignWorker
    /// ignora las campañas de este tenant aunque estén en estado Running.
    /// Útil para pausar emergencias sin tocar el estado de las campañas.
    /// </summary>
    public bool CampaignDispatchEnabled { get; set; } = true;

    /// <summary>
    /// Tiempo de espera (debounce) para agrupar mensajes entrantes del mismo cliente
    /// antes de procesarlos con el agente. Evita que el agente responda mensaje por
    /// mensaje cuando el cliente escribe por partes (ej: "hola", "cómo estás?",
    /// "me puedes ayudar"). Durante el debounce los mensajes se concatenan y se
    /// procesan como un solo turno.
    /// Rango: 0–15. Default 5. 0 = deshabilitado (comportamiento anterior, procesa cada mensaje al instante).
    /// </summary>
    public int MessageBufferSeconds { get; set; } = 5;

    /// <summary>
    /// Interruptor global del Cerebro. Cuando es false el sistema funciona igual que hoy.
    /// Cuando es true, todos los mensajes de este tenant pasan por el Cerebro.
    /// </summary>
    public bool BrainEnabled { get; set; }

    /// <summary>
    /// Interruptor global del Webhook Contract System.
    /// Cuando es false el ActionExecutorService no se invoca — el flujo actual queda intacto.
    /// Cuando es true, el sistema ejecuta webhooks basados en InputSchema/OutputSchema
    /// configurados en CampaignTemplates.ActionConfigs por acción.
    /// </summary>
    public bool WebhookContractEnabled { get; set; }

    /// <summary>
    /// Interruptor para inyectar Documentos de Referencia (PDFs adjuntos al maestro de campaña)
    /// en el contexto del agente. Cuando es false, el sistema NO inyecta los PDFs al prompt
    /// ni al request Anthropic — comportamiento idéntico a antes de la feature.
    /// Default false: opt-in explícito para controlar rollout. La migración backfilleó este
    /// campo en true para tenants que ya tenían documentos cargados.
    /// </summary>
    public bool ReferenceDocumentsEnabled { get; set; }

    /// <summary>
    /// Lista de IDs de PromptTemplates asignados a este tenant. Los prompts son un
    /// catálogo global; esta columna JSON restringe qué prompts son visibles en
    /// el formulario del maestro de campaña del tenant.
    /// Si la lista está vacía, el tenant ve TODOS los prompts activos (retrocompat).
    /// </summary>
    public List<Guid> AssignedPromptIds { get; set; } = [];

    /// <summary>
    /// Lista de IDs de ActionDefinitions asignadas (visibles) a este tenant. Las acciones
    /// existen con TenantId, pero el super admin puede marcar cuáles están habilitadas
    /// para aparecer en el selector del maestro de campaña.
    /// Si la lista está vacía, el tenant ve TODAS sus acciones activas (retrocompat).
    /// </summary>
    public List<Guid> AssignedActionIds { get; set; } = [];

    /// <summary>
    /// Prompt de análisis para el ConversationLabelingJob. Reemplaza al SystemPromptText
    /// hardcoded del worker. Cuando es null, el worker usa el prompt por defecto.
    /// </summary>
    public string? LabelingAnalysisPrompt { get; set; }

    /// <summary>
    /// Schema JSON que el LLM debe devolver además del label. Cuando está configurado,
    /// el worker pide a Claude que produzca un JSON con esta estructura y lo persiste
    /// en Conversation.LabelingResultJson — los webhooks pueden mapear sus campos vía
    /// sourceType=labelingResult en el InputSchema.
    /// Ejemplo: {"comentario": "string", "fechaPago": "yyyy-MM-dd", "montoPagar": 0}
    /// </summary>
    public string? LabelingResultSchemaPrompt { get; set; }

    /// <summary>
    /// Código de país telefónico por defecto para este tenant. Ej: "507" (Panamá), "57" (Colombia).
    /// Usado por el módulo de morosidad como fallback cuando la ActionDelinquencyConfig
    /// no especifica un CodigoPais propio.
    /// </summary>
    public string CodigoPaisDefault { get; set; } = "507";

    public ICollection<AgentDefinition> Agents { get; set; } = [];
    public ICollection<Campaign> Campaigns { get; set; } = [];
    public ICollection<AppUser> Users { get; set; } = [];
    public ICollection<WhatsAppLine> WhatsAppLines { get; set; } = [];
}
