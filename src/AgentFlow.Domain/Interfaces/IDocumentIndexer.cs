namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Orquesta el pipeline completo de indexado RAG de un PDF subido:
///   1. Descarga el PDF del blob storage
///   2. <see cref="IPdfTextExtractor"/> — texto por página
///   3. <see cref="ITextChunker"/> — chunks ~500 tokens
///   4. <see cref="IEmbeddingService"/> — embeddings vía OpenAI
///   5. Persiste <c>CampaignTemplateDocumentChunks</c> y marca el Document con
///      <c>IndexedAt</c> + <c>IndexedTokenCount</c>
///
/// Es idempotente: si ya hay chunks indexados con el mismo TextHash, los reutiliza
/// y no llama a OpenAI por ellos. Si la indexación falla, persiste el error en
/// <c>CampaignTemplateDocument.IndexingError</c> y deja IndexedAt en NULL para
/// que el admin pueda reintentar.
///
/// Diseñado para correr async via Hangfire al subir un PDF, NO inline en el
/// upload — un PDF de 30 páginas tarda ~20s indexando, no queremos bloquear
/// la respuesta HTTP del admin.
/// </summary>
public interface IDocumentIndexer
{
    /// <summary>
    /// Indexa el documento con el ID dado. Si ya está indexado y no se forzó
    /// re-indexado, no hace nada. La operación es resiliente: errores se
    /// persisten en IndexingError y no se propagan.
    /// </summary>
    /// <param name="documentId">CampaignTemplateDocument.Id</param>
    /// <param name="forceReindex">
    /// Si true, borra los chunks existentes y reindexa desde cero — útil cuando
    /// se mejora el chunker o se cambia el modelo de embedding.
    /// </param>
    Task<IndexDocumentResult> IndexAsync(
        Guid documentId, bool forceReindex = false, CancellationToken ct = default);
}

/// <summary>Resumen del indexado de un documento — para logs y UI del admin.</summary>
public record IndexDocumentResult(
    Guid DocumentId,
    bool Success,
    int ChunksCreated,
    int ChunksReused,
    int TotalTokens,
    string? Error);
