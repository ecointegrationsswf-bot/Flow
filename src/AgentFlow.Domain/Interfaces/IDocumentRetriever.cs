namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Recupera los fragmentos (chunks) más relevantes para una pregunta del cliente
/// usando búsqueda por similitud coseno sobre los embeddings previamente
/// indexados. Es la cara de "lectura" del sistema RAG — el contraparte del
/// <see cref="IDocumentIndexer"/> (escritura).
///
/// Diseño:
///   - Recibe el texto crudo de la pregunta. Internamente lo embed-ea con el mismo
///     modelo que se usó al indexar (text-embedding-3-small).
///   - Filtra chunks por TenantId (multi-tenant strict) y opcionalmente prioriza
///     los del template activo (mismo comportamiento que la inyección de PDFs
///     enteros hoy).
///   - Devuelve top-K chunks ordenados por similitud descendente.
///   - Cosine similarity se hace en memoria — adecuado hasta ~50k chunks por
///     tenant. Cuando crezca, swap por Azure AI Search sin tocar el caller.
///
/// Implementación: <c>ChunkBasedDocumentRetriever</c>.
/// </summary>
public interface IDocumentRetriever
{
    /// <summary>
    /// Recupera los top-K chunks más relevantes para la query.
    /// </summary>
    /// <param name="tenantId">Filtro multi-tenant obligatorio.</param>
    /// <param name="prioritizeTemplateId">
    /// Si tiene valor, sube en el ranking los chunks de ese template (priority
    /// boost). Útil cuando la conversación viene de una campaña: privilegia
    /// los docs del maestro activo aunque haya chunks similares en otros maestros.
    /// </param>
    /// <param name="query">Texto crudo del mensaje del cliente.</param>
    /// <param name="topK">Cantidad de chunks a devolver. Default 5.</param>
    /// <param name="minScore">
    /// Umbral mínimo de similitud (0..1). Chunks por debajo se descartan.
    /// Default 0.25 — empíricamente diferencia ruido de relevancia para
    /// text-embedding-3-small en español.
    /// </param>
    Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(
        Guid tenantId,
        Guid? prioritizeTemplateId,
        string query,
        int topK = 5,
        float minScore = 0.25f,
        CancellationToken ct = default);
}

/// <summary>Chunk recuperado con metadata para citar en la respuesta.</summary>
/// <param name="DocumentId">FK al CampaignTemplateDocument de origen.</param>
/// <param name="FileName">Nombre del archivo original (para cita al cliente).</param>
/// <param name="Description">Descripción del documento (si la hay).</param>
/// <param name="PageNumber">Página del PDF.</param>
/// <param name="Text">Texto del chunk.</param>
/// <param name="Score">Similitud coseno (0..1). Mayor = más relevante.</param>
public record RetrievedChunk(
    Guid DocumentId,
    string FileName,
    string? Description,
    int PageNumber,
    string Text,
    float Score);
