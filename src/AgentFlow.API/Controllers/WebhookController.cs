using System.Text.Json;
using AgentFlow.Application.Modules.Webhooks;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhookController(IMediator mediator, ITenantContext tenantCtx, AgentFlowDbContext db) : ControllerBase
{
    /// <summary>
    /// Endpoint normalizado — usado por n8n o cualquier integrador que ya tenga
    /// el TenantId en el header X-Tenant-Id.
    /// </summary>
    [HttpPost("message")]
    public async Task<IActionResult> IncomingMessage(
        [FromBody] IncomingMessageDto dto, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        if (tenantId == Guid.Empty)
            return BadRequest(new { error = "TenantId requerido. Envía header X-Tenant-Id o JWT válido." });

        var result = await mediator.Send(new ProcessIncomingMessageCommand(
            tenantId,
            dto.From,
            dto.Body,
            Enum.Parse<ChannelType>(dto.Channel, true),
            dto.ClientName,
            dto.ExternalId
        ), ct);

        return Ok(new { result.ConversationId, result.ReplyText, result.AgentType });
    }

    /// <summary>
    /// Webhook DIRECTO de UltraMsg — endpoint público (sin autenticación).
    ///
    /// UltraMsg envía un POST con su propio formato cada vez que recibe un mensaje.
    /// Este endpoint:
    /// 1. Parsea el payload de UltraMsg (puede venir como JSON o form-data)
    /// 2. Identifica al tenant buscando la línea WhatsApp por instanceId
    /// 3. Ignora mensajes salientes (solo procesa entrantes)
    /// 4. Convierte al formato interno y procesa el mensaje
    ///
    /// URL a configurar en UltraMsg:
    /// https://tu-servidor/api/webhooks/ultramsg?instanceId=XXXXX&token=YYYYY
    /// </summary>
    [HttpPost("ultramsg")]
    public async Task<IActionResult> UltraMsgWebhook(
        [FromQuery] string? instanceId,
        [FromQuery] string? token,
        CancellationToken ct)
    {
        // ── 1. Leer el body como JSON ────────────────────
        // UltraMsg puede enviar como JSON o form-data, normalizamos
        string bodyContent;
        using (var reader = new StreamReader(Request.Body))
        {
            bodyContent = await reader.ReadToEndAsync(ct);
        }

        if (string.IsNullOrEmpty(bodyContent))
            return Ok(new { status = "ignored", reason = "empty body" });

        // ── 2. Parsear el payload de UltraMsg ────────────
        UltraMsgWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<UltraMsgWebhookPayload>(bodyContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return Ok(new { status = "ignored", reason = "invalid JSON" });
        }

        if (payload is null || string.IsNullOrEmpty(payload.Body))
            return Ok(new { status = "ignored", reason = "no body" });

        // ── 3. Ignorar mensajes salientes ────────────────
        // UltraMsg envía TODOS los mensajes (entrantes y salientes).
        // Solo procesamos los entrantes (fromMe = false).
        if (payload.FromMe == true)
            return Ok(new { status = "ignored", reason = "outbound message" });

        // ── 4. Identificar tenant por instanceId ─────────
        // Buscar la línea WhatsApp cuyo instanceId coincide
        var normalizedInstance = instanceId?.Trim() ?? payload.InstanceId ?? "";

        // Quitar prefijo "instance" si lo tiene, para comparar solo el número
        var instanceNumber = normalizedInstance
            .Replace("instance", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        var line = await db.WhatsAppLines
            .Include(l => l.Tenant)
            .Where(l => l.TenantId != null && l.IsActive)  // solo líneas con tenant y activas
            .FirstOrDefaultAsync(l =>
                l.InstanceId == instanceNumber
                || l.InstanceId == normalizedInstance
                || l.InstanceId == $"instance{instanceNumber}", ct);

        if (line?.TenantId is null)
            return Ok(new { status = "ignored", reason = "unknown instanceId" });

        // ── 5. Validar token (seguridad básica) ──────────
        if (!string.IsNullOrEmpty(token) && line.ApiToken != token)
        {
            // Si se envió token pero no coincide, podría ser spam
            return Unauthorized(new { error = "Token inválido." });
        }

        // ── 6. Extraer datos del mensaje ─────────────────
        // UltraMsg envía el remitente como "5076000XXXX@c.us"
        var fromPhone = ExtractPhoneFromJid(payload.From ?? "");
        if (string.IsNullOrEmpty(fromPhone))
            return Ok(new { status = "ignored", reason = "invalid sender" });

        var clientName = payload.PushName ?? payload.NotifyName;

        // ── 7. Procesar como mensaje entrante ────────────
        var result = await mediator.Send(new ProcessIncomingMessageCommand(
            line.TenantId.Value,
            fromPhone,
            payload.Body,
            ChannelType.WhatsApp,
            clientName,
            payload.Id
        ), ct);

        return Ok(new
        {
            status = "processed",
            conversationId = result.ConversationId,
            agentType = result.AgentType
        });
    }

    /// <summary>
    /// Verificación de webhook de Meta Cloud API (GET challenge).
    /// </summary>
    [HttpGet("meta/verify")]
    public IActionResult MetaVerify(
        [FromQuery(Name = "hub.mode")] string mode,
        [FromQuery(Name = "hub.challenge")] string challenge,
        [FromQuery(Name = "hub.verify_token")] string verifyToken,
        [FromServices] IConfiguration config)
    {
        var expected = config["Meta:VerifyToken"];
        if (mode == "subscribe" && verifyToken == expected)
            return Ok(int.Parse(challenge));
        return Forbid();
    }

    /// <summary>
    /// Convierte un JID de WhatsApp ("5076000XXXX@c.us") al formato E.164 ("+5076000XXXX").
    /// </summary>
    private static string ExtractPhoneFromJid(string jid)
    {
        // Formato: "5076000XXXX@c.us" o "5076000XXXX@s.whatsapp.net"
        var atIndex = jid.IndexOf('@');
        if (atIndex <= 0) return "";

        var digits = jid[..atIndex];
        return digits.StartsWith('+') ? digits : $"+{digits}";
    }
}

// ── DTOs ────────────────────────────────────────────

public record IncomingMessageDto(
    string From,
    string Body,
    string Channel,
    string? ClientName,
    string? ExternalId
);

/// <summary>
/// Payload que UltraMsg envía al webhook.
/// Documentación: https://docs.ultramsg.com/api/webhooks
///
/// UltraMsg envía campos como:
/// - id: ID del mensaje en WhatsApp
/// - from: remitente ("5076000XXXX@c.us")
/// - to: destinatario
/// - body: texto del mensaje
/// - fromMe: true si lo envió la instancia (saliente), false si lo recibió (entrante)
/// - pushName: nombre que el contacto tiene en WhatsApp
/// - type: "chat", "image", "video", etc.
/// </summary>
public record UltraMsgWebhookPayload
{
    public string? Id { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Body { get; init; }
    public bool? FromMe { get; init; }
    public string? PushName { get; init; }
    public string? NotifyName { get; init; }
    public string? Type { get; init; }
    public string? InstanceId { get; init; }
}
