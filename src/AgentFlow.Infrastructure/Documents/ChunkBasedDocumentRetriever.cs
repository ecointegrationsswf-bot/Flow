using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Embeddings;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Documents;

/// <summary>
/// Implementación de <see cref="IDocumentRetriever"/>: embed la query → cosine
/// similarity en memoria contra todos los chunks del tenant → top K.
///
/// Cache: los chunks de un tenant se cachean por 5 minutos en MemoryCache para
/// evitar pegar a la BD en cada mensaje. Cuando se reindexa un PDF, los chunks
/// nuevos no se ven hasta que expire el cache — aceptable para los primeros 5
/// minutos post-upload (el upload es asíncrono igual, el indexado típicamente
/// tarda 20-60s).
///
/// Costo por consulta:
///   - 1 llamada a OpenAI Embeddings (texto pregunta, ~30 tok)
///   - 1 query a SQL Server (si no hay cache) — TenantId+CampaignTemplateId indexado
///   - Cosine similarity en memoria (O(N) donde N = chunks del tenant; típico &lt;500)
/// </summary>
public class ChunkBasedDocumentRetriever(
    AgentFlowDbContext db,
    IEmbeddingService embeddings,
    IMemoryCache cache,
    ISystemAuditLogger audit,
    ILogger<ChunkBasedDocumentRetriever> log) : IDocumentRetriever
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    // Boost del score cuando el chunk viene del template prioritario. Empírico:
    // 0.05 hace que un chunk "casi tan relevante" del template activo gane a uno
    // marginalmente mejor de otro maestro — alineado con la intuición humana
    // (la conversación es de cobros → privilegio docs del maestro Cobros).
    private const float PriorityBoost = 0.05f;

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(
        Guid tenantId,
        Guid? prioritizeTemplateId,
        string query,
        int topK = 5,
        float minScore = 0.25f,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        // 1. Embed la query. Si falla (OpenAI down/429), logueamos a auditoría
        // pero NO propagamos — el agente cae a su system prompt base y responde
        // sin contexto documental (mejor que tirar fallback al cliente).
        float[] queryVec;
        try
        {
            queryVec = await embeddings.GenerateAsync(query, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "[RAG Retrieve] embed de query falló — agente sin contexto documental. tenant={TenantId}", tenantId);
            await audit.LogErrorAsync(
                category: "RAG_RETRIEVAL",
                message: $"OpenAI Embeddings falló al recuperar contexto para query.",
                ex: ex,
                tenantId: tenantId,
                contextJson: $"{{\"query\":\"{System.Text.Json.JsonEncodedText.Encode(query.Length > 200 ? query[..200] : query)}\"}}",
                ct: ct);
            return [];
        }

        // 2. Cargar chunks del tenant (con cache).
        var chunks = await GetTenantChunksAsync(tenantId, ct);
        if (chunks.Count == 0)
        {
            log.LogInformation("[RAG Retrieve] tenant={TenantId}: sin chunks indexados.", tenantId);
            return [];
        }

        // 3. Cosine similarity + priority boost.
        var scored = new List<(ChunkRecord Chunk, float Score)>(chunks.Count);
        foreach (var c in chunks)
        {
            var raw = EmbeddingSerializer.CosineSimilarity(queryVec, c.Embedding);
            var boosted = (prioritizeTemplateId.HasValue && c.CampaignTemplateId == prioritizeTemplateId.Value)
                ? raw + PriorityBoost
                : raw;
            scored.Add((c, boosted));
        }

        // 4. Filtrar por umbral y tomar topK.
        var top = scored
            .Where(x => x.Score >= minScore)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();

        log.LogInformation(
            "[RAG Retrieve] tenant={TenantId} query='{Q}' scanned={Total} matched={Top} bestScore={Best:F3}",
            tenantId,
            query.Length > 60 ? query[..60] : query,
            chunks.Count, top.Count,
            top.FirstOrDefault().Score);

        return top
            .Select(x => new RetrievedChunk(
                DocumentId:  x.Chunk.DocumentId,
                FileName:    x.Chunk.FileName,
                Description: x.Chunk.Description,
                PageNumber:  x.Chunk.PageNumber,
                Text:        x.Chunk.Text,
                Score:       x.Score))
            .ToList();
    }

    /// <summary>
    /// Carga TODOS los chunks indexados del tenant (con embedding deserializado)
    /// + metadata del documento padre para citar al cliente. Cacheado 5 min.
    /// </summary>
    private async Task<List<ChunkRecord>> GetTenantChunksAsync(Guid tenantId, CancellationToken ct)
    {
        var cacheKey = $"rag:chunks:{tenantId}";
        if (cache.TryGetValue<List<ChunkRecord>>(cacheKey, out var cached) && cached is not null)
            return cached;

        // JOIN para traer también FileName y Description del documento padre — el
        // retriever las usa al construir el bloque del system prompt.
        // Solo consideramos chunks de documentos YA indexados (IndexedAt no nulo).
        var raw = await (
            from c in db.CampaignTemplateDocumentChunks
            join d in db.CampaignTemplateDocuments on c.DocumentId equals d.Id
            where c.TenantId == tenantId && d.IndexedAt != null
            select new
            {
                c.DocumentId,
                d.FileName,
                d.Description,
                c.CampaignTemplateId,
                c.PageNumber,
                c.Text,
                c.Embedding,
            }).ToListAsync(ct);

        var records = raw.Select(r => new ChunkRecord(
            DocumentId:         r.DocumentId,
            FileName:           r.FileName,
            Description:        r.Description,
            CampaignTemplateId: r.CampaignTemplateId,
            PageNumber:         r.PageNumber,
            Text:               r.Text,
            Embedding:          EmbeddingSerializer.FromBytes(r.Embedding)
        )).ToList();

        cache.Set(cacheKey, records, CacheTtl);
        return records;
    }

    private sealed record ChunkRecord(
        Guid DocumentId, string FileName, string? Description,
        Guid CampaignTemplateId, int PageNumber, string Text, float[] Embedding);
}
