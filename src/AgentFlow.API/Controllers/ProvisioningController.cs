using System.Text.Json;
using AgentFlow.Domain.Provisioning;
using AgentFlow.Infrastructure.Provisioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.API.Controllers;

/// <summary>
/// Integración Ludo CRM — Fase 2 (Dirección A, inbound). Endpoint que Ludo invoca para
/// aprovisionar un tenant en TalkIA.
///
/// <para>Autenticación: <b>HMAC-SHA256</b> sobre <c>"{timestamp}.{rawBody}"</c> con el secreto
/// compartido <c>Ludo:WebhookSecret</c> (lo define y provee Ludo; se inyecta en el deploy).
/// No usa JWT — es server-to-server. Sin secreto configurado el endpoint responde 503 y NO
/// aprovisiona (no se crea ningún tenant sin firma verificada).</para>
///
/// <para>Es completamente aditivo: la ruta es nueva, anónima por defecto (no hay fallback
/// policy global), y no afecta ningún flujo existente.</para>
/// </summary>
[ApiController]
[Route("api/provisioning")]
[AllowAnonymous]
public class ProvisioningController(
    ITenantProvisioningService provisioning,
    IConfiguration config,
    ILogger<ProvisioningController> log) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [HttpPost("tenant")]
    public async Task<IActionResult> ProvisionTenant(CancellationToken ct)
    {
        // 1. Leer el body CRUDO (la firma se calcula sobre estos bytes exactos).
        string rawBody;
        using (var reader = new StreamReader(Request.Body))
            rawBody = await reader.ReadToEndAsync(ct);

        // 2. Validar firma HMAC + anti-replay.
        var secret = config["Ludo:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            log.LogWarning("Provisioning rechazado: Ludo:WebhookSecret no configurado.");
            return StatusCode(503, new { error = "Integración Ludo no configurada (falta secreto)." });
        }

        var signature = Request.Headers["X-Ludo-Signature"].FirstOrDefault();
        var timestamp = Request.Headers["X-Ludo-Timestamp"].FirstOrDefault();
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var (ok, reason) = LudoWebhookSignature.Validate(rawBody, signature, timestamp, secret, nowUnix);
        if (!ok)
        {
            log.LogWarning("Provisioning rechazado: firma inválida ({Reason}).", reason);
            return Unauthorized(new { error = $"Firma inválida: {reason}" });
        }

        // 3. Deserializar.
        ProvisionTenantRequest? req;
        try
        {
            req = JsonSerializer.Deserialize<ProvisionTenantRequest>(rawBody, JsonOpts);
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = $"Payload inválido: {ex.Message}" });
        }
        if (req is null || string.IsNullOrWhiteSpace(req.LudoTenantId) || string.IsNullOrWhiteSpace(req.NombreNegocio))
            return BadRequest(new { error = "Faltan campos requeridos (ludoTenantId, nombreNegocio)." });

        // 4. Aprovisionar (idempotente + transaccional).
        try
        {
            var result = await provisioning.ProvisionAsync(req, ct);
            var body = new
            {
                tenantId = result.TenantId,
                alreadyExisted = result.AlreadyExisted,
                masters = result.Masters.Select(m => new { m.AgentSlug, m.TemplateId, m.Name }),
            };
            // Reenvío idempotente → 200; alta nueva → 201.
            return result.AlreadyExisted ? Ok(body) : StatusCode(201, body);
        }
        catch (ProvisioningValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
