using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

/// <summary>
/// Endpoints de callback que n8n llama durante la ejecución del flujo de campaña.
/// Autenticados únicamente por X-N8N-Key header — no requieren JWT del tenant.
///
/// Flujo push (n8n orquesta):
///   1. Lanzar campaña (CampaignsController) → envía contactos a n8n
///   2. n8n valida teléfonos → POST /api/campaigns/{id}/mark-invalid
///   3. n8n verifica duplicados → POST /api/campaigns/check-duplicates
///   4. n8n envía mensaje por contacto → POST /api/webhooks/campaign-send
///   5. n8n actualiza estado → POST /api/campaigns/contact-sent
///                           → POST /api/campaigns/contact-failed
///   6. n8n finaliza campaña → POST /api/campaigns/completed
/// </summary>
[ApiController]
[Route("api")]
public class N8nCallbackController(
    AgentFlowDbContext db,
    IConfiguration cfg,
    IHttpClientFactory httpClientFactory,
    IConversationNotifier notifier) : ControllerBase
{
    // ── Autenticación por API key ─────────────────────────────────────────────

    private bool IsAuthorized()
    {
        var expected = cfg["N8n:ApiKey"];
        if (string.IsNullOrEmpty(expected)) return true; // no configurada → solo dev
        Request.Headers.TryGetValue("X-N8N-Key", out var provided);
        return provided == expected;
    }

    // ── 1. Marcar contactos inválidos ─────────────────────────────────────────

    /// <summary>
    /// n8n llama esto cuando detecta teléfonos inválidos en el lote.
    /// Los contactos se marcan como Skipped para excluirlos de futuros reintentos.
    /// </summary>
    [HttpPost("campaigns/{campaignId:guid}/mark-invalid")]
    public async Task<IActionResult> MarkInvalid(
        Guid campaignId,
        [FromBody] MarkInvalidRequest req,
        CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized(new { error = "X-N8N-Key inválida." });

        int marked = 0;
        if (req.InvalidContacts is { Count: > 0 })
        {
            var phones = req.InvalidContacts
                .Select(c => c.Phone)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToHashSet();

            var contacts = await db.CampaignContacts
                .Where(c => c.CampaignId == campaignId && phones.Contains(c.PhoneNumber))
                .ToListAsync(ct);

            foreach (var c in contacts)
            {
                c.IsPhoneValid    = false;
                c.DispatchStatus  = DispatchStatus.Skipped;
                c.DispatchError   = "phone_invalid_n8n";
            }

            marked = contacts.Count;
            await db.SaveChangesAsync(ct);
        }

        return Ok(new { marked });
    }

    // ── 2. Verificar duplicados / sesiones activas ────────────────────────────

    /// <summary>
    /// n8n pregunta qué teléfonos ya tienen una conversación activa.
    /// Responde con lista de teléfonos activos para que n8n los excluya.
    /// </summary>
    [HttpPost("campaigns/check-duplicates")]
    public async Task<IActionResult> CheckDuplicates(
        [FromBody] CheckDuplicatesRequest req,
        CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized(new { error = "X-N8N-Key inválida." });

        List<string> activeSessions = [];

        if (req.Phones is { Count: > 0 })
        {
            // Buscar conversaciones activas en el tenant indicado en el header
            Request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeader);
            if (Guid.TryParse(tenantHeader, out var tenantId))
            {
                // Parsear campaignId si viene en el request (para excluir la campaña actual)
                Guid? currentCampaignId = null;
                if (!string.IsNullOrEmpty(req.CampaignId))
                {
                    var rawCampaignId = req.CampaignId.TrimStart('=').Trim();
                    if (Guid.TryParse(rawCampaignId, out var parsedId))
                        currentCampaignId = parsedId;
                }

                // Obtener IDs de campañas completadas para no filtrar sus contactos
                // Un contacto que está en una campaña completada puede recibir una nueva campaña
                var completedCampaignIds = await db.Campaigns
                    .Where(c => c.TenantId == tenantId && c.Status == CampaignStatus.Completed)
                    .Select(c => c.Id)
                    .ToListAsync(ct);

                activeSessions = await db.Conversations
                    .Where(c => c.TenantId == tenantId
                             && req.Phones.Contains(c.ClientPhone)
                             && c.Status == ConversationStatus.Active
                             // Excluir conversaciones de la campaña actual o de campañas ya completadas
                             && (currentCampaignId == null || c.CampaignId != currentCampaignId)
                             && (c.CampaignId == null || !completedCampaignIds.Contains(c.CampaignId.Value)))
                    .Select(c => c.ClientPhone)
                    .ToListAsync(ct);
            }
        }

        return Ok(new { activeSessions });
    }

    // ── 3. Programar contactos diferidos ──────────────────────────────────────

    /// <summary>
    /// n8n llama esto cuando el límite de calentamiento (warmup) dejó contactos
    /// sin enviar hoy. Se guardan como pendientes para mañana.
    /// Implementación actual: acknowledges the request (no re-schedule logic yet).
    /// </summary>
    [HttpPost("campaigns/schedule-deferred")]
    public IActionResult ScheduleDeferred([FromBody] ScheduleDeferredRequest req)
    {
        if (!IsAuthorized()) return Unauthorized(new { error = "X-N8N-Key inválida." });

        // TODO: implementar re-programación con Hangfire cuando se necesite warmup
        return Ok(new
        {
            acknowledged = true,
            deferredCount = req.Contacts?.Count ?? 0,
            message = $"Contactos diferidos recibidos para campaña {req.CampaignId}. Se procesarán manualmente.",
        });
    }

    // ── 4. Enviar mensaje de campaña (Claude + UltraMsg) ─────────────────────

    /// <summary>
    /// Endpoint principal del flujo push.
    /// n8n llama esto por cada contacto con los datos del cliente.
    ///
    /// Proceso:
    ///   1. Carga el prompt del maestro de campaña
    ///   2. Genera mensaje personalizado con Claude (Haiku — rápido y económico)
    ///   3. Envía el mensaje por UltraMsg
    ///   4. Retorna { success, externalMessageId, generatedMessage }
    ///
    /// La actualización del estado del contacto en BD se hace en los endpoints
    /// /contact-sent y /contact-failed que n8n llama inmediatamente después.
    /// </summary>
    [HttpPost("webhooks/campaign-send")]
    public async Task<IActionResult> CampaignSend(
        [FromBody] CampaignSendRequest req,
        CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized(new { error = "X-N8N-Key inválida." });

        var sw = Stopwatch.StartNew();

        // ── Cargar campaña, tenant y prompt ─────────────────────────────────
        var campaign = await db.Campaigns
            .Include(c => c.CampaignTemplate)
            .Include(c => c.Tenant)
            .FirstOrDefaultAsync(c => c.Id == req.CampaignId, ct);

        if (campaign is null)
            return NotFound(new { error = "Campaña no encontrada.", success = false });

        string? promptText = null;
        if (campaign.CampaignTemplate?.PromptTemplateIds?.Count > 0)
        {
            var pid = campaign.CampaignTemplate.PromptTemplateIds[0];
            var pt  = await db.PromptTemplates.FindAsync([pid], ct);
            promptText = pt?.SystemPrompt;
        }

        if (string.IsNullOrWhiteSpace(promptText))
            return BadRequest(new { error = "La campaña no tiene prompt configurado.", success = false, campaignId = req.CampaignId, phone = req.Phone });

        // ── Generar mensaje con Claude ───────────────────────────────────────
        string generatedMessage;
        try
        {
            // Construir contexto de variables desde ContactDataJson + campos directos
            var context = CallbackHelpers.BuildContext(req.ContactDataJson);
            context.TryAdd("NombreCliente",  req.ClientName    ?? "");
            context.TryAdd("NumeroPoliza",   req.PolicyNumber  ?? "");
            context.TryAdd("MontoDeuda",     req.PendingAmount?.ToString("F2") ?? "0.00");
            context.TryAdd("Aseguradora",    req.Insurance     ?? "");

            var resolvedPrompt = CallbackHelpers.ResolveVariables(promptText, context);
            var userMsg        = CallbackHelpers.BuildUserMessage(req.ContactDataJson, context);

            // La API key de Anthropic se almacena por tenant (Tenant.LlmApiKey)
            var apiKey = campaign.Tenant?.LlmApiKey;
            if (string.IsNullOrEmpty(apiKey))
                return StatusCode(503, new { error = "El tenant no tiene Anthropic API key configurada (LlmApiKey).", success = false, campaignId = req.CampaignId, phone = req.Phone });

            var client   = new AnthropicClient(apiKey);
            var messages = new List<Anthropic.SDK.Messaging.Message>
            {
                new() { Role = RoleType.User, Content = [new TextContent { Text = userMsg }] }
            };

            var resp = await client.Messages.GetClaudeMessageAsync(
                new MessageParameters
                {
                    Model       = "claude-haiku-4-5-20251001",
                    MaxTokens   = 512,
                    System      = [new SystemMessage(resolvedPrompt)],
                    Messages    = messages,
                    Stream      = false,
                    Temperature = new decimal(0.2),
                }, ct);

            generatedMessage = resp.Content.OfType<TextContent>()
                                           .FirstOrDefault()?.Text?.Trim() ?? "";
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success    = false,
                error      = $"Error generando mensaje con Claude: {ex.Message}",
                campaignId = req.CampaignId,
                phone      = req.Phone,
            });
        }

        // ── Enviar por UltraMsg ──────────────────────────────────────────────
        var instanceId = req.TenantConfig?.UltraMsgInstanceId;
        var token      = req.TenantConfig?.UltraMsgToken;

        if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(token))
            return BadRequest(new { error = "Faltan credenciales UltraMsg en tenantConfig.", success = false, campaignId = req.CampaignId, phone = req.Phone });

        string? externalMessageId = null;
        try
        {
            var httpClient = httpClientFactory.CreateClient();
            // UltraMsg requiere form-encoded, no JSON (igual que UltraMsgProvider.SendTextAsync)
            // El instanceId en BD puede venir como "140984" o "instance140984" — normalizar
            var normalizedInstanceId = instanceId.StartsWith("instance", StringComparison.OrdinalIgnoreCase)
                ? instanceId : $"instance{instanceId}";
            var formData = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = token,
                ["to"]    = req.Phone,
                ["body"]  = generatedMessage,
            });

            var httpResp = await httpClient.PostAsync(
                $"https://api.ultramsg.com/{normalizedInstanceId}/messages/chat",
                formData,
                ct);

            var responseBody = await httpResp.Content.ReadAsStringAsync(ct);
            sw.Stop();

            if (!httpResp.IsSuccessStatusCode)
                return Ok(new
                {
                    success    = false,
                    error      = $"UltraMsg {(int)httpResp.StatusCode}: {responseBody}",
                    durationMs = sw.ElapsedMilliseconds,
                    campaignId = req.CampaignId,
                    phone      = req.Phone,
                });

            try
            {
                var parsed = JsonSerializer.Deserialize<JsonElement>(responseBody);
                externalMessageId = parsed.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            }
            catch { /* ignorar si no parsea el ID */ }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Ok(new { success = false, error = ex.Message, durationMs = sw.ElapsedMilliseconds, campaignId = req.CampaignId, phone = req.Phone });
        }

        // ── Crear o recuperar conversación en el monitor ─────────────────────
        // La conversación se crea inmediatamente al enviar para que el ejecutivo
        // pueda verla en el monitor antes de que el cliente responda.
        Guid conversationId;
        try
        {
            var tenantId = campaign.TenantId;

            // Reusar conversación abierta si ya existe para este teléfono
            var existing = await db.Conversations
                .FirstOrDefaultAsync(c => c.TenantId == tenantId
                                       && c.ClientPhone == req.Phone
                                       && c.Status != ConversationStatus.Closed, ct);

            if (existing is not null)
            {
                conversationId = existing.Id;
                existing.LastActivityAt = DateTime.UtcNow;
            }
            else
            {
                var conversation = new Conversation
                {
                    Id             = Guid.NewGuid(),
                    TenantId       = tenantId,
                    ClientPhone    = req.Phone,
                    ClientName     = req.ClientName,
                    PolicyNumber   = req.PolicyNumber,
                    Channel        = ChannelType.WhatsApp,
                    ActiveAgentId  = req.AgentId,
                    CampaignId     = req.CampaignId,
                    Status         = ConversationStatus.WaitingClient,
                    StartedAt      = DateTime.UtcNow,
                    LastActivityAt = DateTime.UtcNow,
                };
                db.Conversations.Add(conversation);
                conversationId = conversation.Id;
            }

            // Registrar el mensaje saliente
            var outboundMsg = new AgentFlow.Domain.Entities.Message
            {
                Id                = Guid.NewGuid(),
                ConversationId    = conversationId,
                Direction         = MessageDirection.Outbound,
                Status            = MessageStatus.Sent,
                Content           = generatedMessage,
                ExternalMessageId = externalMessageId,
                IsFromAgent       = true,
                AgentName         = campaign.CampaignTemplate?.Name ?? "Agente campaña",
                SentAt            = DateTime.UtcNow,
            };
            db.Messages.Add(outboundMsg);
            await db.SaveChangesAsync(ct);

            // Notificar monitor en tiempo real vía SignalR
            await notifier.NotifyMessageAsync(tenantId.ToString(), new
            {
                conversationId  = conversationId,
                phone           = req.Phone,
                clientName      = req.ClientName,
                message         = generatedMessage,
                direction       = "Outbound",
                isFromAgent     = true,
                sentAt          = DateTime.UtcNow,
                status          = ConversationStatus.WaitingClient.ToString(),
            });
        }
        catch
        {
            // Si falla la creación de conversación no bloqueamos el flujo principal
            conversationId = Guid.Empty;
        }

        return Ok(new
        {
            success           = true,
            externalMessageId,
            conversationId,
            generatedMessage,
            durationMs        = sw.ElapsedMilliseconds,
            campaignId        = req.CampaignId,
            phone             = req.Phone,
        });
    }

    // ── 5. Marcar contacto como enviado ──────────────────────────────────────

    [HttpPost("campaigns/contact-sent")]
    public async Task<IActionResult> ContactSent(
        [FromBody] ContactSentRequest req,
        CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized(new { error = "X-N8N-Key inválida." });

        // n8n puede enviar el Guid con prefijo '=' — limpiar y parsear
        var rawId = req.CampaignId?.TrimStart('=').Trim();
        if (!Guid.TryParse(rawId, out var campaignId))
            return BadRequest(new { error = $"campaignId inválido: '{req.CampaignId}'" });

        var phone = req.Phone?.TrimStart('=').Trim();

        var contact = await db.CampaignContacts
            .FirstOrDefaultAsync(c => c.CampaignId == campaignId
                                   && c.PhoneNumber == phone, ct);

        if (contact is not null)
        {
            contact.DispatchStatus    = DispatchStatus.Sent;
            contact.SentAt            = DateTime.UtcNow;
            contact.ExternalMessageId = req.ExternalMessageId;
            await db.SaveChangesAsync(ct);
        }

        // Incrementar contador de forma atómica para evitar condición de carrera
        // cuando múltiples contactos se marcan como enviados simultáneamente
        await db.Campaigns
            .Where(c => c.Id == campaignId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                c => c.ProcessedContacts, c => c.ProcessedContacts + 1), ct);

        // Auto-completar la campaña si ProcessedContacts >= TotalContacts.
        // IMPORTANTE: usar el change tracker (SaveChangesAsync) en lugar de ExecuteUpdateAsync
        // para que la conversión HasConversion<string>() se aplique correctamente.
        // ExecuteUpdateAsync no aplica el value converter y genera SQL con el entero del enum,
        // lo que produce 0 filas actualizadas cuando la columna almacena strings.
        bool autoCompleted = false;
        var freshCampaign = await db.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct);

        if (freshCampaign is not null
            && freshCampaign.ProcessedContacts >= freshCampaign.TotalContacts
            && freshCampaign.TotalContacts > 0
            && freshCampaign.Status != CampaignStatus.Completed
            && freshCampaign.Status != CampaignStatus.Failed)
        {
            // Cargar con tracking para que SaveChangesAsync aplique el converter correctamente
            var campaignToComplete = await db.Campaigns
                .FirstOrDefaultAsync(c => c.Id == campaignId, ct);

            if (campaignToComplete is not null
                && campaignToComplete.Status != CampaignStatus.Completed
                && campaignToComplete.Status != CampaignStatus.Failed)
            {
                campaignToComplete.Status = CampaignStatus.Completed;
                campaignToComplete.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                autoCompleted = true;
            }
        }

        return Ok(new { updated = contact is not null, campaignId = rawId, phone, autoCompleted });
    }

    // ── 6. Registrar fallo de envío ───────────────────────────────────────────

    [HttpPost("campaigns/contact-failed")]
    public async Task<IActionResult> ContactFailed(
        [FromBody] ContactFailedRequest req,
        CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized(new { error = "X-N8N-Key inválida." });

        var rawId = req.CampaignId?.TrimStart('=').Trim();
        if (!Guid.TryParse(rawId, out var campaignId))
            return BadRequest(new { error = $"campaignId inválido: '{req.CampaignId}'" });

        var phone = req.Phone?.TrimStart('=').Trim();

        var contact = await db.CampaignContacts
            .FirstOrDefaultAsync(c => c.CampaignId == campaignId
                                   && c.PhoneNumber == phone, ct);

        if (contact is not null)
        {
            contact.DispatchAttempts++;
            contact.DispatchStatus = contact.DispatchAttempts >= 3
                ? DispatchStatus.Error
                : DispatchStatus.Retry;
            contact.DispatchError = req.Error;
            await db.SaveChangesAsync(ct);
        }

        // Incrementar contador de forma atómica (igual que contact-sent)
        // para que la campaña pueda auto-completar aunque haya fallas.
        await db.Campaigns
            .Where(c => c.Id == campaignId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                c => c.ProcessedContacts, c => c.ProcessedContacts + 1), ct);

        bool autoCompleted = false;
        var freshCampaign = await db.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct);

        if (freshCampaign is not null
            && freshCampaign.ProcessedContacts >= freshCampaign.TotalContacts
            && freshCampaign.TotalContacts > 0
            && freshCampaign.Status != CampaignStatus.Completed
            && freshCampaign.Status != CampaignStatus.Failed)
        {
            var campaignToComplete = await db.Campaigns
                .FirstOrDefaultAsync(c => c.Id == campaignId, ct);

            if (campaignToComplete is not null
                && campaignToComplete.Status != CampaignStatus.Completed
                && campaignToComplete.Status != CampaignStatus.Failed)
            {
                campaignToComplete.Status = CampaignStatus.Completed;
                campaignToComplete.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                autoCompleted = true;
            }
        }

        return Ok(new { updated = contact is not null, autoCompleted });
    }

    // ── 7. Marcar campaña como completada ─────────────────────────────────────

    [HttpPost("campaigns/completed")]
    public async Task<IActionResult> CampaignCompleted(
        [FromBody] CampaignCompletedRequest req,
        CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized(new { error = "X-N8N-Key inválida." });

        var rawId = req.CampaignId?.TrimStart('=').Trim();
        if (!Guid.TryParse(rawId, out var campaignId))
            return BadRequest(new { error = $"campaignId inválido: '{req.CampaignId}'" });

        var campaign = await db.Campaigns.FindAsync([campaignId], ct);
        if (campaign is not null)
        {
            campaign.Status      = CampaignStatus.Completed;
            campaign.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            completed        = campaign is not null,
            totalSent        = req.TotalSent,
            totalDeferred    = req.TotalDeferred,
            totalDuplicates  = req.TotalDuplicates,
        });
    }
}

// ── Helpers compartidos ────────────────────────────────────────────────────────

internal static class CallbackHelpers
{
    internal static Dictionary<string, string> BuildContext(string? json)
    {
        var ctx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return ctx;
        try
        {
            var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
            if (rows is null) return ctx;
            foreach (var row in rows)
                foreach (var (k, v) in row)
                {
                    var s = v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString();
                    ctx.TryAdd(k, s);
                }
            ctx["__TotalRegistros__"] = rows.Count.ToString();
        }
        catch { }
        return ctx;
    }

    internal static string ResolveVariables(string template, Dictionary<string, string> ctx)
        => Regex.Replace(template, @"\{\{(\w+)\}\}",
            m => ctx.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    internal static string BuildUserMessage(string? json, Dictionary<string, string> ctx)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "Redacta el mensaje para el cliente con los datos disponibles.";
        try
        {
            var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
            if (rows is null || rows.Count == 0)
                return "Redacta el mensaje para el cliente con los datos disponibles.";
            if (rows.Count == 1)
            {
                var lines = rows[0].Select(kv => $"- {kv.Key}: {kv.Value}");
                return $"Redacta el mensaje para el cliente:\n{string.Join('\n', lines)}";
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Redacta el mensaje para el cliente con {rows.Count} pólizas:");
            for (var i = 0; i < rows.Count; i++)
            {
                sb.AppendLine($"\nPóliza {i + 1}:");
                foreach (var (k, v) in rows[i]) sb.AppendLine($"  - {k}: {v}");
            }
            return sb.ToString();
        }
        catch { return "Redacta el mensaje para el cliente con los datos disponibles."; }
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record MarkInvalidRequest(List<InvalidContactDto>? InvalidContacts);
public record InvalidContactDto(string Phone, string? Reason);

public record CheckDuplicatesRequest(List<string>? Phones, string? CampaignId = null);

public record ScheduleDeferredRequest(
    Guid CampaignId,
    Guid AgentId,
    List<JsonElement>? Contacts,
    int WarmupDay,
    TenantConfigDto? TenantConfig);

public record CampaignSendRequest(
    Guid CampaignId,
    Guid? AgentId,
    string Phone,
    string? ClientName,
    string? PolicyNumber,
    decimal? PendingAmount,
    string? Insurance,
    string? ContactDataJson,
    TenantConfigDto? TenantConfig);

public record TenantConfigDto(
    string? UltraMsgInstanceId,
    string? UltraMsgToken,
    string? TenantId);

public record ContactSentRequest(
    string? CampaignId,
    string? Phone,
    string? ExternalMessageId,
    string? ConversationId);

public record ContactFailedRequest(
    string? CampaignId,
    string? Phone,
    string? Error);

public record CampaignCompletedRequest(
    string? CampaignId,
    int TotalSent,
    int TotalDeferred,
    int TotalDuplicates);
