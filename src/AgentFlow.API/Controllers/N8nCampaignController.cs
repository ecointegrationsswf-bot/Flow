using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Infrastructure.Persistence;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

/// <summary>
/// Endpoints exclusivos para el workflow de n8n.
/// Autenticados por API key (header X-N8N-Key) — no requieren JWT.
///
/// Flujo que sigue n8n:
///   1. GET  /api/n8n/campaigns/{id}/claim-next  → obtiene el siguiente contacto pendiente (atomic)
///   2. POST /api/n8n/contacts/{id}/generate-message → genera mensaje personalizado con IA
///   3. POST /api/n8n/contacts/{id}/send            → envía a UltraMsg y actualiza estado
/// </summary>
[ApiController]
[Route("api/n8n")]
public class N8nCampaignController(
    AgentFlowDbContext db,
    IConfiguration cfg,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    // ── Autenticación por API key ─────────────────────────────────────────────

    private bool IsAuthorized()
    {
        var expected = cfg["N8n:ApiKey"];
        if (string.IsNullOrEmpty(expected)) return true; // no configurada → solo dev
        Request.Headers.TryGetValue("X-N8N-Key", out var provided);
        return provided == expected;
    }

    // ── 1. Claim-next ─────────────────────────────────────────────────────────

    /// <summary>
    /// Devuelve el siguiente contacto pendiente de la campaña y lo marca como "Claimed"
    /// de forma atómica para evitar duplicados en ambientes concurrentes.
    ///
    /// Incluye en la respuesta:
    /// - Datos del contacto (teléfono, ContactDataJson)
    /// - Snapshot del prompt del PromptTemplate vinculado al maestro de campaña
    /// - Credenciales UltraMsg del tenant
    ///
    /// Si no hay más pendientes, retorna 204 → n8n debe cerrar el loop.
    /// </summary>
    [HttpGet("campaigns/{campaignId:guid}/claim-next")]
    public async Task<IActionResult> ClaimNext(Guid campaignId, CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized(new { error = "X-N8N-Key inválida." });

        // ── Claim atómico con UPDATE + OUTPUT ────────────────────────────────
        // Timeout de claim: si un contacto lleva >5 min como Claimed sin confirmación,
        // vuelve a estar disponible (protección ante caídas de n8n).
        var claimTimeoutUtc = DateTime.UtcNow.AddMinutes(-5);

        using var tx = await db.Database.BeginTransactionAsync(ct);

        var contact = await db.CampaignContacts
            .Where(c => c.CampaignId == campaignId
                     && (c.DispatchStatus == DispatchStatus.Pending || c.DispatchStatus == DispatchStatus.Retry)
                     && (c.ClaimedAt == null || c.ClaimedAt < claimTimeoutUtc))
            .OrderBy(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (contact is null)
        {
            await tx.CommitAsync(ct);

            // Sin más pendientes → marcar campaña como Completed
            var camp = await db.Campaigns.FindAsync([campaignId], ct);
            if (camp is { Status: CampaignStatus.Running })
            {
                camp.Status = CampaignStatus.Completed;
                camp.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            return NoContent(); // 204 → n8n termina el loop
        }

        contact.DispatchStatus = DispatchStatus.Claimed;
        contact.ClaimedAt = DateTime.UtcNow;
        contact.DispatchAttempts++;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // ── Cargar prompt del maestro de campaña ────────────────────────────
        var campaign = await db.Campaigns
            .Include(c => c.CampaignTemplate)
            .Include(c => c.Tenant)
            .FirstAsync(c => c.Id == campaignId, ct);

        string? promptText = null;
        Guid? promptTemplateId = null;

        if (campaign.CampaignTemplate?.PromptTemplateIds?.Count > 0)
        {
            var pid = campaign.CampaignTemplate.PromptTemplateIds[0];
            promptTemplateId = pid;
            var pt = await db.PromptTemplates.FindAsync([pid], ct);
            promptText = pt?.SystemPrompt;
        }

        // ── Credenciales UltraMsg del tenant ────────────────────────────────
        var tenant = campaign.Tenant;

        return Ok(new
        {
            contactId       = contact.Id,
            phone           = contact.PhoneNumber,
            clientName      = contact.ClientName,
            contactDataJson = contact.ContactDataJson,
            attemptNumber   = contact.DispatchAttempts,
            promptTemplateId,
            promptSnapshot  = promptText,
            ultramsg = new
            {
                instanceId = tenant.WhatsAppInstanceId,
                token      = tenant.WhatsAppApiToken,
            },
            tenantId        = campaign.TenantId,
        });
    }

    // ── 2. Generate-message ───────────────────────────────────────────────────

    /// <summary>
    /// Genera el mensaje personalizado para el contacto usando el prompt del maestro
    /// de campaña y los datos del cliente en ContactDataJson.
    ///
    /// Estrategia:
    /// 1. Resuelve variables {{NombreCliente}}, {{KeyValue}}, etc. del primer registro
    /// 2. Envía al LLM: system = prompt resuelto, user = datos completos del cliente
    /// 3. Retorna el mensaje generado sin persistirlo (lo persiste /send)
    /// </summary>
    [HttpPost("contacts/{contactId:guid}/generate-message")]
    public async Task<IActionResult> GenerateMessage(
        Guid contactId,
        [FromBody] GenerateMessageRequest req,
        CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized(new { error = "X-N8N-Key inválida." });

        if (string.IsNullOrWhiteSpace(req.PromptSnapshot))
            return BadRequest(new { error = "El prompt no puede estar vacío." });

        // ── Construir contexto de variables desde ContactDataJson ───────────
        var context = BuildContext(req.ContactDataJson);

        // ── Resolver {{variables}} en el prompt ────────────────────────────
        var resolvedPrompt = ResolveVariables(req.PromptSnapshot, context);

        // ── Llamar al LLM (Anthropic Claude) ──────────────────────────────
        var anthropicKey = cfg["Anthropic:ApiKey"];
        if (string.IsNullOrEmpty(anthropicKey) || anthropicKey == "YOUR_ANTHROPIC_API_KEY")
            return StatusCode(503, new { error = "Anthropic API key no configurada." });

        var client = new AnthropicClient(anthropicKey);

        // User message: todos los datos del cliente para que Claude los use
        var userMessageText = BuildUserMessage(req.ContactDataJson, context);

        var messages = new List<Anthropic.SDK.Messaging.Message>
        {
            new() { Role = RoleType.User, Content = [new TextContent { Text = userMessageText }] }
        };

        var response = await client.Messages.GetClaudeMessageAsync(
            new MessageParameters
            {
                Model      = "claude-haiku-4-5-20251001", // Haiku = rápido y económico para campañas
                MaxTokens  = 512,
                System     = [new SystemMessage(resolvedPrompt)],
                Messages   = messages,
                Stream     = false,
                Temperature = new decimal(0.2), // bajo para mensajes controlados
            }, ct);

        var generated = response.Content.OfType<TextContent>()
                                        .FirstOrDefault()?.Text ?? "";

        return Ok(new
        {
            message         = generated.Trim(),
            resolvedPrompt,
            variablesUsed   = context.Keys.ToList(),
        });
    }

    // ── 3. Send ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Envía el mensaje generado al cliente via UltraMsg y actualiza el estado
    /// del contacto y escribe el log de auditoría.
    /// </summary>
    [HttpPost("contacts/{contactId:guid}/send")]
    public async Task<IActionResult> Send(
        Guid contactId,
        [FromBody] N8nSendMessageRequest req,
        CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized(new { error = "X-N8N-Key inválida." });

        var contact = await db.CampaignContacts
            .Include(c => c.Campaign).ThenInclude(c => c.Tenant)
            .FirstOrDefaultAsync(c => c.Id == contactId, ct);

        if (contact is null) return NotFound(new { error = "Contacto no encontrado." });
        if (contact.DispatchStatus != DispatchStatus.Claimed)
            return Conflict(new { error = $"El contacto no está en estado Claimed (actual: {contact.DispatchStatus})." });

        var tenant = contact.Campaign.Tenant;
        var sw = Stopwatch.StartNew();

        // ── Llamar a UltraMsg ───────────────────────────────────────────────
        string? externalMessageId = null;
        string? ultraMsgResponse  = null;
        string  dispatchStatus    = "Sent";
        string? errorDetail       = null;

        try
        {
            var httpClient = httpClientFactory.CreateClient();
            var instanceId = tenant.WhatsAppInstanceId ?? req.InstanceId;
            var token      = tenant.WhatsAppApiToken   ?? req.Token;

            if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(token))
                throw new InvalidOperationException("Tenant sin configuración de UltraMsg (instanceId o token vacíos).");

            var payload = new
            {
                token,
                to      = contact.PhoneNumber,
                body    = req.Message,
                priority = 1,
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content     = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
            var normalizedInstanceId = instanceId.StartsWith("instance", StringComparison.OrdinalIgnoreCase)
                ? instanceId : $"instance{instanceId}";
            var url         = $"https://api.ultramsg.com/{normalizedInstanceId}/messages/chat";

            var httpResp = await httpClient.PostAsync(url, content, ct);
            ultraMsgResponse = await httpResp.Content.ReadAsStringAsync(ct);

            if (httpResp.IsSuccessStatusCode)
            {
                // Extraer ID de mensaje de la respuesta de UltraMsg
                try
                {
                    var parsed = JsonSerializer.Deserialize<JsonElement>(ultraMsgResponse);
                    externalMessageId = parsed.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                }
                catch { /* ignorar si no parsea */ }
            }
            else
            {
                dispatchStatus = httpResp.StatusCode == System.Net.HttpStatusCode.BadRequest
                    ? "Skipped"   // número inválido — no reintentar
                    : "Error";
                errorDetail = $"HTTP {(int)httpResp.StatusCode}: {ultraMsgResponse}";
            }
        }
        catch (Exception ex)
        {
            dispatchStatus = "Error";
            errorDetail    = ex.Message;
        }

        sw.Stop();

        // ── Actualizar contacto ──────────────────────────────────────────────
        contact.DispatchStatus    = dispatchStatus switch
        {
            "Sent"    => DispatchStatus.Sent,
            "Skipped" => DispatchStatus.Skipped,
            _         => contact.DispatchAttempts >= 3 ? DispatchStatus.Error : DispatchStatus.Retry,
        };
        contact.SentAt            = dispatchStatus == "Sent" ? DateTime.UtcNow : null;
        contact.GeneratedMessage  = req.Message;
        contact.ExternalMessageId = externalMessageId;
        contact.DispatchError     = errorDetail;

        // ── Log de auditoría ────────────────────────────────────────────────
        db.CampaignDispatchLogs.Add(new CampaignDispatchLog
        {
            Id                  = Guid.NewGuid(),
            CampaignId          = contact.CampaignId,
            CampaignContactId   = contact.Id,
            TenantId            = tenant.Id,
            AttemptNumber       = contact.DispatchAttempts,
            PromptSnapshot      = req.PromptSnapshot,
            ContactDataSnapshot = contact.ContactDataJson,
            GeneratedMessage    = req.Message,
            PhoneNumber         = contact.PhoneNumber,
            UltraMsgResponse    = ultraMsgResponse,
            ExternalMessageId   = externalMessageId,
            Status              = dispatchStatus,
            ErrorDetail         = errorDetail,
            DurationMs          = (int)sw.ElapsedMilliseconds,
            OccurredAt          = DateTime.UtcNow,
        });

        // Actualizar contador de la campaña
        if (dispatchStatus == "Sent")
        {
            var camp = contact.Campaign;
            camp.ProcessedContacts++;
        }

        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            success           = dispatchStatus == "Sent",
            dispatchStatus,
            externalMessageId,
            durationMs        = sw.ElapsedMilliseconds,
            error             = errorDetail,
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Construye un diccionario de variables a partir del ContactDataJson.
    /// Aplana todos los registros: si el mismo campo aparece en varios registros
    /// se toma el primer valor no vacío.
    /// </summary>
    private static Dictionary<string, string> BuildContext(string? contactDataJson)
    {
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(contactDataJson)) return context;

        try
        {
            var registros = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(contactDataJson);
            if (registros is null) return context;

            foreach (var registro in registros)
                foreach (var (key, val) in registro)
                {
                    var strVal = val.ValueKind == JsonValueKind.String
                        ? val.GetString() ?? ""
                        : val.ToString();
                    context.TryAdd(key, strVal);
                }

            // Meta-variables
            context["__TotalRegistros__"] = registros.Count.ToString();
        }
        catch { /* JSON inválido → contexto vacío */ }

        return context;
    }

    /// <summary>
    /// Sustituye {{Variable}} en el texto por el valor del contexto.
    /// Si la variable no existe en el contexto se deja sin cambios.
    /// </summary>
    private static string ResolveVariables(string template, Dictionary<string, string> context)
        => Regex.Replace(template, @"\{\{(\w+)\}\}", m =>
            context.TryGetValue(m.Groups[1].Value, out var val) ? val : m.Value);

    /// <summary>
    /// Construye el mensaje de usuario para el LLM con todos los datos del cliente.
    /// Para contactos con múltiples registros (múltiples pólizas) se lista cada uno.
    /// </summary>
    private static string BuildUserMessage(string? contactDataJson, Dictionary<string, string> context)
    {
        if (string.IsNullOrWhiteSpace(contactDataJson))
            return "Redacta el mensaje para el cliente.";

        try
        {
            var registros = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(contactDataJson);
            if (registros is null || registros.Count == 0)
                return "Redacta el mensaje para el cliente.";

            if (registros.Count == 1)
            {
                var lines = registros[0].Select(kv => $"- {kv.Key}: {kv.Value}");
                return $"Redacta el mensaje para el cliente con los siguientes datos:\n{string.Join('\n', lines)}";
            }

            // Múltiples registros (varias pólizas)
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Redacta el mensaje para el cliente. Tiene {registros.Count} registros asociados:");
            for (var i = 0; i < registros.Count; i++)
            {
                sb.AppendLine($"\nRegistro {i + 1}:");
                foreach (var (k, v) in registros[i])
                    sb.AppendLine($"  - {k}: {v}");
            }
            return sb.ToString();
        }
        catch
        {
            return "Redacta el mensaje para el cliente.";
        }
    }
}

// ── Request records ───────────────────────────────────────────────────────────

public record GenerateMessageRequest(
    string PromptSnapshot,
    string? ContactDataJson
);

public record N8nSendMessageRequest(
    string Message,
    string? PromptSnapshot,
    // Fallback si el tenant no tiene credenciales en BD (solo para testing)
    string? InstanceId = null,
    string? Token = null
);
