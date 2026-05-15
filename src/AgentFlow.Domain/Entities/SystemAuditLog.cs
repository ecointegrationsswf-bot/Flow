namespace AgentFlow.Domain.Entities;

/// <summary>
/// Tabla general de auditoría de errores y eventos sistémicos que no encajan en
/// las entidades específicas (GestionEvent, IndexingError, InboundMessageQueue,
/// JobExecutions). Existe para que CUALQUIER error operacional sea revisable
/// desde la BD sin requerir acceso a logs del servidor.
///
/// Llenado típico:
///   - Retrieval RAG falla al embed-ear la query (OpenAI cae)
///   - Indexación de PDF falla en un paso intermedio (PdfPig, embedding batch)
///   - Envío de WhatsApp/Email es rechazado por el provider
///   - Errores genéricos de infraestructura (blob storage, DB, Redis)
///
/// Convención: los errores que YA se persisten en tablas específicas (ej:
/// agent-exception en GestionEvents, IndexingError en CampaignTemplateDocuments)
/// también se duplican aquí para tener UNA sola tabla de auditoría cross-cutting.
/// Esto facilita queries del estilo "todos los errores de PASESA en las últimas
/// 24h sin importar la fuente".
/// </summary>
public class SystemAuditLog
{
    public Guid Id { get; set; }

    /// <summary>Cuándo ocurrió el evento (UTC).</summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>
    /// Tenant afectado. NULL para errores globales (descarga de un PDF de un
    /// tenant que ya fue borrado, errores del scheduler antes de resolver tenant).
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Categoría del evento — define el subsistema. Ejemplos:
    /// "RAG_INDEX", "RAG_RETRIEVAL", "EMBEDDING_API", "PDF_EXTRACT",
    /// "WHATSAPP_SEND", "EMAIL_SEND", "AGENT_RUN", "BLOB_DOWNLOAD".
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>"Info", "Warning", "Error", "Critical".</summary>
    public string Severity { get; set; } = "Error";

    /// <summary>Mensaje principal del evento — truncado a 1000 chars.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Stack trace completo cuando hay excepción. Truncado a 4000 chars.</summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// Tipo de entidad relacionada al evento — para correlacionar con otras tablas.
    /// Ejemplos: "Document", "Conversation", "Message", "Campaign".
    /// </summary>
    public string? RelatedEntityType { get; set; }

    /// <summary>Id de la entidad relacionada (cuando aplica).</summary>
    public Guid? RelatedEntityId { get; set; }

    /// <summary>
    /// JSON con contexto adicional — payload de la operación, parámetros, etc.
    /// Truncado a 4000 chars al persistir. NO debe contener secretos.
    /// </summary>
    public string? Context { get; set; }
}
