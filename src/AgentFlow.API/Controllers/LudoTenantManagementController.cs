using System.Text.Json;
using AgentFlow.Domain.Provisioning;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Provisioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

/// <summary>
/// API de gestión de maestros para LUDO CRM (capa de partner sobre el servicio genérico
/// <see cref="ITenantMasterManagementService"/>). Permite que Ludo, después del alta,
/// administre los maestros de SUS tenants: prompt (regenerar por objetivo o literal),
/// documentos de referencia (PDF/RAG), horarios y activación.
///
/// <para>Seguridad: misma del provisioning — HMAC-SHA256 sobre <c>"{timestamp}.{rawBody}"</c>
/// con <c>Ludo:WebhookSecret</c> + anti-replay 300s (headers X-Ludo-Signature /
/// X-Ludo-Timestamp; en GET/DELETE el body firmado es cadena vacía). El
/// <c>ludoTenantId</c> de la ruta se resuelve vía LudoTenantMap: Ludo SOLO puede tocar
/// tenants que él mismo aprovisionó.</para>
///
/// <para>Para otro partner futuro: replicar este controller (~30 líneas de plomería) con
/// su propio secreto y tabla de mapeo — el servicio de negocio es el mismo.</para>
/// </summary>
[ApiController]
[Route("api/ludo/tenant/{ludoTenantId}")]
[AllowAnonymous]
public class LudoTenantManagementController(
    ITenantMasterManagementService mgmt,
    AgentFlowDbContext db,
    IConfiguration config,
    ILogger<LudoTenantManagementController> log) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [HttpGet("masters")]
    public async Task<IActionResult> GetMasters(string ludoTenantId, CancellationToken ct)
    {
        var (error, tenantId) = await AuthorizeAsync(ludoTenantId, rawBody: string.Empty, ct);
        if (error is not null) return error;

        var masters = await mgmt.GetMastersAsync(tenantId, ct);
        return Ok(new { ludoTenantId, masters });
    }

    [HttpPut("master")]
    public async Task<IActionResult> UpdateMaster(string ludoTenantId, CancellationToken ct)
    {
        var rawBody = await ReadBodyAsync(ct);
        var (error, tenantId) = await AuthorizeAsync(ludoTenantId, rawBody, ct);
        if (error is not null) return error;

        var req = Deserialize<UpdateMasterRequest>(rawBody, out var badRequest);
        if (req is null) return badRequest!;

        var result = await mgmt.UpdateMasterAsync(tenantId, req, ct);
        return ToHttp(result);
    }

    [HttpPost("master/documents")]
    [RequestSizeLimit(30L * 1024 * 1024)] // base64 de un PDF de hasta ~20 MB
    public async Task<IActionResult> AddDocument(string ludoTenantId, CancellationToken ct)
    {
        var rawBody = await ReadBodyAsync(ct);
        var (error, tenantId) = await AuthorizeAsync(ludoTenantId, rawBody, ct);
        if (error is not null) return error;

        var req = Deserialize<UploadDocumentRequest>(rawBody, out var badRequest);
        if (req is null) return badRequest!;

        var result = await mgmt.AddDocumentAsync(tenantId, req, ct);
        return ToHttp(result, createdOnOk: true);
    }

    [HttpGet("master/{agentSlug}/documents")]
    public async Task<IActionResult> ListDocuments(string ludoTenantId, string agentSlug, CancellationToken ct)
    {
        var (error, tenantId) = await AuthorizeAsync(ludoTenantId, rawBody: string.Empty, ct);
        if (error is not null) return error;

        var docs = await mgmt.ListDocumentsAsync(tenantId, agentSlug, ct);
        if (docs is null)
            return BadRequest(new { ok = false, message = $"No existe un maestro para el agente '{agentSlug}' en este tenant." });
        return Ok(new { ludoTenantId, agentSlug, documents = docs });
    }

    [HttpDelete("master/{agentSlug}/documents/{documentId:guid}")]
    public async Task<IActionResult> RemoveDocument(
        string ludoTenantId, string agentSlug, Guid documentId, CancellationToken ct)
    {
        var (error, tenantId) = await AuthorizeAsync(ludoTenantId, rawBody: string.Empty, ct);
        if (error is not null) return error;

        var result = await mgmt.RemoveDocumentAsync(tenantId, agentSlug, documentId, ct);
        return ToHttp(result);
    }

    [HttpPut("horarios")]
    public async Task<IActionResult> UpdateHours(string ludoTenantId, CancellationToken ct)
    {
        var rawBody = await ReadBodyAsync(ct);
        var (error, tenantId) = await AuthorizeAsync(ludoTenantId, rawBody, ct);
        if (error is not null) return error;

        var req = Deserialize<UpdateTenantHoursRequest>(rawBody, out var badRequest);
        if (req is null) return badRequest!;

        var result = await mgmt.UpdateTenantHoursAsync(tenantId, req, ct);
        return ToHttp(result);
    }

    // ── Plomería del partner ──────────────────────────────────────────────────────

    private async Task<string> ReadBodyAsync(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>Valida HMAC + anti-replay y resuelve ludoTenantId → TenantId.</summary>
    private async Task<(IActionResult? Error, Guid TenantId)> AuthorizeAsync(
        string ludoTenantId, string rawBody, CancellationToken ct)
    {
        var secret = config["Ludo:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            return (StatusCode(503, new { error = "Integración Ludo no configurada (falta secreto)." }), Guid.Empty);

        var signature = Request.Headers["X-Ludo-Signature"].FirstOrDefault();
        var timestamp = Request.Headers["X-Ludo-Timestamp"].FirstOrDefault();
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var (ok, reason) = LudoWebhookSignature.Validate(rawBody, signature, timestamp, secret, nowUnix);
        if (!ok)
        {
            log.LogWarning("[LudoMgmt] Firma inválida ({Reason}) para ludoTenantId={LudoTenantId}.", reason, ludoTenantId);
            return (Unauthorized(new { error = $"Firma inválida: {reason}" }), Guid.Empty);
        }

        var tenantId = await db.LudoTenantMaps
            .Where(m => m.LudoTenantId == ludoTenantId)
            .Select(m => (Guid?)m.TenantId)
            .FirstOrDefaultAsync(ct);
        if (tenantId is null)
            return (NotFound(new { error = $"ludoTenantId '{ludoTenantId}' no está aprovisionado en TalkIA." }), Guid.Empty);

        return (null, tenantId.Value);
    }

    private static T? Deserialize<T>(string rawBody, out IActionResult? badRequest) where T : class
    {
        badRequest = null;
        try
        {
            var obj = JsonSerializer.Deserialize<T>(rawBody, JsonOpts);
            if (obj is null) badRequest = new BadRequestObjectResult(new { error = "Body vacío." });
            return obj;
        }
        catch (JsonException ex)
        {
            badRequest = new BadRequestObjectResult(new { error = $"Payload inválido: {ex.Message}" });
            return null;
        }
    }

    private static IActionResult ToHttp(MasterManagementResult result, bool createdOnOk = false)
    {
        var body = new
        {
            ok = result.Ok,
            message = result.Message,
            templateId = result.TemplateId,
            isActive = result.IsActive,
            isPrimaryForAgent = result.IsPrimary,
            documentId = result.DocumentId,
        };
        if (!result.Ok) return new BadRequestObjectResult(body);
        return createdOnOk ? new ObjectResult(body) { StatusCode = 201 } : new OkObjectResult(body);
    }
}
