using System.Text.Json;
using AgentFlow.Application.Modules.Webhooks;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Messaging;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Storage;
using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhookController(
    IMediator mediator,
    ITenantContext tenantCtx,
    AgentFlowDbContext db,
    IBlobStorageService blobStorage,
    IHttpClientFactory httpClientFactory,
    AgentFlow.Domain.Interfaces.ITranscriptionService transcriptionService,
    IMessageBufferStore messageBuffer,
    IBackgroundJobClient? jobClient = null) : ControllerBase
{
    /// <summary>
    /// Despacha un mensaje entrante. Si el tenant tiene MessageBufferSeconds > 0
    /// (debounce activo), el mensaje se agrega al buffer Redis y se programa un
    /// job Hangfire que procesa el buffer cuando el cliente deja de escribir.
    /// Si es 0, se procesa inmediatamente vía mediator (comportamiento anterior).
    /// </summary>
    private async Task<(bool Buffered, Guid? ConversationId, string? ReplyText, string? AgentType)> DispatchAsync(
        Guid tenantId, string fromPhone, string message, ChannelType channel,
        string? clientName, string? externalId, string? mediaUrl, string? mediaType,
        CancellationToken ct)
    {
        int bufferSec = 0;
        try
        {
            bufferSec = await db.Tenants
                .Where(t => t.Id == tenantId)
                .Select(t => (int?)t.MessageBufferSeconds)
                .FirstOrDefaultAsync(ct) ?? 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Dispatch] No pude leer MessageBufferSeconds, fallback a directo: {ex.Message}");
        }

        // Intento de buffer con debounce. Si Redis o Hangfire fallan, hacemos fallback
        // automático al flujo directo (mediator.Send) — el cliente no debe perder el
        // mensaje por una caída del buffer.
        if (bufferSec > 0 && jobClient is not null)
        {
            try
            {
                var buffered = new BufferedMessage(
                    Content: message ?? string.Empty,
                    Channel: channel.ToString(),
                    ClientName: clientName,
                    ExternalMessageId: externalId,
                    MediaUrl: mediaUrl,
                    MediaType: mediaType,
                    TimestampTicks: DateTime.UtcNow.Ticks);

                await messageBuffer.AppendAsync(tenantId, fromPhone, buffered, ct);
                jobClient.Schedule<MessageBufferFlushJob>(
                    svc => svc.RunAsync(tenantId, fromPhone, CancellationToken.None),
                    TimeSpan.FromSeconds(bufferSec));
                return (true, null, null, null);
            }
            catch (Exception ex)
            {
                // Buffer caído (Redis o Hangfire) — proceso directo para no perder el mensaje.
                Console.WriteLine($"[Dispatch] Buffer falló ({ex.GetType().Name}: {ex.Message}). Fallback a flujo directo.");
            }
        }

        var result = await mediator.Send(new ProcessIncomingMessageCommand(
            tenantId, fromPhone, message ?? string.Empty, channel, clientName, externalId, mediaUrl, mediaType), ct);
        return (false, result.ConversationId, result.ReplyText, result.AgentType);
    }

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

        var channel = Enum.Parse<ChannelType>(dto.Channel, true);
        var dispatch = await DispatchAsync(
            tenantId, dto.From, dto.Body, channel,
            dto.ClientName, dto.ExternalId,
            mediaUrl: null, mediaType: null, ct);

        if (dispatch.Buffered)
            return Accepted(new { buffered = true, message = "Mensaje agregado al buffer — se procesará tras el debounce." });

        return Ok(new { dispatch.ConversationId, dispatch.ReplyText, dispatch.AgentType });
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
        // ── 1. Leer el body ──────────────────────────────
        string bodyContent;
        using (var reader = new StreamReader(Request.Body))
            bodyContent = await reader.ReadToEndAsync(ct);

        // Registrar en bitácora — siempre, aunque falle el procesamiento
        var log = new AgentFlow.Domain.Entities.WebhookLog
        {
            Provider = "ultramsg",
            InstanceId = instanceId,
            RawPayload = bodyContent?.Length > 4000 ? bodyContent[..4000] : bodyContent,
            ReceivedAt = DateTime.UtcNow,
            Status = "received"
        };

        if (string.IsNullOrEmpty(bodyContent))
        {
            log.Status = "ignored"; log.StatusReason = "empty body";
            db.WebhookLogs.Add(log); await db.SaveChangesAsync(ct);
            return Ok(new { status = "ignored", reason = "empty body" });
        }

        // ── 2. Parsear payload ───────────────────────────
        // UltraMsg envía los datos del mensaje dentro de un objeto "data":
        // { "data": { "id":"...", "from":"...", "body":"...", ... },
        //   "instanceId": "instance140984", "token": "..." }
        //
        // Algunos entornos envían los datos como application/x-www-form-urlencoded.
        // En ese caso intentamos extraer el campo "data" del form body.
        UltraMsgWebhookPayload? payload;
        string? wrapperInstanceId = null;
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Intentar parsear el bodyContent directamente como JSON
            string jsonToParse = bodyContent;

            // Si el body parece form-encoded (contiene '=' y no empieza con '{'),
            // intentar extraer el campo "data" del form body.
            if (!bodyContent.TrimStart().StartsWith('{') && bodyContent.Contains('='))
            {
                // Parsear como form-encoded: data={"id":"...",...}&token=xxx
                var formParts = System.Web.HttpUtility.ParseQueryString(bodyContent);
                var dataField  = formParts["data"];
                var tokenField = formParts["token"];
                var instField  = formParts["instanceId"] ?? formParts["instance_id"];
                if (!string.IsNullOrEmpty(dataField))
                {
                    jsonToParse = $"{{\"data\":{dataField},\"instanceId\":\"{instField}\",\"token\":\"{tokenField}\"}}";
                }
                // Si no hay campo "data", el body entero podría ser la data directamente
                else
                {
                    jsonToParse = bodyContent; // intentar parsear tal cual
                }
            }

            var wrapper = JsonSerializer.Deserialize<UltraMsgWebhookWrapper>(jsonToParse, opts);

            if (wrapper?.Data is not null)
            {
                // Formato wrapeado: { data: {...}, instanceId: "instance140984" }
                payload = wrapper.Data;
                wrapperInstanceId = wrapper.InstanceId;
            }
            else
            {
                // Fallback: intentar deserializar directamente (algunos webhooks pueden enviarlo sin wrapper)
                payload = JsonSerializer.Deserialize<UltraMsgWebhookPayload>(jsonToParse, opts);
            }
        }
        catch (Exception ex)
        {
            log.Status = "error"; log.StatusReason = $"parse error: {ex.Message}";
            db.WebhookLogs.Add(log); await db.SaveChangesAsync(ct);
            return Ok(new { status = "ignored", reason = "invalid JSON", detail = ex.Message });
        }

        if (payload is null)
        {
            log.Status = "ignored"; log.StatusReason = "null payload";
            db.WebhookLogs.Add(log); await db.SaveChangesAsync(ct);
            return Ok(new { status = "ignored", reason = "null payload" });
        }

        // Enriquecer bitácora con datos del payload
        log.FromPhone = payload.From;
        log.MessageType = payload.Type;
        log.Body = payload.Body?.Length > 500 ? payload.Body[..500] : payload.Body;
        log.ExternalMessageId = payload.Id;

        // ── 3. Ignorar mensajes salientes ────────────────
        if (payload.FromMe == true)
        {
            log.Status = "ignored"; log.StatusReason = "outbound";
            db.WebhookLogs.Add(log); await db.SaveChangesAsync(ct);
            return Ok(new { status = "ignored", reason = "outbound message" });
        }

        // ── 4. Ignorar tipos no soportados ───────────────
        var msgType = payload.Type?.ToLower() ?? "chat";
        var supportedTypes = new[] { "chat", "ptt", "audio", "image", "document" };
        if (!supportedTypes.Contains(msgType))
        {
            log.Status = "ignored"; log.StatusReason = $"unsupported type: {msgType}";
            db.WebhookLogs.Add(log); await db.SaveChangesAsync(ct);
            return Ok(new { status = "ignored", reason = $"unsupported type: {msgType}" });
        }

        // ── 5. Identificar tenant por instanceId ─────────
        // Prioridad: query param > wrapper instanceId > campo en el payload
        var normalizedInstance = instanceId?.Trim()
            ?? wrapperInstanceId?.Trim()
            ?? payload.InstanceId
            ?? "";
        var instanceNumber = normalizedInstance
            .Replace("instance", "", StringComparison.OrdinalIgnoreCase).Trim();

        var line = await db.WhatsAppLines
            .Include(l => l.Tenant)
            .Where(l => l.TenantId != null && l.IsActive)
            .FirstOrDefaultAsync(l =>
                l.InstanceId == instanceNumber
                || l.InstanceId == normalizedInstance
                || l.InstanceId == $"instance{instanceNumber}", ct);

        if (line?.TenantId is null)
        {
            log.Status = "ignored"; log.StatusReason = $"unknown instanceId: {normalizedInstance}";
            db.WebhookLogs.Add(log); await db.SaveChangesAsync(ct);
            return Ok(new { status = "ignored", reason = "unknown instanceId", tried = normalizedInstance });
        }

        log.TenantId = line.TenantId;

        // ── 6. Validar token ─────────────────────────────
        if (!string.IsNullOrEmpty(token) && line.ApiToken != token)
        {
            log.Status = "ignored"; log.StatusReason = "invalid token";
            db.WebhookLogs.Add(log); await db.SaveChangesAsync(ct);
            return Unauthorized(new { error = "Token inválido." });
        }

        // ── 7. Anti-duplicados por ID de mensaje ─────────
        if (!string.IsNullOrEmpty(payload.Id))
        {
            var alreadyProcessed = await db.Messages
                .AnyAsync(m => m.ExternalMessageId == payload.Id, ct);
            if (alreadyProcessed)
            {
                log.Status = "ignored"; log.StatusReason = "duplicate";
                db.WebhookLogs.Add(log); await db.SaveChangesAsync(ct);
                return Ok(new { status = "ignored", reason = "duplicate" });
            }
        }

        // ── 8. Extraer remitente ──────────────────────────
        var fromPhone = ExtractPhoneFromJid(payload.From ?? "");
        if (string.IsNullOrEmpty(fromPhone))
        {
            log.Status = "ignored"; log.StatusReason = $"invalid sender: {payload.From}";
            db.WebhookLogs.Add(log); await db.SaveChangesAsync(ct);
            return Ok(new { status = "ignored", reason = "invalid sender" });
        }

        var clientName = payload.Pushname ?? payload.NotifyName;

        // ── 9. Normalizar contenido según tipo ───────────
        // Para media (imagen, documento, audio), body contiene la URL del archivo.
        // Construimos un texto descriptivo para el agente y pasamos la URL como MediaUrl.
        string messageText;
        string? mediaUrl = null;
        string? mediaType = null;

        switch (msgType)
        {
            case "image":
                // En UltraMsg, la URL del archivo está SIEMPRE en "media".
                // "body" contiene el caption/texto que el usuario escribió junto a la imagen.
                mediaUrl  = payload.Media;
                mediaType = "image";
                // Usar el texto del caption como messageText para que el agente reciba
                // la pregunta real del cliente y pueda responder en contexto.
                var imgCaptionText = !string.IsNullOrWhiteSpace(payload.Body) ? payload.Body
                    : !string.IsNullOrWhiteSpace(payload.Caption) ? payload.Caption
                    : "";
                messageText = string.IsNullOrWhiteSpace(imgCaptionText)
                    ? "📷 [Imagen]"
                    : imgCaptionText;
                break;

            case "document":
                // URL del archivo siempre en "media"; caption del usuario en "body" o "caption"
                mediaUrl  = payload.Media;
                mediaType = "document";
                var docCaptionText = !string.IsNullOrWhiteSpace(payload.Body) ? payload.Body
                    : !string.IsNullOrWhiteSpace(payload.Caption) ? payload.Caption
                    : "";
                // Usar el caption directo — sin corchetes ni emojis extra
                messageText = string.IsNullOrWhiteSpace(docCaptionText)
                    ? "📄 [Documento]"
                    : docCaptionText;
                break;

            case "ptt":
            case "audio":
                // UltraMsg envía la URL del audio en el campo "media", NO en "body"
                mediaUrl  = payload.Media ?? payload.Body;
                mediaType = "audio";
                messageText = "🎤 [Nota de voz — transcripción en proceso]";
                break;

            default: // chat
                if (string.IsNullOrEmpty(payload.Body))
                    return Ok(new { status = "ignored", reason = "empty text message" });
                messageText = payload.Body;
                break;
        }

        // ── 10. Descargar media y subir a Azure ──────────
        byte[]? capturedAudioBytes = null;
        bool audioDownloadFailed = false;
        if (!string.IsNullOrEmpty(mediaUrl))
        {
            // Paso A: descargar el archivo (separado del upload a Azure)
            try
            {
                var httpClient = httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                var mediaBytes = await httpClient.GetByteArrayAsync(mediaUrl, ct);

                if (msgType is "ptt" or "audio")
                    capturedAudioBytes = mediaBytes;

                // Paso B: subir a Azure Blob (si falla, se usa la URL original de UltraMsg)
                try
                {
                    var ext = msgType switch
                    {
                        "image"    => DetectImageExtension(payload.Mimetype),
                        "document" => DetectDocExtension(payload.Mimetype, payload.Caption, payload.Filename),
                        "audio"    => "ogg",
                        "ptt"      => "ogg",
                        _          => "bin"
                    };
                    var contentType = payload.Mimetype ?? (msgType == "image" ? "image/jpeg" : "application/octet-stream");
                    var blobName    = $"conversations/{line.TenantId.Value}/{DateTime.UtcNow:yyyyMMdd}/{payload.Id ?? Guid.NewGuid().ToString()}.{ext}";
                    var azureUrl    = await blobStorage.UploadWhatsAppMediaAsync(blobName, mediaBytes, contentType, ct);
                    mediaUrl = azureUrl;
                }
                catch (Exception azEx)
                {
                    Console.WriteLine($"[Azure] No se pudo subir media: {azEx.Message}. Usando URL original.");
                }
            }
            catch (Exception dlEx)
            {
                Console.WriteLine($"[Media] No se pudo descargar el archivo desde UltraMsg: {dlEx.Message}");
                if (msgType is "ptt" or "audio")
                    audioDownloadFailed = true;
            }
        }

        // ── 10b. Transcribir nota de voz con Whisper ─────
        if (msgType is "ptt" or "audio")
        {
            if (audioDownloadFailed || capturedAudioBytes is null)
            {
                // No se pudo obtener el audio — pedir al cliente que escriba
                messageText = "🎤 [Nota de voz recibida] No pude procesar el audio. ¿Puedes escribir tu mensaje?";
                Console.WriteLine($"[Whisper] Audio no disponible para {fromPhone} — se usó fallback.");
            }
            else
            {
                var audioFileName = $"{payload.Id ?? Guid.NewGuid().ToString()}.ogg";
                var transcription = await transcriptionService.TranscribeAsync(capturedAudioBytes, audioFileName, ct);
                if (!string.IsNullOrWhiteSpace(transcription))
                {
                    messageText = transcription;
                    Console.WriteLine($"[Whisper] Audio transcripto ({capturedAudioBytes.Length / 1024}KB): \"{transcription}\"");
                }
                else
                {
                    // Whisper no disponible o falló — pedir al cliente que escriba
                    messageText = "🎤 [Nota de voz recibida] No pude transcribir el audio. ¿Puedes escribir tu mensaje?";
                    Console.WriteLine($"[Whisper] Transcripción fallida para {fromPhone} — se usó fallback.");
                }
            }
        }

        // ── 11. Procesar mensaje ──────────────────────────
        try
        {
            var dispatch = await DispatchAsync(
                line.TenantId.Value, fromPhone, messageText, ChannelType.WhatsApp,
                clientName, payload.Id, mediaUrl, mediaType, ct);

            // Actualizar bitácora — si fue bufferizado marcamos como 'buffered'
            log.Status = dispatch.Buffered ? "buffered" : "processed";
            log.StatusReason = dispatch.Buffered ? "debounce-active" : dispatch.AgentType;
            db.WebhookLogs.Add(log);
            await db.SaveChangesAsync(ct);

            return Ok(new
            {
                status = log.Status,
                conversationId = dispatch.ConversationId,
                agentType = dispatch.AgentType,
                buffered = dispatch.Buffered
            });
        }
        catch (Exception ex)
        {
            // Capturar error para diagnóstico
            var errorDetail = ex.Message + " | " + (ex.InnerException?.Message ?? "") + " | " + ex.StackTrace;
            log.Status = "error";
            log.StatusReason = errorDetail.Length > 2000 ? errorDetail.Substring(0, 2000) : errorDetail;
            db.WebhookLogs.Add(log);
            try { await db.SaveChangesAsync(ct); } catch { }
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Endpoint de diagnóstico — muestra los últimos 50 registros de la bitácora del webhook.
    /// Solo para uso interno. No requiere autenticación para facilitar el diagnóstico.
    /// </summary>
    [HttpGet("ultramsg/logs")]
    public async Task<IActionResult> WebhookLogs(
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var logs = await db.WebhookLogs
            .OrderByDescending(l => l.ReceivedAt)
            .Take(Math.Min(take, 200))
            .Select(l => new
            {
                l.Id,
                l.ReceivedAt,
                l.Provider,
                l.InstanceId,
                l.TenantId,
                l.FromPhone,
                l.MessageType,
                l.Body,
                l.ExternalMessageId,
                l.Status,
                l.StatusReason,
                RawPayload = l.RawPayload  // incluir el payload completo para diagnóstico
            })
            .ToListAsync(ct);

        return Ok(new { count = logs.Count, logs });
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
        var atIndex = jid.IndexOf('@');
        if (atIndex <= 0) return "";
        var digits = jid[..atIndex];
        return digits.StartsWith('+') ? digits : $"+{digits}";
    }

    private static string DetectImageExtension(string? mimeType) => mimeType switch
    {
        "image/png"  => "png",
        "image/gif"  => "gif",
        "image/webp" => "webp",
        _            => "jpg"
    };

    private static string DetectDocExtension(string? mimeType, string? caption, string? filename = null)
    {
        if (mimeType == "application/pdf") return "pdf";
        if (filename?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true) return "pdf";
        if (caption?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true) return "pdf";
        if (mimeType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document") return "docx";
        if (mimeType == "application/msword") return "doc";
        return "bin";
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
/// Wrapper externo que UltraMsg envía al webhook.
/// Formato: { "data": { ...mensaje... }, "instanceId": "instance140984", "token": "...", "event_type": "..." }
/// </summary>
public class UltraMsgWebhookWrapper
{
    public UltraMsgWebhookPayload? Data { get; set; }
    public string? InstanceId { get; set; }
    public string? Token { get; set; }
    public string? EventType { get; set; }
}

/// <summary>
/// Payload del mensaje dentro del objeto "data" de UltraMsg.
/// Documentación: https://docs.ultramsg.com/api/webhooks
/// </summary>
public class UltraMsgWebhookPayload
{
    public string? Id { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Body { get; set; }          // texto o URL del media
    public bool? FromMe { get; set; }

    // UltraMsg envía "pushname" (lowercase). Solo un campo para evitar colisión con PropertyNameCaseInsensitive.
    [System.Text.Json.Serialization.JsonPropertyName("pushname")]
    public string? Pushname { get; set; }

    public string? NotifyName { get; set; }
    public string? Type { get; set; }          // chat | ptt | audio | image | document | video
    public string? InstanceId { get; set; }
    public string? Caption { get; set; }       // texto adjunto a imagen/documento
    public string? Mimetype { get; set; }      // ej: "image/jpeg", "application/pdf"
    public string? Media { get; set; }         // URL del archivo en algunos formatos de UltraMsg
    public string? Filename { get; set; }      // nombre original del archivo (ej: "factura.pdf")
}
