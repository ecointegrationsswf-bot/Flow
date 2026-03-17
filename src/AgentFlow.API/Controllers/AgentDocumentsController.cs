using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

[ApiController]
// [Authorize(Roles = "Admin,Supervisor")] // TODO: habilitar cuando auth esté configurado
[Route("api/agents/{agentId:guid}/documents")]
public class AgentDocumentsController(
    ITenantContext tenantCtx,
    AgentFlowDbContext db,
    IBlobStorageService blobStorage) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid agentId, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var docs = await db.AgentDocuments
            .Where(d => d.AgentDefinitionId == agentId && d.TenantId == tenantId)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new
            {
                d.Id,
                d.FileName,
                d.BlobUrl,
                d.ContentType,
                d.FileSizeBytes,
                d.UploadedAt,
            })
            .ToListAsync(ct);

        return Ok(docs);
    }

    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    public async Task<IActionResult> Upload(Guid agentId, IFormFile file, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        if (file.ContentType != "application/pdf")
            return BadRequest(new { error = "Solo se permiten archivos PDF." });

        var agent = await db.AgentDefinitions
            .FirstOrDefaultAsync(a => a.Id == agentId && a.TenantId == tenantId, ct);

        if (agent is null)
            return NotFound(new { error = "Agente no encontrado." });

        var blobPath = $"{tenantId}/{agentId}/{Guid.NewGuid()}_{file.FileName}";

        await using var stream = file.OpenReadStream();
        var blobUrl = await blobStorage.UploadAsync(blobPath, stream, file.ContentType, ct);

        var doc = new AgentDocument
        {
            Id = Guid.NewGuid(),
            AgentDefinitionId = agentId,
            TenantId = tenantId,
            FileName = file.FileName,
            BlobUrl = blobUrl,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            UploadedAt = DateTime.UtcNow,
        };

        db.AgentDocuments.Add(doc);
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            doc.Id,
            doc.FileName,
            doc.BlobUrl,
            doc.ContentType,
            doc.FileSizeBytes,
            doc.UploadedAt,
        });
    }

    /// <summary>
    /// Sirve el PDF para visualizacion en el navegador (inline).
    /// </summary>
    [HttpGet("{docId:guid}/preview")]
    public async Task<IActionResult> Preview(Guid agentId, Guid docId, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var doc = await db.AgentDocuments
            .FirstOrDefaultAsync(d => d.Id == docId && d.AgentDefinitionId == agentId && d.TenantId == tenantId, ct);

        if (doc is null)
            return NotFound(new { error = "Documento no encontrado." });

        var blobPath = ExtractBlobPath(doc.BlobUrl);
        var (content, contentType) = await blobStorage.DownloadAsync(blobPath, ct);

        Response.Headers.Append("Content-Disposition", $"inline; filename=\"{doc.FileName}\"");
        return File(content, contentType);
    }

    /// <summary>
    /// Descarga el PDF como attachment.
    /// </summary>
    [HttpGet("{docId:guid}/download")]
    public async Task<IActionResult> Download(Guid agentId, Guid docId, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var doc = await db.AgentDocuments
            .FirstOrDefaultAsync(d => d.Id == docId && d.AgentDefinitionId == agentId && d.TenantId == tenantId, ct);

        if (doc is null)
            return NotFound(new { error = "Documento no encontrado." });

        var blobPath = ExtractBlobPath(doc.BlobUrl);
        var (content, contentType) = await blobStorage.DownloadAsync(blobPath, ct);

        return File(content, contentType, doc.FileName);
    }

    [HttpDelete("{docId:guid}")]
    public async Task<IActionResult> Delete(Guid agentId, Guid docId, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var doc = await db.AgentDocuments
            .FirstOrDefaultAsync(d => d.Id == docId && d.AgentDefinitionId == agentId && d.TenantId == tenantId, ct);

        if (doc is null)
            return NotFound(new { error = "Documento no encontrado." });

        var blobPath = ExtractBlobPath(doc.BlobUrl);
        await blobStorage.DeleteAsync(blobPath, ct);

        db.AgentDocuments.Remove(doc);
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
        // URL: https://account.blob.core.windows.net/container/tenant/agent/file.pdf
        const string marker = ".blob.core.windows.net/";
        var markerIdx = blobUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIdx < 0) return blobUrl;

        var afterHost = blobUrl[(markerIdx + marker.Length)..]; // container/tenant/agent/file.pdf
        var slashIdx = afterHost.IndexOf('/');
        if (slashIdx < 0) return afterHost;

        var blobPath = afterHost[(slashIdx + 1)..]; // tenant/agent/file.pdf
        return Uri.UnescapeDataString(blobPath);
    }
}
