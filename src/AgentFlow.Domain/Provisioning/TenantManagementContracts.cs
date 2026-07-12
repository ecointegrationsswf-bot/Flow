namespace AgentFlow.Domain.Provisioning;

/// <summary>
/// API de gestión de maestros para PARTNERS externos (Ludo CRM hoy; reutilizable para
/// otros sistemas mañana). Capa de servicio GENÉRICA por <c>TenantId</c> — la resolución
/// del partner (ludoTenantId→TenantId, HMAC) vive en el controller de cada partner.
/// </summary>
public sealed record UpdateMasterRequest(
    string AgentSlug,
    // Opcional: apuntar a una VERSIÓN específica del maestro (ej. el borrador creado por
    // una regeneración — el id sale de la respuesta del PUT o de GET /masters). Sin él,
    // se resuelve el primario activo del agente (o el más reciente). Con templateId +
    // activar:true, la versión indicada REEMPLAZA a la primaria actual (swap).
    Guid? TemplateId = null,
    // Nuevo objetivo en lenguaje natural → REGENERA el prompt con IA (versionado seguro:
    // si el maestro está activo se crea un borrador nuevo; si es borrador, en sitio).
    string? Objetivo = null,
    // Prompt literal → se setea directo. En vertical "seguro" el gate de autenticación
    // vetado se ANTEPONE siempre (inviolable, lección PASESA).
    string? SystemPrompt = null,
    // Horario de envío del maestro ("HH:mm"). Prevalece sobre el del tenant.
    string? SendFrom = null,
    string? SendUntil = null,
    // true = activar (si el agente no tiene primario activo, se promueve a primario).
    // false = desactivar (deja de usarse para conversaciones/campañas nuevas).
    bool? Activar = null);

/// <summary>Documento de referencia (PDF) que el agente usa como contexto (RAG).</summary>
public sealed record UploadDocumentRequest(
    string AgentSlug,
    string FileName,
    string Base64,
    string? Description = null);

public sealed record UpdateTenantHoursRequest(
    string BusinessHoursStart,   // "HH:mm"
    string BusinessHoursEnd);

public sealed record MasterManagementResult(
    bool Ok,
    string Message,
    Guid? TemplateId = null,
    bool? IsActive = null,
    bool? IsPrimary = null,
    Guid? DocumentId = null);

/// <summary>Estado de un maestro, para que el partner consulte antes/después de editar.</summary>
public sealed record MasterState(
    string AgentSlug,
    Guid TemplateId,
    string Name,
    bool IsActive,
    bool IsPrimaryForAgent,
    string? Objetivo,
    string? SendFrom,
    string? SendUntil,
    int DocumentCount,
    DateTime? UpdatedAt);

/// <summary>Documento de referencia existente (para listar y poder eliminar por id).</summary>
public sealed record DocumentState(
    Guid DocumentId,
    string FileName,
    string? Description,
    long FileSizeBytes,
    DateTime UploadedAt,
    bool Indexed);

public interface ITenantMasterManagementService
{
    Task<IReadOnlyList<MasterState>> GetMastersAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<DocumentState>?> ListDocumentsAsync(Guid tenantId, string agentSlug, CancellationToken ct);
    Task<MasterManagementResult> UpdateMasterAsync(Guid tenantId, UpdateMasterRequest req, CancellationToken ct);
    Task<MasterManagementResult> AddDocumentAsync(Guid tenantId, UploadDocumentRequest req, CancellationToken ct);
    Task<MasterManagementResult> RemoveDocumentAsync(Guid tenantId, string agentSlug, Guid documentId, CancellationToken ct);
    Task<MasterManagementResult> UpdateTenantHoursAsync(Guid tenantId, UpdateTenantHoursRequest req, CancellationToken ct);
}
