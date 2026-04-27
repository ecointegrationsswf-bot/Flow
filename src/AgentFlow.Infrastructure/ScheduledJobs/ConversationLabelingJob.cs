using System.Text;
using System.Text.Json;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Domain.Webhooks;
using AgentFlow.Infrastructure.Persistence;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using InputSchema = AgentFlow.Domain.Webhooks.InputSchema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Executor del job LABEL_CONVERSATIONS. Diseñado para correr como un job Cron
/// global (típicamente cada hora) — internamente verifica qué CampaignTemplates
/// del sistema tienen LabelingJobHourUtc igual a la hora UTC actual, y para
/// cada uno clasifica las conversaciones cerradas pendientes de etiquetar.
///
/// Flujo por conversación:
///   1. Carga historial completo (Inbound + Outbound).
///   2. Construye prompt fijo + lista de etiquetas del maestro.
///   3. Llama a Claude (claude-sonnet-4-6) con max_tokens=200, temperature=0.
///   4. Parsea la respuesta JSON {labelName, confidence, reasoning, extractedDate}.
///   5. Persiste LabelId + LabeledAt en la conversación.
///   6. Si el maestro tiene ResultWebhookUrl configurado, dispara el webhook
///      reusando PayloadBuilder + HttpDispatcher del Webhook Contract System.
///
/// Idempotencia: solo procesa conversaciones con LabelId NULL. Si el webhook
/// ya se envió (ResultWebhookSentAt no nulo), no se reintenta automáticamente.
/// </summary>
public class ConversationLabelingJob(
    AgentFlowDbContext db,
    AnthropicClient anthropic,
    ISystemContextBuilder contextBuilder,
    IPayloadBuilder payloadBuilder,
    IHttpDispatcher httpDispatcher,
    ILogger<ConversationLabelingJob> log) : IScheduledJobExecutor
{
    public string Slug => "LABEL_CONVERSATIONS";

    private const string ClassifierModel = "claude-sonnet-4-6";
    private const int ClassifierMaxTokens = 300;
    private const decimal ClassifierTemperature = 0.0m;
    private const int MaxConversationsPerRun = 200;
    private const int MaxParallel = 10;

    private const string SystemPromptText = """
Eres un clasificador de conversaciones de atención al cliente.
Tu única tarea es leer el historial COMPLETO de una conversación y asignarle
la etiqueta que mejor describe su resultado.

REGLAS:
1. Analiza el historial completo, prestando atención a la última intención del cliente.
2. Elige EXACTAMENTE UNA etiqueta de la lista proporcionada (campo "labelName").
3. Usa las palabras clave de cada etiqueta como guía semántica, no como match literal.
4. El campo "confidence" debe ser un decimal entre 0.0 y 1.0 (1.0 = certeza total).
5. El campo "reasoning" debe ser una sola frase clara, en español, sin citar mensajes literales.
6. Si la conversación menciona una fecha futura (compromiso de pago, cita, vencimiento)
   inclúyela en "extractedDate" en formato ISO YYYY-MM-DD. Si no hay fecha, usa null.
7. Responde ÚNICAMENTE con JSON válido, sin markdown ni texto adicional.

Formato exacto:
{"labelName":"...","confidence":0.0,"reasoning":"...","extractedDate":null}
""";

    public async Task<JobRunResult> ExecuteAsync(
        ScheduledWebhookJob job, ScheduledJobContext ctx, CancellationToken ct)
    {
        var nowHourUtc = DateTime.UtcNow.Hour;

        // 1. Maestros que tienen el job programado para esta hora UTC.
        var templates = await db.CampaignTemplates
            .AsNoTracking()
            .Where(t => t.LabelingJobHourUtc != null && t.LabelingJobHourUtc == nowHourUtc)
            .Select(t => new { t.Id, t.TenantId, t.Name, t.LabelIds, t.ResultWebhookUrl, t.ResultOutputSchema })
            .ToListAsync(ct);

        if (templates.Count == 0)
            return JobRunResult.Skipped($"Sin maestros configurados para hora UTC={nowHourUtc}.");

        var totalProcessed = 0;
        var totalLabeled = 0;
        var totalFailed = 0;
        var totalWebhookOk = 0;
        var totalWebhookFail = 0;

        foreach (var tpl in templates)
        {
            if (ct.IsCancellationRequested) break;

            // Resolver labels disponibles del maestro (intersección con ConversationLabels del tenant).
            var labels = await db.ConversationLabels
                .Where(l => l.TenantId == tpl.TenantId && l.IsActive && tpl.LabelIds.Contains(l.Id))
                .Select(l => new { l.Id, l.Name, l.Keywords })
                .ToListAsync(ct);

            if (labels.Count == 0)
            {
                log.LogInformation("Labeling: maestro {Tpl} sin labels asignadas — skip.", tpl.Id);
                continue;
            }

            // Conversaciones cerradas, sin etiquetar, de campañas que usan este maestro.
            var pending = await db.Conversations
                .Where(c => c.TenantId == tpl.TenantId
                            && c.Status == ConversationStatus.Closed
                            && c.LabelId == null
                            && c.CampaignId != null
                            && db.Campaigns.Any(camp => camp.Id == c.CampaignId && camp.CampaignTemplateId == tpl.Id))
                .OrderBy(c => c.ClosedAt)
                .Take(MaxConversationsPerRun)
                .Select(c => c.Id)
                .ToListAsync(ct);

            if (pending.Count == 0) continue;
            log.LogInformation("Labeling: {N} conversaciones para maestro {Tpl}.", pending.Count, tpl.Id);

            // Procesar con paralelismo controlado.
            using var sem = new SemaphoreSlim(MaxParallel);
            var tasks = pending.Select(async convId =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    var (labeled, webhookSent, webhookOk) = await ProcessConversationAsync(
                        convId, tpl.Id, tpl.TenantId, tpl.ResultWebhookUrl, tpl.ResultOutputSchema,
                        labels.Select(l => (l.Id, l.Name, l.Keywords)).ToList(), ct);
                    return (labeled, webhookSent, webhookOk);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Labeling: error procesando conversación {Conv}.", convId);
                    return (false, false, false);
                }
                finally { sem.Release(); }
            });

            var results = await Task.WhenAll(tasks);
            foreach (var (labeled, sent, ok) in results)
            {
                totalProcessed++;
                if (labeled) totalLabeled++; else totalFailed++;
                if (sent) { if (ok) totalWebhookOk++; else totalWebhookFail++; }
            }
        }

        var summary = $"Procesadas={totalProcessed} · Etiquetadas={totalLabeled} · Fallos={totalFailed} · Webhooks OK={totalWebhookOk}/{totalWebhookOk + totalWebhookFail}";
        log.LogInformation("LabelingJob completo: {Summary}", summary);

        if (totalProcessed == 0) return JobRunResult.Skipped("Sin conversaciones pendientes.");
        if (totalFailed == 0) return JobRunResult.Success(totalProcessed, summary);
        if (totalLabeled == 0) return JobRunResult.Failed("Todas las clasificaciones fallaron.", summary);
        return JobRunResult.Partial(totalProcessed, totalLabeled, totalFailed, summary);
    }

    /// <summary>
    /// Devuelve (labeled, webhookSent, webhookOk).
    /// </summary>
    private async Task<(bool, bool, bool)> ProcessConversationAsync(
        Guid conversationId, Guid templateId, Guid tenantId,
        string? resultWebhookUrl, string? resultOutputSchema,
        List<(Guid Id, string Name, List<string> Keywords)> labels,
        CancellationToken ct)
    {
        // 1. Cargar historial.
        var conv = await db.Conversations
            .Include(c => c.Messages.OrderBy(m => m.SentAt))
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conv is null) return (false, false, false);

        var history = string.Join("\n",
            conv.Messages.Select(m =>
            {
                var role = m.Direction == MessageDirection.Inbound ? "Cliente" :
                           (m.AgentName ?? "Agente");
                var ts = m.SentAt.ToString("HH:mm");
                var content = m.Content ?? "";
                if (content.Length > 600) content = content[..600] + "...";
                return $"[{ts}] {role}: {content}";
            }));

        // 2. Construir user prompt con etiquetas y historial.
        var userPrompt = BuildClassifierUserPrompt(labels, history);

        // 3. Llamar a Claude.
        LabelingResult? parsed;
        try
        {
            var response = await anthropic.Messages.GetClaudeMessageAsync(new MessageParameters
            {
                Model = ClassifierModel,
                MaxTokens = ClassifierMaxTokens,
                Temperature = ClassifierTemperature,
                System = [new SystemMessage(SystemPromptText)],
                Messages = [new Anthropic.SDK.Messaging.Message
                {
                    Role = RoleType.User,
                    Content = [new TextContent { Text = userPrompt }]
                }],
                Stream = false,
            }, ct);

            var raw = response.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? "";
            parsed = TryParseLabelingResult(raw);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Labeling: Claude llamada falló para conv {Conv}.", conversationId);
            return (false, false, false);
        }

        if (parsed is null)
        {
            log.LogWarning("Labeling: respuesta no parseable para conv {Conv}.", conversationId);
            return (false, false, false);
        }

        // 4. Resolver el LabelId por Name (case-insensitive).
        var matched = labels.FirstOrDefault(l =>
            string.Equals(l.Name, parsed.LabelName, StringComparison.OrdinalIgnoreCase));
        if (matched.Id == Guid.Empty)
        {
            log.LogWarning("Labeling: Claude devolvió '{Label}' que no está en el catálogo de conv {Conv}.",
                parsed.LabelName, conversationId);
            return (false, false, false);
        }

        // 5. Persistir etiqueta.
        conv.LabelId = matched.Id;
        conv.LabeledAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Labeling: conv {Conv} → '{Label}' (conf={Conf}).",
            conversationId, matched.Name, parsed.Confidence);

        // 6. Disparar webhook resultado si está configurado.
        if (string.IsNullOrEmpty(resultWebhookUrl))
            return (true, false, false);

        // Skip si ya se envió antes (idempotencia básica).
        if (conv.ResultWebhookSentAt is not null)
            return (true, true, conv.ResultWebhookStatus is >= 200 and < 300);

        var sentOk = await SendResultWebhookAsync(
            conv.Id, resultWebhookUrl, resultOutputSchema, parsed, ct);

        return (true, true, sentOk);
    }

    private async Task<bool> SendResultWebhookAsync(
        Guid conversationId, string url, string? schemaJson,
        LabelingResult parsed, CancellationToken ct)
    {
        try
        {
            // Construir contexto enriquecido con todos los sourceKeys del resultado.
            var sysCtx = await contextBuilder.BuildResultContextAsync(conversationId, ct);
            sysCtx.Set("conversation.label.confidence", parsed.Confidence.ToString("F2"));
            sysCtx.Set("conversation.label.reasoning", parsed.Reasoning);
            if (!string.IsNullOrEmpty(parsed.ExtractedDate))
                sysCtx.Set("conversation.label.extractedDate", parsed.ExtractedDate);

            // Si no hay schema, enviamos un payload mínimo razonable. Si lo hay, lo respetamos.
            object payload;
            if (string.IsNullOrEmpty(schemaJson))
            {
                payload = new
                {
                    conversationId = conversationId.ToString(),
                    label = parsed.LabelName,
                    confidence = parsed.Confidence,
                    reasoning = parsed.Reasoning,
                    extractedDate = parsed.ExtractedDate,
                    closedAt = sysCtx.Get("conversation.closedAt"),
                    contactPhone = sysCtx.Get("contact.phone"),
                };
            }
            else
            {
                var schema = JsonSerializer.Deserialize<InputSchema>(schemaJson);
                if (schema is null) return false;
                payload = payloadBuilder.Build(schema, new CollectedParams(), sysCtx);
            }

            var endpoint = new WebhookEndpointConfig
            {
                WebhookUrl = url,
                WebhookMethod = "POST",
                AuthType = "None",
            };

            var dispatch = await httpDispatcher.SendAsync(endpoint, payload, "application/json", ct);

            // Persistir el resultado del envío en la conversación (independiente de éxito).
            await db.Conversations
                .Where(c => c.Id == conversationId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.ResultWebhookSentAt, DateTime.UtcNow)
                    .SetProperty(c => c.ResultWebhookStatus, dispatch.StatusCode), ct);

            return dispatch.StatusCode is >= 200 and < 300;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Labeling: webhook resultado falló para conv {Conv}.", conversationId);
            await db.Conversations
                .Where(c => c.Id == conversationId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.ResultWebhookSentAt, DateTime.UtcNow)
                    .SetProperty(c => c.ResultWebhookStatus, 0), ct);
            return false;
        }
    }

    private static string BuildClassifierUserPrompt(
        List<(Guid Id, string Name, List<string> Keywords)> labels, string history)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## ETIQUETAS DISPONIBLES");
        foreach (var l in labels)
        {
            sb.AppendLine($"- Nombre: {l.Name}");
            if (l.Keywords is { Count: > 0 })
                sb.AppendLine($"  Palabras clave: {string.Join(", ", l.Keywords)}");
        }
        sb.AppendLine();
        sb.AppendLine("## HISTORIAL DE LA CONVERSACIÓN");
        sb.AppendLine(history);
        sb.AppendLine();
        sb.AppendLine("Devuelve únicamente el JSON. Recuerda: \"labelName\" debe coincidir EXACTAMENTE con el Nombre de una etiqueta de la lista.");
        return sb.ToString();
    }

    private static LabelingResult? TryParseLabelingResult(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Si Claude envuelve el JSON en markdown, extraer el bloque.
        var jsonStart = raw.IndexOf('{');
        var jsonEnd = raw.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd <= jsonStart) return null;
        var jsonText = raw[jsonStart..(jsonEnd + 1)];

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            var name = root.TryGetProperty("labelName", out var n) ? n.GetString() : null;
            var conf = root.TryGetProperty("confidence", out var c) ? c.GetDouble() : 0.0;
            var reason = root.TryGetProperty("reasoning", out var r) ? r.GetString() : null;
            var date = root.TryGetProperty("extractedDate", out var d) && d.ValueKind != JsonValueKind.Null
                ? d.GetString() : null;
            if (string.IsNullOrEmpty(name)) return null;
            return new LabelingResult(name, conf, reason, date);
        }
        catch { return null; }
    }
}
