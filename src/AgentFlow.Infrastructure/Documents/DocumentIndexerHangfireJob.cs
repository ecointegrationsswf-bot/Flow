using AgentFlow.Domain.Interfaces;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Documents;

/// <summary>
/// Wrapper público invocable por Hangfire para disparar el indexado RAG de un
/// documento de forma asíncrona y persistente. La interfaz pública es plana
/// (Task con argumentos primitivos) para que Hangfire pueda serializarla.
///
/// Uso desde un controller / handler:
///   <c>BackgroundJob.Enqueue&lt;DocumentIndexerHangfireJob&gt;(j => j.IndexAsync(docId, false));</c>
///
/// Hangfire serializa el call, lo persiste en su DB, y un Hangfire Server
/// (API en site12 o Worker on-prem — cualquiera que esté conectado a la misma
/// BD) lo levanta y ejecuta. Reintentos automáticos en caso de fallo de
/// infraestructura (default: 10 reintentos con backoff).
/// </summary>
public class DocumentIndexerHangfireJob(
    IDocumentIndexer indexer,
    ILogger<DocumentIndexerHangfireJob> log)
{
    /// <summary>
    /// Entrypoint serializable de Hangfire. <c>AutomaticRetry(Attempts = 3)</c>
    /// porque la mayor parte de las fallas son transitorias (OpenAI 429, blob
    /// timeout). Más allá de 3 reintentos es probable un problema permanente
    /// (PDF corrupto, OpenAI fuera de servicio) — el documento queda con
    /// IndexingError persistido y el admin lo reintenta manualmente.
    /// </summary>
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task IndexAsync(Guid documentId, bool forceReindex)
    {
        log.LogInformation("[RAG Hangfire] arrancando indexado de {DocId} (force={Force})", documentId, forceReindex);
        var result = await indexer.IndexAsync(documentId, forceReindex, CancellationToken.None);

        if (!result.Success)
            log.LogWarning("[RAG Hangfire] indexado falló para {DocId}: {Error}", documentId, result.Error);
        else
            log.LogInformation("[RAG Hangfire] OK {DocId}: {Chunks} chunks, {Tokens} tokens",
                documentId, result.ChunksCreated, result.TotalTokens);
    }
}
