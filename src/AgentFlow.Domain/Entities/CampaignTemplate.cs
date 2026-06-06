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

    /// <summary>
    /// JSON array (List&lt;Guid?&gt;) de IDs de plantilla Meta de seguimiento, paralelo a
    /// FollowUpHours. Índice i → plantilla a usar en el follow-up i cuando la línea es
    /// Meta (en vez del texto libre de FollowUpMessagesJson, inválido fuera de 24h).
    /// null en un índice = sin plantilla configurada (el Worker salta ese seguimiento).
    /// </summary>
    public string? FollowUpTemplateIdsJson { get; set; }

    // Cierre automático — horas después de enviada
    public int AutoCloseHours { get; set; } = 72;

    /// <summary>
    /// Mensaje enviado al cliente cuando la campaña se cierra automáticamente
    /// por AutoCloseHours sin actividad. NULL = cierra sin avisar.
    /// </summary>
    public string? AutoCloseMessage { get; set; }

    // ── Fase 3 — Etiquetado IA ────────────────────────────────────────────
    // El job de etiquetado se modela íntegramente en /admin/scheduled-jobs:
    // - Un ScheduledWebhookJob Cron (la expresión cron es el horario)
    //   apuntando al ActionDefinition global "LABEL_CONVERSATIONS".
    // - Un segundo ScheduledWebhookJob EventBased con TriggerEvent="ConversationLabeled"
    //   y Scope="PerConversation" apuntando a una Action del cliente con su propio
    //   InputSchema/OutputSchema, para enviar el webhook de resultado.
    // El maestro de campaña ya no necesita campos específicos de etiquetado.

    // Etiquetas de seguimiento (IDs del maestro de etiquetas)
    public List<Guid> LabelIds { get; set; } = [];

    // Configuración de email (usa SendGrid del Tenant)
    public bool SendEmail { get; set; }
    public string? EmailAddress { get; set; }

    // ── Plantilla de email personalizable ─────────────────────────────────
    // Usado por SendEmailResumeService (y futuras acciones que envíen email)
    // cuando el template tiene asignada una ActionDefinition con SendsEmail=true.
    // Si EmailBodyHtml es null o vacío, se usa el cuerpo hardcoded original
    // como fallback (compatibilidad con maestros antiguos).
    //
    // Soporta variables {{namespace.field}} resueltas por EmailTemplateRenderer:
    //   {{cliente.nombre}}, {{cliente.poliza}}, {{conversacion.resumen}}, etc.
    public string? EmailSubject { get; set; }
    public string? EmailBodyHtml { get; set; }
    public string? EmailBodyText { get; set; }
    public DateTime? EmailTemplateUpdatedAt { get; set; }

    // ── Layout adaptativo del correo (Fase A) ─────────────────────────────
    // El sistema agrupa por teléfono al subir la campaña. Si el cliente
    // termina con >= UmbralCorporativo registros (pólizas, productos, etc.),
    // el renderer usa el bloque "corporativo" (KPIs + agrupado + top urgentes).
    // Default 10 — configurable por maestro.
    public int UmbralCorporativo { get; set; } = 10;

    /// <summary>
    /// Mapeo genérico del dataset para que el correo no esté acoplado al dominio
    /// de seguros. JSON con la forma:
    /// {
    ///   "label": "Pólizas",            // título de la colección
    ///   "titleColumn": "numero",        // columna del título de cada item
    ///   "subtitleColumn": "aseguradora",// subtítulo (opcional)
    ///   "categoryColumn": "ramo",       // badge categórico (opcional)
    ///   "amountColumn": "saldo",        // columna del monto (opcional)
    ///   "detailColumns": ["marca","placa","vencimiento"] // 2x N grid
    /// }
    /// Si es NULL, el renderer cae a defaults razonables.
    /// </summary>
    public string? ItemsConfig { get; set; }

    /// <summary>
    /// JSON con datos de muestra (típicamente la primera fila del archivo
    /// modelo que el usuario subió al configurar el maestro). Se usa para:
    ///   - poblar dropdowns de columnas en el tab Correo del maestro
    ///   - renderizar previews del correo con datos reales (en vez del sample hardcoded)
    /// El formato esperado es un array JSON con N objetos (cada uno = 1 fila),
    /// igual que ContactDataJson produce el FixedFormatCampaignService.
    /// </summary>
    public string? SampleDataJson { get; set; }

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

    /// <summary>
    /// Marca este maestro como el "primario" del agente: el que responde a
    /// mensajes orgánicos (sin campaña activa) cuando Tenant.BrainEnabled = false.
    /// En no-Brain solo UNO por (TenantId, AgentDefinitionId) puede tener este flag.
    /// El swap se hace explícitamente desde la UI (modal de confirmación).
    /// En Brain enabled este flag se ignora (el Cerebro elige por slug).
    /// </summary>
    public bool IsPrimaryForAgent { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Documentos de referencia (PDFs) — el agente los usa como contexto al responder.
    public List<CampaignTemplateDocument> Documents { get; set; } = [];
}
