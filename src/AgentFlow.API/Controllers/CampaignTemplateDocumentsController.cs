using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Documents;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Storage;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

/// <summary>
/// Endpoints para gestionar los PDFs de referencia de un maestro de campaña.
/// Los documentos se almacenan en Azure Blob Storage bajo la ruta
/// {tenantId}/campaign-templates/{templateId}/{Guid}_{fileName}.
/// El agente los usa como contexto al responder mensajes de la campaña.
/// </summary>
[ApiController]
// [Authorize(Roles = "Admin,Supervisor")] // TODO: habilitar cuando auth esté configurado
[Route("api/campaign-templates/{templateId:guid}/documents")]
public class CampaignTemplateDocumentsController(
    ITenantContext tenantCtx,
    AgentFlowDbContext db,
    IBlobStorageService blobStorage,
    IBackgroundJobClient backgroundJobs,
    IDocumentRetriever ragRetriever) : ControllerBase
{
    // Límites duros por maestro — se aplican al subir. Defensivo ante context window
    // y costos de Anthropic. Los topes son intencionalmente generosos: el verdadero
    // candado de costos es el RAG (chunking + embeddings + retrieval limita lo que
    // entra al prompt, no el PDF crudo).
    private const int MaxDocumentsPerTemplate = 20;
    private const long MaxTotalBytesPerTemplate = 100L * 1024 * 1024; // 100 MB
    private const long MaxBytesPerDocument = 100L * 1024 * 1024;      // 100 MB


    [HttpGet]
    public async Task<IActionResult> List(Guid templateId, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var docs = await db.CampaignTemplateDocuments
            .Where(d => d.CampaignTemplateId == templateId && d.TenantId == tenantId)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new
            {
                d.Id,
                d.FileName,
                d.BlobUrl,
                d.ContentType,
                d.FileSizeBytes,
                d.UploadedAt,
                d.Description,
            })
            .ToListAsync(ct);

        return Ok(docs);
    }

    [HttpPost]
    [RequestSizeLimit(100L * 1024 * 1024)] // 100 MB
    public async Task<IActionResult> Upload(
        Guid templateId,
        IFormFile file,
        [FromForm] string? description,
        CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        if (file.ContentType != "application/pdf")
            return BadRequest(new { error = "Solo se permiten archivos PDF." });

        if (file.Length > MaxBytesPerDocument)
            return BadRequest(new { error = $"El archivo excede el límite de {MaxBytesPerDocument / (1024 * 1024)} MB por documento." });

        var template = await db.CampaignTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.TenantId == tenantId, ct);

        if (template is null)
            return NotFound(new { error = "Maestro de campaña no encontrado." });

        // Validar límites de cantidad y tamaño total del maestro.
        var existing = await db.CampaignTemplateDocuments
            .Where(d => d.CampaignTemplateId == templateId && d.TenantId == tenantId)
            .Select(d => new { d.FileSizeBytes })
            .ToListAsync(ct);

        if (existing.Count >= MaxDocumentsPerTemplate)
            return BadRequest(new { error = $"Este maestro ya tiene el máximo de {MaxDocumentsPerTemplate} documentos. Eliminá uno para subir otro." });

        var currentTotal = existing.Sum(d => d.FileSizeBytes);
        if (currentTotal + file.Length > MaxTotalBytesPerTemplate)
        {
            var remainingMb = Math.Max(0, MaxTotalBytesPerTemplate - currentTotal) / (1024.0 * 1024.0);
            return BadRequest(new
            {
                error = $"El tamaño total de los documentos del maestro excedería {MaxTotalBytesPerTemplate / (1024 * 1024)} MB. Espacio restante: {remainingMb:F1} MB."
            });
        }

        var blobPath = $"{tenantId}/campaign-templates/{templateId}/{Guid.NewGuid()}_{file.FileName}";

        await using var stream = file.OpenReadStream();
        var blobUrl = await blobStorage.UploadAsync(blobPath, stream, file.ContentType, ct);

        var doc = new CampaignTemplateDocument
        {
            Id = Guid.NewGuid(),
            CampaignTemplateId = templateId,
            TenantId = tenantId,
            FileName = file.FileName,
            BlobUrl = blobUrl,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            UploadedAt = DateTime.UtcNow,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        };

        db.CampaignTemplateDocuments.Add(doc);
        await db.SaveChangesAsync(ct);

        // ── Disparar indexado RAG asíncrono ─────────────────────────────────
        // Hangfire persiste el job en BD y un servidor Hangfire lo levanta
        // (API en site12 o Worker on-prem — el primero que esté libre). Si
        // la indexación falla, hay 3 reintentos automáticos; tras eso el
        // documento queda con IndexingError persistido para que el admin
        // pueda reintentar manualmente desde la UI.
        try
        {
            backgroundJobs.Enqueue<DocumentIndexerHangfireJob>(
                j => j.IndexAsync(doc.Id, false));
        }
        catch (Exception ex)
        {
            // No-bloqueante: si Hangfire está caído, el upload sigue siendo
            // exitoso — el PDF queda no-indexado y se puede reintentar después.
            Console.WriteLine($"[CTDocs] No se pudo encolar indexado RAG de {doc.Id}: {ex.Message}");
        }

        return Ok(new
        {
            doc.Id,
            doc.FileName,
            doc.BlobUrl,
            doc.ContentType,
            doc.FileSizeBytes,
            doc.UploadedAt,
            doc.Description,
        });
    }

    /// <summary>
    /// Fuerza el reindexado RAG de un documento. Útil cuando:
    ///   - El indexado original falló (IndexingError no nulo) y queremos reintentar.
    ///   - Se mejora el chunker o se cambia el modelo de embedding y necesitamos
    ///     reprocesar el PDF para aprovecharlo.
    ///   - El PDF se subió antes de que existiera el sistema RAG (back-fill).
    /// </summary>
    [HttpPost("{docId:guid}/reindex")]
    public async Task<IActionResult> Reindex(Guid templateId, Guid docId, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var exists = await db.CampaignTemplateDocuments
            .AnyAsync(d => d.Id == docId
                        && d.CampaignTemplateId == templateId
                        && d.TenantId == tenantId, ct);
        if (!exists) return NotFound(new { error = "Documento no encontrado." });

        backgroundJobs.Enqueue<DocumentIndexerHangfireJob>(j => j.IndexAsync(docId, true));
        return Accepted(new { docId, queued = true });
    }

    /// <summary>
    /// Diagnóstico RAG — recibe una pregunta y devuelve los top chunks que el
    /// retriever recuperaría para esa query. Útil para validar la calidad del
    /// indexado sin tener que pasar por una conversación WhatsApp real.
    /// El endpoint NO necesita que el tenant tenga UseRagRetrieval=true.
    /// </summary>
    public record RagDiagnoseRequest(string Query, int? TopK, float? MinScore);

    [HttpPost("rag-diagnose")]
    public async Task<IActionResult> RagDiagnose(
        Guid templateId, [FromBody] RagDiagnoseRequest req, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        if (string.IsNullOrWhiteSpace(req.Query))
            return BadRequest(new { error = "Query es requerido." });

        var chunks = await ragRetriever.RetrieveAsync(
            tenantId,
            prioritizeTemplateId: templateId,
            query: req.Query,
            topK:     req.TopK ?? 5,
            minScore: req.MinScore ?? 0.25f,
            ct: ct);

        return Ok(new
        {
            query     = req.Query,
            tenantId,
            templateId,
            matched   = chunks.Count,
            results   = chunks.Select(c => new
            {
                c.FileName,
                c.PageNumber,
                Score = Math.Round(c.Score, 3),
                Preview = c.Text.Length > 220 ? c.Text[..220] + "…" : c.Text,
            }),
        });
    }

    /// <summary>
    /// Devuelve estado de indexado RAG de los documentos del maestro — para que
    /// la UI del admin muestre íconos de "Indexando…", "Indexado ✓" o
    /// "Error ⚠" con detalle.
    /// </summary>
    [HttpGet("indexing-status")]
    public async Task<IActionResult> IndexingStatus(Guid templateId, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var rows = await db.CampaignTemplateDocuments
            .Where(d => d.CampaignTemplateId == templateId && d.TenantId == tenantId)
            .Select(d => new
            {
                d.Id,
                d.FileName,
                d.IndexedAt,
                d.IndexedTokenCount,
                d.IndexingError,
                ChunkCount = db.CampaignTemplateDocumentChunks.Count(c => c.DocumentId == d.Id),
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    public record UpdateDocumentDescriptionRequest(string? Description);

    /// <summary>
    /// Actualiza la descripción de un documento — usada por el agente para decidir
    /// cuándo consultar el PDF durante una conversación.
    /// </summary>
    [HttpPatch("{docId:guid}")]
    public async Task<IActionResult> UpdateDescription(
        Guid templateId,
        Guid docId,
        [FromBody] UpdateDocumentDescriptionRequest req,
        CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var doc = await db.CampaignTemplateDocuments
            .FirstOrDefaultAsync(d => d.Id == docId && d.CampaignTemplateId == templateId && d.TenantId == tenantId, ct);

        if (doc is null)
            return NotFound(new { error = "Documento no encontrado." });

        doc.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        await db.SaveChangesAsync(ct);

        return Ok(new { doc.Id, doc.Description });
    }

    /// <summary>Sirve el PDF para visualización en el navegador (inline).</summary>
    [HttpGet("{docId:guid}/preview")]
    public async Task<IActionResult> Preview(Guid templateId, Guid docId, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var doc = await db.CampaignTemplateDocuments
            .FirstOrDefaultAsync(d => d.Id == docId && d.CampaignTemplateId == templateId && d.TenantId == tenantId, ct);

        if (doc is null)
            return NotFound(new { error = "Documento no encontrado." });

        var blobPath = ExtractBlobPath(doc.BlobUrl);
        var (content, contentType) = await blobStorage.DownloadAsync(blobPath, ct);

        Response.Headers.Append("Content-Disposition", $"inline; filename=\"{doc.FileName}\"");
        return File(content, contentType);
    }

    /// <summary>Descarga el PDF como attachment.</summary>
    [HttpGet("{docId:guid}/download")]
    public async Task<IActionResult> Download(Guid templateId, Guid docId, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var doc = await db.CampaignTemplateDocuments
            .FirstOrDefaultAsync(d => d.Id == docId && d.CampaignTemplateId == templateId && d.TenantId == tenantId, ct);

        if (doc is null)
            return NotFound(new { error = "Documento no encontrado." });

        var blobPath = ExtractBlobPath(doc.BlobUrl);
        var (content, contentType) = await blobStorage.DownloadAsync(blobPath, ct);

        return File(content, contentType, doc.FileName);
    }

    [HttpDelete("{docId:guid}")]
    public async Task<IActionResult> Delete(Guid templateId, Guid docId, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var doc = await db.CampaignTemplateDocuments
            .FirstOrDefaultAsync(d => d.Id == docId && d.CampaignTemplateId == templateId && d.TenantId == tenantId, ct);

        if (doc is null)
            return NotFound(new { error = "Documento no encontrado." });

        var blobPath = ExtractBlobPath(doc.BlobUrl);
        await blobStorage.DeleteAsync(blobPath, ct);

        db.CampaignTemplateDocuments.Remove(doc);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// Extrae el path relativo del blob desde la URL completa de Azure.
    /// Usa string parsing en lugar de Uri para evitar problemas de doble encoding
    /// con caracteres especiales (espacios, paréntesis) en nombres de archivo.
    /// </summary>
    private static string ExtractBlobPath(string blobUrl)
    {
        const string marker = ".blob.core.windows.net/";
        var markerIdx = blobUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIdx < 0) return blobUrl;

        var afterHost = blobUrl[(markerIdx + marker.Length)..];
        var slashIdx = afterHost.IndexOf('/');
        if (slashIdx < 0) return afterHost;

        var blobPath = afterHost[(slashIdx + 1)..];
        return Uri.UnescapeDataString(blobPath);
    }
}
