using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Construye el bloque de texto "DOCUMENTOS DE REFERENCIA" que se inyecta al
/// final del system prompt cuando un tenant tiene PDFs adjuntos.
/// Provee además los bytes de los PDFs (base64) para adjuntar al primer
/// mensaje user del request a la API de Anthropic como bloques "document".
///
/// Estrategia tenant-wide: los PDFs son visibles a todos los agentes del
/// tenant, no solo al maestro activo. Esto evita que el cliente reciba
/// respuestas incorrectas cuando el Cerebro enruta a un agente cuyo maestro
/// no tiene los documentos transversales del corredor (red de hospitales,
/// catálogos generales, etc.). El parámetro prioritizeTemplateId solo afecta
/// el orden de carga cuando el cupo (RefDocMaxCount) es menor al total.
/// </summary>
public interface IDocumentReferencePromptBuilder
{
    /// <summary>
    /// Construye el bloque de texto con lista de documentos + reglas de uso.
    /// Devuelve string vacío si el tenant no tiene documentos.
    /// </summary>
    /// <param name="prioritizeTemplateId">Maestro a priorizar (opcional). Sus PDFs aparecen primero en la lista; los demás del tenant siguen por fecha de carga.</param>
    /// <param name="tenantId">Tenant cuyas reglas de visibilidad aplican.</param>
    Task<string> BuildTextBlockAsync(Guid? prioritizeTemplateId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Devuelve los documentos con sus bytes en base64 listos para adjuntarse
    /// al request Anthropic. Lista vacía si no hay documentos en el tenant.
    /// </summary>
    Task<IReadOnlyList<ReferenceDocumentPayload>> GetDocumentsAsBase64Async(
        Guid? prioritizeTemplateId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Devuelve los metadatos (FileName/Description/BlobUrl) de los documentos
    /// del tenant en el orden en que deben adjuntarse al prompt. Se usa para
    /// alimentar AgentRunRequest.ReferenceDocuments sin tener que descargar
    /// los blobs por adelantado (la descarga la hace el AgentRunner).
    /// </summary>
    Task<IReadOnlyList<ReferenceDocument>> GetTenantDocumentsAsync(
        Guid? prioritizeTemplateId, Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// Payload de un documento listo para adjuntarse a la API de Anthropic.
/// </summary>
public sealed record ReferenceDocumentPayload(
    Guid Id,
    string FileName,
    string? Description,
    string Base64Content,
    string MediaType);
