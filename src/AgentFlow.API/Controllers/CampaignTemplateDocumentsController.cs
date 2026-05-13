using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Storage;
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
    IBlobStorageService blobStorage) : ControllerBase
{
    // Límites duros por maestro — se aplican al subir. Defensivo ante context window
    // y costos de Anthropic. Alineado con Fase 3 del plan de Referencia Documents.
    private const int MaxDocumentsPerTemplate = 5;
    private const long MaxTotalBytesPerTemplate = 20L * 1024 * 1024; // 20 MB
    private const long MaxBytesPerDocument = 10L * 1024 * 1024;      // 10 MB (ya validado vía RequestSizeLimit)


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
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
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
