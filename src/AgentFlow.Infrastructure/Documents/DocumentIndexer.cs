using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Embeddings;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Documents;

/// <summary>
/// Implementación de <see cref="IDocumentIndexer"/>. Orquesta el pipeline RAG:
/// descarga PDF → extrae texto → chunks → embeddings → persiste chunks +
/// marca el documento como indexado.
///
/// Idempotencia: si ya hay chunks con TextHash igual al de un chunk recién
/// generado, reutiliza el embedding existente y NO llama a OpenAI por ese
/// chunk. Aplica especialmente al re-indexar (mismas páginas, mismas oraciones).
///
/// Tolerancia a fallos: si algo se rompe (PDF inválido, OpenAI cae, error de BD),
/// persistimos el mensaje en <c>CampaignTemplateDocument.IndexingError</c> y
/// devolvemos un IndexDocumentResult con Success=false. El admin puede
/// reintentar desde la UI. NUNCA propagamos excepciones — los jobs Hangfire
/// no deben fallar por documentos individuales.
/// </summary>
public class DocumentIndexer(
    AgentFlowDbContext db,
    IHttpClientFactory httpClientFactory,
    IPdfTextExtractor pdfExtractor,
    ITextChunker chunker,
    IEmbeddingService embeddings,
    ISystemAuditLogger audit,
    ILogger<DocumentIndexer> log) : IDocumentIndexer
{
    public async Task<IndexDocumentResult> IndexAsync(
        Guid documentId, bool forceReindex = false, CancellationToken ct = default)
    {
        var doc = await db.CampaignTemplateDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null)
        {
            log.LogWarning("[RAG] IndexAsync: documento {DocId} no encontrado.", documentId);
            return new IndexDocumentResult(documentId, false, 0, 0, 0, "Documento no encontrado.");
        }

        // Skip si ya está indexado y no se forzó (idempotencia frente a reintentos).
        if (doc.IndexedAt.HasValue && !forceReindex)
        {
            log.LogInformation(
                "[RAG] {Doc} ya indexado en {IndexedAt}; omitido. (forceReindex=false)",
                doc.FileName, doc.IndexedAt);
            return new IndexDocumentResult(documentId, true, 0, 0, doc.IndexedTokenCount ?? 0, null);
        }

        try
        {
            // ── 1. Descargar bytes del PDF desde Azure Blob ─────────────────
            var bytes = await DownloadAsync(doc.BlobUrl, ct);
            if (bytes is null || bytes.Length == 0)
            {
                await MarkErrorAsync(doc, "No se pudo descargar el PDF del blob.", ct);
                return new IndexDocumentResult(documentId, false, 0, 0, 0, "Descarga vacía.");
            }
            log.LogInformation("[RAG] {Doc} descargado ({Bytes:N0} bytes), extrayendo texto…",
                doc.FileName, bytes.Length);

            // ── 2. Extraer texto por página ─────────────────────────────────
            var pages = pdfExtractor.Extract(bytes);
            var nonEmptyPages = pages.Count(p => !string.IsNullOrWhiteSpace(p.Text));
            if (nonEmptyPages == 0)
            {
                await MarkErrorAsync(doc,
                    "PDF sin texto extraíble. ¿Es un PDF escaneado? OCR no implementado todavía.", ct);
                return new IndexDocumentResult(documentId, false, 0, 0, 0, "PDF sin texto.");
            }

            // ── 3. Chunking ─────────────────────────────────────────────────
            var chunks = chunker.Chunk(pages);
            if (chunks.Count == 0)
            {
                await MarkErrorAsync(doc, "El chunker no produjo fragmentos — texto demasiado corto.", ct);
                return new IndexDocumentResult(documentId, false, 0, 0, 0, "Sin chunks.");
            }
            log.LogInformation("[RAG] {Doc}: {Pages} páginas → {Chunks} chunks",
                doc.FileName, pages.Count, chunks.Count);

            // ── 4. Dedup por TextHash contra chunks ya existentes del mismo doc ─
            // En forceReindex limpiamos primero y arrancamos de cero (más simple
            // que diffear). Si NO es forceReindex pero ya hay chunks (caso raro:
            // IndexedAt=null pero hay chunks huérfanos), también los limpiamos.
            var existing = await db.CampaignTemplateDocumentChunks
                .Where(c => c.DocumentId == doc.Id)
                .ToListAsync(ct);
            if (existing.Count > 0)
            {
                db.CampaignTemplateDocumentChunks.RemoveRange(existing);
                await db.SaveChangesAsync(ct);
                log.LogInformation("[RAG] {Doc}: borrados {N} chunks previos.", doc.FileName, existing.Count);
            }

            // ── 5. Embeddings en batch para todos los chunks ────────────────
            var texts = chunks.Select(c => c.Text).ToList();
            log.LogInformation("[RAG] {Doc}: embedding {N} chunks…", doc.FileName, texts.Count);
            var vectors = await embeddings.GenerateBatchAsync(texts, ct);
            if (vectors.Count != chunks.Count)
            {
                await MarkErrorAsync(doc,
                    $"OpenAI devolvió {vectors.Count} embeddings; esperaba {chunks.Count}.", ct);
                return new IndexDocumentResult(documentId, false, 0, 0, 0, "Mismatch embeddings/chunks.");
            }

            // ── 6. Persistir chunks ─────────────────────────────────────────
            var totalTokens = 0;
            var rows = new List<CampaignTemplateDocumentChunk>(chunks.Count);
            for (var i = 0; i < chunks.Count; i++)
            {
                var c = chunks[i];
                rows.Add(new CampaignTemplateDocumentChunk
                {
                    Id                 = Guid.NewGuid(),
                    DocumentId         = doc.Id,
                    TenantId           = doc.TenantId,
                    CampaignTemplateId = doc.CampaignTemplateId,
                    PageNumber         = c.PageNumber,
                    ChunkIndex         = c.ChunkIndex,
                    Text               = c.Text,
                    TextHash           = ComputeHash(c.Text),
                    Embedding          = EmbeddingSerializer.ToBytes(vectors[i]),
                    TokenCount         = c.TokenCount,
                    CreatedAt          = DateTime.UtcNow,
                });
                totalTokens += c.TokenCount;
            }
            db.CampaignTemplateDocumentChunks.AddRange(rows);

            // ── 7. Marcar el documento como indexado ────────────────────────
            doc.IndexedAt          = DateTime.UtcNow;
            doc.IndexedTokenCount  = totalTokens;
            doc.IndexingError      = null;
            await db.SaveChangesAsync(ct);

            log.LogInformation(
                "[RAG] {Doc} indexado: {Chunks} chunks, ~{Tokens:N0} tokens. tenant={TenantId}",
                doc.FileName, rows.Count, totalTokens, doc.TenantId);

            return new IndexDocumentResult(documentId, true, rows.Count, 0, totalTokens, null);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[RAG] Falló indexado de {DocId} ({FileName})", documentId, doc.FileName);
            await MarkErrorAsync(doc, $"{ex.GetType().Name}: {ex.Message}", ct);
            await audit.LogErrorAsync(
                category: "RAG_INDEX",
                message: $"Falló indexado de PDF: {doc.FileName}",
                ex: ex,
                tenantId: doc.TenantId,
                relatedEntityType: "CampaignTemplateDocument",
                relatedEntityId: doc.Id,
                ct: ct);
            return new IndexDocumentResult(documentId, false, 0, 0, 0, ex.Message);
        }
    }

    private async Task MarkErrorAsync(CampaignTemplateDocument doc, string error, CancellationToken ct)
    {
        try
        {
            doc.IndexingError = error.Length > 500 ? error[..500] : error;
            doc.IndexedAt = null;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[RAG] No pude persistir IndexingError de {DocId}", doc.Id);
        }
    }

    private async Task<byte[]?> DownloadAsync(string blobUrl, CancellationToken ct)
    {
        // Reusamos el patrón existente del AnthropicAgentRunner — descarga vía
        // URL pública del blob (no via IBlobStorageService) porque los blobs
        // están en un container con acceso público para que el agente los
        // adjunte. Encodeamos espacios y caracteres no-URL.
        var safeUrl = EncodeBlobUrl(blobUrl);
        try
        {
            var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(60);
            return await http.GetByteArrayAsync(safeUrl, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[RAG] Error descargando blob {Url}", blobUrl);
            return null;
        }
    }

    private static string EncodeBlobUrl(string rawUrl)
    {
        try
        {
            var u = new Uri(rawUrl);
            var encodedPath = string.Join('/', u.AbsolutePath.Split('/')
                .Select(seg => string.IsNullOrEmpty(seg) ? seg : Uri.EscapeDataString(Uri.UnescapeDataString(seg))));
            return $"{u.Scheme}://{u.Host}{(u.IsDefaultPort ? "" : ":" + u.Port)}{encodedPath}{u.Query}";
        }
        catch
        {
            return rawUrl;
        }
    }

    private static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
