using System.Text.Json;
using AgentFlow.Application.Modules.Webhooks;
using AgentFlow.Domain.Entities;
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
    AgentFlow.Infrastructure.Messaging.InProcessMessageDebouncer debouncer,
    AgentFlow.Domain.Interfaces.IInboundMessageQueue inboundQueue,
    IConfiguration configuration,
    IBackgroundJobClient? jobClient = null) : ControllerBase
{
    // Flag de transición. Default true para no romper deploys que aún no
    // tengan el Worker on-prem actualizado con el dispatcher. Cuando el
    // Worker esté operando, poner Inbox:UseInProcessDebouncer=false en
    // appsettings y solo la cola SQL será la fuente de procesamiento.
    private bool DebouncerEnabled =>
        configuration.GetValue("Inbox:UseInProcessDebouncer", true);
    /// <summary>
    /// Despacha un mensaje entrante. Si el tenant tiene MessageBufferSeconds > 0
    /// (debounce activo), el mensaje se agrega al buffer Redis y se programa un
    /// job Hangfire que procesa el buffer cuando el cliente deja de escribir.
    /// Si es 0, se procesa inmediatamente vía mediator (comportamiento anterior).
    /// </summary>
    private async Task<(bool Buffered, Guid? ConversationId, string? ReplyText, string? AgentType, Guid? QueueItemId)> DispatchAsync(
        Guid tenantId, string fromPhone, string message, ChannelType channel,
        string? clientName, string? externalId, string? mediaUrl, string? mediaType,
        CancellationToken ct,
        Guid? whatsAppLineId = null)
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

        // Doble-escritura (Día 1): persistimos en la cola durable antes de
        // tocar el debouncer. Aunque el AppPool muera, la fila Pending queda
        // en BD y el watchdog/dispatcher del Worker la rescata.
        // Tolerante a fallos: si el upsert falla NO bloqueamos el flujo —
        // el debouncer seguía funcionando antes sin la cola, y mantenemos
        // ese comportamiento mientras estabilizamos.
        Guid? queueItemId = null;
        try
        {
            queueItemId = await inboundQueue.UpsertAsync(new AgentFlow.Domain.Interfaces.InboundMessageUpsertRequest(
                TenantId: tenantId,
                FromPhone: fromPhone,
                Channel: channel.ToString(),
                WhatsAppLineId: whatsAppLineId,
                MessageContent: message ?? string.Empty,
                ClientName: clientName,
                ExternalMessageId: externalId,
                MediaUrl: mediaUrl,
                MediaType: mediaType,
                BufferSeconds: bufferSec > 0 ? bufferSec : 12), ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Dispatch] InboundQueue.UpsertAsync falló (no bloqueante): {ex.Message}");
        }

        // Si el debouncer está apagado por flag (Inbox:UseInProcessDebouncer=false),
        // delegamos COMPLETAMENTE al Worker on-prem vía la cola SQL. El cliente
        // recibe respuesta cuando el InboundMessageDispatcher reclame el item
        // (5-15s) o el watchdog lo escale (≤ 2 min). No tocamos al debouncer.
        if (bufferSec > 0 && !DebouncerEnabled)
        {
            return (true, null, null, null, queueItemId);
        }

        // Debounce in-process — agrupa mensajes ráfaga del mismo (tenant, phone).
        // Reset real del timer en cada mensaje nuevo. Sin dependencia de
        // Hangfire/Redis (más confiable en hosting compartido como Smartasp).
        // Por defecto sigue activo como acelerador del hot-path; cuando se
        // active el flag de apagado, esta rama no se ejecuta.
        if (bufferSec > 0)
        {
            try
            {
                debouncer.Enqueue(
                    tenantId, fromPhone, channel,
                    message ?? string.Empty, clientName, externalId,
                    mediaUrl, mediaType, bufferSec,
                    whatsAppLineId,
                    queueItemId);
                return (true, null, null, null, queueItemId);
            }
            catch (Exception ex)
            {
                // Falla absoluta del debouncer (no debería pasar, in-process) — fallback directo.
                Console.WriteLine($"[Dispatch] Debouncer falló ({ex.GetType().Name}: {ex.Message}). Fallback a flujo directo.");
            }
        }

        try
        {
            var result = await mediator.Send(new ProcessIncomingMessageCommand(
                tenantId, fromPhone, message ?? string.Empty, channel, clientName, externalId,
                mediaUrl, mediaType, whatsAppLineId), ct);
            if (queueItemId.HasValue)
            {
                try { await inboundQueue.MarkRepliedAsync(queueItemId.Value, outboundMessageId: null, ct); }
                catch (Exception qex) { Console.WriteLine($"[Dispatch] MarkReplied falló: {qex.Message}"); }
            }
            return (false, result.ConversationId, result.ReplyText, result.AgentType, queueItemId);
        }
        catch (Exception ex)
        {
            if (queueItemId.HasValue)
            {
                try { await inboundQueue.MarkFailedAsync(queueItemId.Value, "direct-dispatch", ex.GetType().Name + ": " + ex.Message, maxAttempts: 3, ct); }
                catch (Exception qex) { Console.WriteLine($"[Dispatch] MarkFailed falló: {qex.Message}"); }
            }
            throw;
        }
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

            // ── Branch por event_type ────────────────────────────────────────
            // UltraMsg empuja varios tipos de eventos a la MISMA URL. Distinguimos
            // por wrapper.EventType ANTES de procesar como mensaje entrante.
            //
            //   message_received   → mensaje entrante de cliente (flujo normal)
            //   message_create     → confirmación de saliente que nosotros mismos
            //                        creamos. Lo ignoramos para no duplicar.
            //   message_ack        → cambio de delivery status (sent/delivered/
            //                        read/invalid). Lo procesamos aparte para
            //                        actualizar Message.DeliveryStatus y
            //                        CampaignContact.DeliveryStatus.
            //   message_reaction   → reacción con emoji (no usado).
            //
            // Si EventType no está presente (compat con webhook viejo), el
            // flujo cae al procesamiento normal de mensaje entrante.
            if (string.Equals(wrapper?.EventType, "message_ack", StringComparison.OrdinalIgnoreCase))
            {
                // Resolver tenant ANTES de tocar Messages/CampaignContacts. Los IDs
                // de UltraMsg son auto-increment POR INSTANCIA — pueden colisionar
                // entre tenants. Sin scope por TenantId, un ACK de la cuenta A
                // podría actualizar un mensaje de la cuenta B con el mismo ID.
                var ackInstance = (instanceId ?? wrapper?.InstanceId ?? "").Trim();
                var resolvedTenantId = await ResolveTenantFromInstanceAsync(ackInstance, ct);

                log.Status = "ack";
                log.StatusReason = $"ack={wrapper?.Data?.Ack} status={wrapper?.Data?.Status} instance={ackInstance} tenant={resolvedTenantId}";
                log.TenantId = resolvedTenantId;
                db.WebhookLogs.Add(log);

                if (resolvedTenantId is null)
                {
                    // Sin tenant resuelto, no podemos saber a qué Message corresponde.
                    // Lo registramos para diagnóstico pero no tocamos BD (mejor que pisar
                    // un mensaje equivocado).
                    await db.SaveChangesAsync(ct);
                    return Ok(new { status = "ignored", reason = "unknown instanceId for ack", tried = ackInstance });
                }

                await HandleMessageAckAsync(wrapper!, resolvedTenantId.Value, ct);
                await db.SaveChangesAsync(ct);
                return Ok(new { status = "ack-processed", id = wrapper?.Data?.Id, deliveryStatus = wrapper?.Data?.Status, tenant = resolvedTenantId });
            }
            if (string.Equals(wrapper?.EventType, "message_create", StringComparison.OrdinalIgnoreCase)
                && wrapper?.Data?.FromMe == true)
            {
                // El propio saliente que creamos nosotros: UltraMsg lo eco. Ignorar.
                log.Status = "ignored"; log.StatusReason = "self message_create";
                db.WebhookLogs.Add(log); await db.SaveChangesAsync(ct);
                return Ok(new { status = "ignored", reason = "self message_create" });
            }
            // ── /Branch ──────────────────────────────────────────────────────

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
                clientName, payload.Id, mediaUrl, mediaType, ct,
                whatsAppLineId: line.Id);

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
    /// Procesa eventos message_ack de UltraMsg — actualiza el estado real de entrega
    /// del mensaje en Message.DeliveryStatus y CampaignContact.DeliveryStatus.
    ///
    /// ACK transitions: 0 (queue) → 1 (sent) → 2 (delivered) → 3 (read)
    /// ACK = -1 → invalid (cuenta restringida / número no existe en WhatsApp)
    ///
    /// El sistema ya no depende solo del HTTP 200 de UltraMsg para saber si un
    /// mensaje "llegó". Ahora la BD refleja la verdad reportada por WhatsApp.
    ///
    /// Si llega status invalid|failed|expired|unsent, además marca el
    /// CampaignContact como DispatchStatus=Error para que la próxima
    /// ejecución de la campaña lo reintente (en vez de creer que ya se envió).
    /// </summary>
    private async Task HandleMessageAckAsync(
        UltraMsgWebhookWrapper wrapper, Guid tenantId, CancellationToken ct)
    {
        var data = wrapper.Data;
        if (data is null || string.IsNullOrEmpty(data.Id)) return;

        var externalId = data.Id;
        // Ack puede venir como número ("1", "2"...) o como string ("pending", "sent"...).
        // Si parsea a int, usamos el numérico; si no, intentamos resolver el status
        // desde el propio string y dejamos ack=0 como "desconocido".
        var ack        = int.TryParse(data.Ack, out var ackInt) ? ackInt : 0;
        // Status string normalizado a minúsculas para comparaciones estables.
        // Prioridad: data.Status (explícito) → data.Ack (si vino como string) → mapeo ack→string.
        var status     = (data.Status
                          ?? (ackInt == 0 && !string.IsNullOrEmpty(data.Ack) ? data.Ack : null)
                          ?? AckToStatusString(ack))
                         .ToLowerInvariant();
        var nowUtc     = DateTime.UtcNow;

        // 1) Actualizar Message — SIEMPRE filtrar por TenantId para evitar
        //    colisiones de ExternalMessageId entre instancias UltraMsg.
        var msg = await db.Messages
            .Include(m => m.Conversation)
            .Where(m => m.ExternalMessageId == externalId
                     && m.Conversation.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);
        if (msg != null)
        {
            msg.DeliveryStatus    = status;
            msg.LastAck           = ack;
            msg.DeliveryUpdatedAt = nowUtc;
            if (status == "delivered" && msg.DeliveredAt is null) msg.DeliveredAt = nowUtc;
            if (status == "read"      && msg.ReadAt is null)      msg.ReadAt      = nowUtc;

            // Espejo al enum interno MessageStatus para que el monitor legacy
            // siga funcionando sin tocar UI:
            msg.Status = status switch
            {
                "read"                                    => AgentFlow.Domain.Entities.MessageStatus.Read,
                "delivered"                               => AgentFlow.Domain.Entities.MessageStatus.Delivered,
                "sent"                                    => AgentFlow.Domain.Entities.MessageStatus.Sent,
                "invalid" or "failed" or "expired" or "unsent"
                                                          => AgentFlow.Domain.Entities.MessageStatus.Failed,
                _                                         => msg.Status,
            };
        }

        // 2) Actualizar CampaignContact (envíos masivos) — también scoped al tenant.
        //    CampaignContact no tiene TenantId directo, lo resolvemos vía Campaign.
        var cc = await db.CampaignContacts
            .Where(c => c.ExternalMessageId == externalId
                     && db.Campaigns.Any(camp => camp.Id == c.CampaignId && camp.TenantId == tenantId))
            .FirstOrDefaultAsync(ct);
        if (cc != null)
        {
            cc.DeliveryStatus = status;
            if (status == "delivered" && cc.DeliveredAt is null) cc.DeliveredAt = nowUtc;
            if (status == "read"      && cc.ReadAt is null)      cc.ReadAt      = nowUtc;

            // Si UltraMsg confirma que NO se entregó, corregir la mentira de
            // "Sent" en BD para que el dispatcher pueda reintentar y para que
            // los reportes muestren la verdad.
            if (status is "invalid" or "failed" or "expired" or "unsent")
            {
                cc.DispatchStatus  = AgentFlow.Domain.Enums.DispatchStatus.Error;
                cc.DispatchError ??= $"UltraMsg ack={ack} status={status}";
            }
        }

        // NOTA: SignalR push al monitor — el ConversationNotifier no expone
        // un evento de status update en esta versión. Cuando se agregue, se
        // dispara aquí: await notifier.NotifyMessageStatusAsync(msg, status).
        // El monitor por ahora puede refrescar via polling o re-query.
    }

    private static string AckToStatusString(int ack) => ack switch
    {
        -1 => "invalid",
         0 => "queue",
         1 => "sent",
         2 => "delivered",
         3 => "read",
         _ => "unknown",
    };

    /// <summary>
    /// Resuelve el TenantId a partir del instanceId de UltraMsg. Reusa la
    /// misma tabla WhatsAppLines que el flujo de mensajes entrantes, con la
    /// misma normalización (acepta "133282", "instance133282", etc.).
    /// Devuelve null si la instancia no está registrada o no está activa.
    /// </summary>
    private async Task<Guid?> ResolveTenantFromInstanceAsync(string? rawInstance, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawInstance)) return null;
        var instanceNumber = rawInstance
            .Replace("instance", "", StringComparison.OrdinalIgnoreCase).Trim();
        var line = await db.WhatsAppLines
            .Where(l => l.TenantId != null && l.IsActive)
            .Where(l => l.InstanceId == instanceNumber
                     || l.InstanceId == rawInstance
                     || l.InstanceId == $"instance{instanceNumber}")
            .Select(l => new { l.TenantId })
            .FirstOrDefaultAsync(ct);
        return line?.TenantId;
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
    /// Verificación GET del webhook único de Meta (mismo URL que el POST de eventos).
    /// Meta hace GET con hub.challenge al registrar el callback. Callback URL a
    /// configurar en Meta: /api/webhooks/meta
    /// </summary>
    [HttpGet("meta")]
    public IActionResult MetaVerifyRoot(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromServices] IConfiguration config)
    {
        var expected = config["Meta:VerifyToken"];
        if (mode == "subscribe" && !string.IsNullOrEmpty(challenge) && verifyToken == expected
            && int.TryParse(challenge, out var c))
            return Ok(c);
        return Forbid();
    }

    /// <summary>
    /// Webhook ÚNICO multi-tenant de Meta Cloud API (POST de eventos).
    /// Análogo al de UltraMsg pero resolviendo el tenant por value.metadata.phone_number_id
    /// (= WhatsAppLine.InstanceId de una línea MetaCloudApi). Reusa el mismo DispatchAsync.
    ///
    /// Seguridad: valida la firma X-Hub-Signature-256 = HMAC-SHA256(rawBody, app_secret).
    /// El app_secret se toma de la línea (MetaAppSecret) o, si no, de config Meta:AppSecret.
    /// Si NO hay app_secret configurado, se procesa con warning (permite operar antes de
    /// endurecer; recomendado configurarlo para producción).
    ///
    /// Siempre responde 200 — Meta reintenta agresivamente ante cualquier no-2xx.
    /// </summary>
    [HttpPost("meta")]
    public async Task<IActionResult> MetaWebhook(
        [FromServices] IConfiguration config,
        [FromServices] AgentFlow.Domain.Interfaces.IWhatsAppNumberValidator numberValidator,
        CancellationToken ct)
    {
        string body;
        using (var reader = new StreamReader(Request.Body))
            body = await reader.ReadToEndAsync(ct);

        var signature = Request.Headers["X-Hub-Signature-256"].ToString();

        var log = new AgentFlow.Domain.Entities.WebhookLog
        {
            Provider = "meta",
            RawPayload = body?.Length > 4000 ? body[..4000] : body,
            ReceivedAt = DateTime.UtcNow,
            Status = "received",
        };

        if (string.IsNullOrWhiteSpace(body))
        {
            log.Status = "ignored"; log.StatusReason = "empty body";
            db.WebhookLogs.Add(log); await db.SaveChangesAsync(ct);
            return Ok();
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (Exception ex)
        {
            log.Status = "error"; log.StatusReason = $"invalid json: {ex.Message}";
            db.WebhookLogs.Add(log); await db.SaveChangesAsync(ct);
            return Ok(); // 200 para que Meta no reintente un payload corrupto
        }

        var processed = 0; var statusCount = 0; var statusFailedBlacklisted = 0; string? reason = null;
        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                log.Status = "ignored"; log.StatusReason = "no entry";
                db.WebhookLogs.Add(log); await db.SaveChangesAsync(ct);
                return Ok();
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value)) continue;

                    // ── Estado de aprobación de plantilla (WABA-level, sin phone_number_id) ──
                    var field = change.TryGetProperty("field", out var fld) ? fld.GetString() : null;
                    if (string.Equals(field, "message_template_status_update", StringComparison.OrdinalIgnoreCase))
                    {
                        var wabaId = entry.TryGetProperty("id", out var eid) ? eid.GetString() : null;
                        var updated = await HandleTemplateStatusUpdateAsync(wabaId, value, ct);
                        statusCount += updated; // reutilizamos el contador para el log (no es msg entrante)
                        reason ??= updated > 0 ? "template_status_updated" : "template_status_no_match";
                        continue;
                    }

                    var phoneNumberId = value.TryGetProperty("metadata", out var meta)
                        && meta.TryGetProperty("phone_number_id", out var pid) ? pid.GetString() : null;
                    if (string.IsNullOrEmpty(phoneNumberId)) continue;

                    // Resolver línea Meta por phone_number_id (índice/único de instancia).
                    var line = await db.WhatsAppLines
                        .Include(l => l.Tenant)
                        .Where(l => l.TenantId != null && l.IsActive
                                 && l.Provider == AgentFlow.Domain.Enums.ProviderType.MetaCloudApi
                                 && l.InstanceId == phoneNumberId)
                        .FirstOrDefaultAsync(ct);

                    if (line?.TenantId is null) { reason = $"unknown phone_number_id {phoneNumberId}"; continue; }
                    log.TenantId = line.TenantId; log.InstanceId = phoneNumberId;

                    // Validación de firma: secreto de la línea o global. Sin secreto → warning.
                    var appSecret = !string.IsNullOrEmpty(line.MetaAppSecret) ? line.MetaAppSecret : config["Meta:AppSecret"];
                    if (!string.IsNullOrEmpty(appSecret))
                    {
                        if (!ValidateMetaSignature(body, signature, appSecret))
                        {
                            log.Status = "ignored"; log.StatusReason = "firma HMAC inválida";
                            db.WebhookLogs.Add(log); await db.SaveChangesAsync(ct);
                            return Ok(); // 200: no reprocesar; pero no actuamos sobre el payload
                        }
                    }

                    string? clientName = null;
                    if (value.TryGetProperty("contacts", out var contacts) && contacts.ValueKind == JsonValueKind.Array && contacts.GetArrayLength() > 0
                        && contacts[0].TryGetProperty("profile", out var prof) && prof.TryGetProperty("name", out var nm))
                        clientName = nm.GetString();

                    // Mensajes entrantes
                    if (value.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in messages.EnumerateArray())
                        {
                            var wamid = m.TryGetProperty("id", out var midp) ? midp.GetString() : null;
                            var from = m.TryGetProperty("from", out var fp) ? fp.GetString() : null;
                            var mtype = m.TryGetProperty("type", out var tp) ? tp.GetString() ?? "text" : "text";
                            if (string.IsNullOrEmpty(from)) continue;
                            var fromPhone = from.StartsWith('+') ? from : $"+{from}";

                            // Idempotencia por wamid
                            if (!string.IsNullOrEmpty(wamid) && await db.Messages.AnyAsync(x => x.ExternalMessageId == wamid, ct))
                                continue;

                            string text;
                            string? mediaUrl = null;
                            string? mediaType = null;

                            if (mtype is "image" or "document" or "audio" or "voice")
                            {
                                // Meta entrega un media_id (no URL). Hay que pedir la URL al
                                // Graph API y descargar los bytes, ambos con Bearer del token de la línea.
                                var mediaObj = m.TryGetProperty(mtype, out var mo) && mo.ValueKind == JsonValueKind.Object ? mo : default;
                                var mediaId = mediaObj.ValueKind == JsonValueKind.Object && mediaObj.TryGetProperty("id", out var mid) ? mid.GetString() : null;
                                var caption = mediaObj.ValueKind == JsonValueKind.Object && mediaObj.TryGetProperty("caption", out var cap) ? cap.GetString() : null;

                                byte[]? bytes = null; string? mime = null;
                                if (!string.IsNullOrEmpty(mediaId) && !string.IsNullOrEmpty(line.MetaAccessToken))
                                {
                                    var fetched = await FetchMetaMediaAsync(mediaId, line.MetaAccessToken!, ct);
                                    if (fetched is not null) { bytes = fetched.Value.Bytes; mime = fetched.Value.Mime; }
                                }

                                if (mtype is "audio" or "voice")
                                {
                                    // Transcribir con Whisper (igual que UltraMsg). Fallback si no se pudo.
                                    var trans = bytes is not null
                                        ? await transcriptionService.TranscribeAsync(bytes, $"{wamid ?? Guid.NewGuid().ToString()}.ogg", ct)
                                        : null;
                                    text = !string.IsNullOrWhiteSpace(trans)
                                        ? trans!
                                        : "🎤 [Nota de voz recibida] No pude procesar el audio. ¿Puedes escribir tu mensaje?";
                                }
                                else // image / document
                                {
                                    mediaType = mtype == "document" ? "document" : "image";
                                    if (bytes is not null)
                                    {
                                        try
                                        {
                                            var ext = mtype == "document" ? DetectDocExtension(mime, caption, null) : DetectImageExtension(mime);
                                            var ctype = mime ?? (mtype == "document" ? "application/octet-stream" : "image/jpeg");
                                            var blobName = $"conversations/{line.TenantId.Value}/{DateTime.UtcNow:yyyyMMdd}/{wamid ?? Guid.NewGuid().ToString()}.{ext}";
                                            mediaUrl = await blobStorage.UploadWhatsAppMediaAsync(blobName, bytes, ctype, ct);
                                        }
                                        catch (Exception azEx) { Console.WriteLine($"[Meta][Azure] No se pudo subir media: {azEx.Message}"); }
                                    }
                                    text = !string.IsNullOrWhiteSpace(caption) ? caption! : (mtype == "document" ? "📄 [Documento]" : "📷 [Imagen]");
                                }
                            }
                            else
                            {
                                text = ExtractMetaMessageText(m, mtype);
                            }

                            if (string.IsNullOrWhiteSpace(text)) continue;

                            await DispatchAsync(
                                line.TenantId.Value, fromPhone, text, ChannelType.WhatsApp,
                                clientName, wamid, mediaUrl, mediaType, ct, whatsAppLineId: line.Id);
                            processed++;
                        }
                    }

                    // Estados de entrega (sent/delivered/read/failed). Para los 'failed'
                    // cuya causa es "número no es WhatsApp" (códigos 131026 / 131021),
                    // auto-registramos el destinatario en la lista negra del tenant —
                    // equivalente reactivo a la pre-validación /contacts/check de UltraMsg,
                    // ya que Meta no ofrece pre-check y solo reporta el número malo al fallar.
                    if (value.TryGetProperty("statuses", out var sts) && sts.ValueKind == JsonValueKind.Array)
                    {
                        statusCount += sts.GetArrayLength();
                        foreach (var st in sts.EnumerateArray())
                        {
                            var stStatus = st.TryGetProperty("status", out var ss) ? ss.GetString() : null;
                            if (!string.Equals(stStatus, "failed", StringComparison.OrdinalIgnoreCase)) continue;

                            var recipient = st.TryGetProperty("recipient_id", out var rid) ? rid.GetString() : null;
                            if (string.IsNullOrWhiteSpace(recipient)) continue;
                            var recipientPhone = recipient.StartsWith('+') ? recipient : $"+{recipient}";

                            // Tomar el primer error reportado (code + title).
                            int errCode = -1; string? errTitle = null;
                            if (st.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
                            {
                                var e0 = errs[0];
                                if (e0.TryGetProperty("code", out var ec) && ec.ValueKind == JsonValueKind.Number) errCode = ec.GetInt32();
                                errTitle = e0.TryGetProperty("title", out var et) ? et.GetString()
                                         : e0.TryGetProperty("message", out var em) ? em.GetString() : null;
                            }

                            // Solo códigos que indican NÚMERO inválido. Otros 'failed'
                            // (131047 re-engagement/ventana 24h, 131051 tipo no soportado,
                            // 131026 a veces es transitorio pero típicamente número malo)
                            // NO deben ensuciar la lista negra.
                            if (errCode is 131026 or 131021)
                            {
                                try
                                {
                                    await numberValidator.RegisterAsBlacklistedAsync(
                                        recipientPhone,
                                        reason: $"Meta status=failed (code {errCode}): {errTitle ?? "número no entregable"}",
                                        source: "meta-status-failed",
                                        tenantId: line.TenantId,
                                        campaignId: null,
                                        userId: null,
                                        ct: ct);
                                    statusFailedBlacklisted++;
                                }
                                catch (Exception bex)
                                {
                                    Console.WriteLine($"[Meta][status-failed] No se pudo blacklistear {recipientPhone}: {bex.Message}");
                                }
                            }
                        }
                    }
                }
            }
        }

        log.Status = processed > 0 ? "processed" : statusCount > 0 ? "status" : "ignored";
        log.StatusReason = reason ?? $"msgs={processed} statuses={statusCount}"
            + (statusFailedBlacklisted > 0 ? $" blacklisted={statusFailedBlacklisted}" : "");
        db.WebhookLogs.Add(log);
        try { await db.SaveChangesAsync(ct); } catch { }
        return Ok();
    }

    /// <summary>Texto a entregar al agente según el tipo de mensaje Meta.</summary>
    /// <summary>
    /// Procesa un evento message_template_status_update de Meta (aprobación/rechazo de
    /// plantilla). Resuelve la plantilla por su MetaTemplateId, o por (nombre+idioma)
    /// dentro del WABA que envió el evento — garantizando que pertenece al tenant dueño
    /// de ese WABA (nunca cruza tenants). Devuelve cuántas plantillas actualizó.
    /// </summary>
    private async Task<int> HandleTemplateStatusUpdateAsync(string? wabaId, JsonElement value, CancellationToken ct)
    {
        var ev = value.TryGetProperty("event", out var e) ? e.GetString() : null;            // APPROVED/REJECTED/...
        var metaTemplateId = value.TryGetProperty("message_template_id", out var mid)
            ? (mid.ValueKind == JsonValueKind.Number ? mid.GetRawText() : mid.GetString())
            : null;
        var name = value.TryGetProperty("message_template_name", out var nm) ? nm.GetString() : null;
        var lang = value.TryGetProperty("message_template_language", out var lg) ? lg.GetString() : null;
        var rejReason = value.TryGetProperty("reason", out var rs) ? rs.GetString() : null;
        if (string.IsNullOrEmpty(ev)) return 0;

        // Candidatas: por MetaTemplateId, o por (nombre+idioma) restringido al WABA del evento.
        var query = db.MetaMessageTemplates.AsQueryable();
        MetaMessageTemplate? template = null;

        if (!string.IsNullOrEmpty(metaTemplateId))
            template = await query.FirstOrDefaultAsync(t => t.MetaTemplateId == metaTemplateId, ct);

        if (template is null && !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(wabaId))
        {
            // Resolver por (nombre+idioma) SOLO entre plantillas cuyas líneas tienen este WABA
            // → blindaje por-tenant: el WABA pertenece a la línea de un tenant específico.
            var lineIds = await db.WhatsAppLines
                .Where(l => l.MetaWabaId == wabaId)
                .Select(l => l.Id)
                .ToListAsync(ct);
            template = await query.FirstOrDefaultAsync(
                t => lineIds.Contains(t.WhatsAppLineId) && t.Name == name
                  && (lang == null || t.Language == lang), ct);
        }

        if (template is null) return 0;

        template.MetaStatus = ev!.ToUpperInvariant();
        template.MetaRejectedReason = string.Equals(ev, "REJECTED", StringComparison.OrdinalIgnoreCase)
            ? rejReason : null;
        if (!string.IsNullOrEmpty(metaTemplateId) && string.IsNullOrEmpty(template.MetaTemplateId))
            template.MetaTemplateId = metaTemplateId;
        template.LastSyncedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return 1;
    }

    private static string ExtractMetaMessageText(JsonElement m, string mtype)
    {
        switch (mtype)
        {
            case "text":
                return m.TryGetProperty("text", out var t) && t.TryGetProperty("body", out var bdy) ? bdy.GetString() ?? "" : "";
            case "button":
                return m.TryGetProperty("button", out var btn) && btn.TryGetProperty("text", out var btt) ? btt.GetString() ?? "" : "";
            case "interactive":
                if (m.TryGetProperty("interactive", out var it))
                {
                    if (it.TryGetProperty("button_reply", out var br) && br.TryGetProperty("title", out var brt)) return brt.GetString() ?? "";
                    if (it.TryGetProperty("list_reply", out var lr) && lr.TryGetProperty("title", out var lrt)) return lrt.GetString() ?? "";
                }
                return "";
            case "image":
                return m.TryGetProperty("image", out var im) && im.TryGetProperty("caption", out var imc) ? imc.GetString() ?? "📷 [Imagen]" : "📷 [Imagen]";
            case "document":
                return m.TryGetProperty("document", out var dc) && dc.TryGetProperty("caption", out var dcc) ? dcc.GetString() ?? "📄 [Documento]" : "📄 [Documento]";
            case "audio":
            case "voice":
                return "🎤 [Nota de voz recibida] ¿Puedes escribir tu mensaje?";
            default:
                return $"[{mtype}]";
        }
    }

    /// <summary>
    /// Descarga un archivo de media de Meta: GET /{media-id} (con Bearer) devuelve
    /// la URL temporal, luego GET de esa URL (también con Bearer) devuelve los bytes.
    /// Devuelve null si algo falla (no rompe el webhook).
    /// </summary>
    private async Task<(byte[] Bytes, string? Mime)?> FetchMetaMediaAsync(string mediaId, string accessToken, CancellationToken ct)
    {
        try
        {
            var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);

            using var metaReq = new HttpRequestMessage(HttpMethod.Get, $"https://graph.facebook.com/v21.0/{mediaId}");
            metaReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            var metaResp = await http.SendAsync(metaReq, ct);
            if (!metaResp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await metaResp.Content.ReadAsStringAsync(ct));
            var url = doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;
            var mime = doc.RootElement.TryGetProperty("mime_type", out var mm) ? mm.GetString() : null;
            if (string.IsNullOrEmpty(url)) return null;

            using var fileReq = new HttpRequestMessage(HttpMethod.Get, url);
            fileReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            var fileResp = await http.SendAsync(fileReq, ct);
            if (!fileResp.IsSuccessStatusCode) return null;

            return (await fileResp.Content.ReadAsByteArrayAsync(ct), mime);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Meta][Media] No se pudo descargar media {mediaId}: {ex.Message}");
            return null;
        }
    }

    private static bool ValidateMetaSignature(string body, string? signatureHeader, string? appSecret)
    {
        if (string.IsNullOrEmpty(appSecret) || string.IsNullOrEmpty(signatureHeader)) return false;
        var provided = signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signatureHeader["sha256=".Length..] : signatureHeader;
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(appSecret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(body));
        var computed = Convert.ToHexString(hash).ToLowerInvariant();
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(computed),
            System.Text.Encoding.UTF8.GetBytes(provided.ToLowerInvariant()));
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

    // El JSON viene en snake_case ("event_type") y PropertyNameCaseInsensitive
    // NO convierte snake_case → camelCase. Mapeo explícito.
    [System.Text.Json.Serialization.JsonPropertyName("event_type")]
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

    // ── Para eventos message_ack (delivery status) ─────────────────────────
    // ack: -1=invalid | 0=queue/pending | 1=sent | 2=delivered | 3=read
    // status: "queue" | "sent" | "delivered" | "read" | "invalid" | "failed" | "expired" | "unsent"
    //
    // OJO: UltraMsg envía "ack" con dos semánticas según el event_type:
    //   - message_ack       → numérico ("1", "2", "3"...)
    //   - message_received  → string ("pending", "sent", "delivered", "read")
    // Por eso tipamos como string? y parseamos a int en el handler de ack
    // cuando lo necesitamos. Si lo tipamos como int? el deserializador rompe
    // todos los message_received con parse error y los inbound se pierden.
    public string? Ack { get; set; }
    public string? Status { get; set; }
}
