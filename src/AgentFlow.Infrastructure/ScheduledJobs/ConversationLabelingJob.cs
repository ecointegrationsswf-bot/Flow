using System.Text;
using System.Text.Json;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Domain.Webhooks;
using AgentFlow.Infrastructure.Persistence;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Executor del job LABEL_CONVERSATIONS. Diseñado para correr como un job Cron
/// global (típicamente cada hora) — internamente verifica qué CampaignTemplates
/// del sistema tienen LabelingJobHourUtc igual a la hora UTC actual, y para
/// cada uno clasifica las conversaciones cerradas pendientes de etiquetar.
///
/// Responsabilidad ÚNICA: asignar etiqueta. NO envía webhooks de resultado.
/// El envío al cliente se modela como una ActionDefinition + ScheduledWebhookJob
/// con TriggerEvent=ConversationLabeled, que se configura desde el admin en
/// /admin/scheduled-jobs y reusa todo el Webhook Contract System.
///
/// Flujo por conversación:
///   1. Carga historial completo (Inbound + Outbound).
///   2. Construye prompt fijo + lista de etiquetas del maestro.
///   3. Llama a Claude (claude-sonnet-4-6) con max_tokens=300, temperature=0.
///   4. Parsea {labelName, confidence, reasoning, extractedDate}.
///   5. Persiste LabelId + LabeledAt en la conversación.
///   6. Dispara evento "ConversationLabeled" — el Worker programará cualquier
///      job que tenga ese trigger configurado por el admin.
///
/// Idempotencia: solo procesa conversaciones con LabelId NULL.
/// </summary>
public class ConversationLabelingJob(
    AgentFlowDbContext db,
    AnthropicClient anthropic,
    IWebhookEventDispatcher eventDispatcher,
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
            .Select(t => new { t.Id, t.TenantId, t.Name, t.LabelIds })
            .ToListAsync(ct);

        if (templates.Count == 0)
            return JobRunResult.Skipped($"Sin maestros configurados para hora UTC={nowHourUtc}.");

        var totalProcessed = 0;
        var totalLabeled = 0;
        var totalFailed = 0;

        foreach (var tpl in templates)
        {
            if (ct.IsCancellationRequested) break;

            // Resolver labels del tenant que el maestro tiene asignadas.
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
                    return await ProcessConversationAsync(
                        convId, tpl.TenantId,
                        labels.Select(l => (l.Id, l.Name, l.Keywords)).ToList(), ct);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Labeling: error procesando conversación {Conv}.", convId);
                    return false;
                }
                finally { sem.Release(); }
            });

            var results = await Task.WhenAll(tasks);
            foreach (var labeled in results)
            {
                totalProcessed++;
                if (labeled) totalLabeled++; else totalFailed++;
            }
        }

        var summary = $"Procesadas={totalProcessed} · Etiquetadas={totalLabeled} · Fallos={totalFailed}";
        log.LogInformation("LabelingJob completo: {Summary}", summary);

        if (totalProcessed == 0) return JobRunResult.Skipped("Sin conversaciones pendientes.");
        if (totalFailed == 0) return JobRunResult.Success(totalProcessed, summary);
        if (totalLabeled == 0) return JobRunResult.Failed("Todas las clasificaciones fallaron.", summary);
        return JobRunResult.Partial(totalProcessed, totalLabeled, totalFailed, summary);
    }

    /// <summary>
    /// Etiqueta UNA conversación. Devuelve true si quedó etiquetada, false si falló.
    /// Tras etiquetar exitosamente, dispara el evento ConversationLabeled.
    /// </summary>
    private async Task<bool> ProcessConversationAsync(
        Guid conversationId, Guid tenantId,
        List<(Guid Id, string Name, List<string> Keywords)> labels,
        CancellationToken ct)
    {
        var conv = await db.Conversations
            .Include(c => c.Messages.OrderBy(m => m.SentAt))
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conv is null) return false;

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

        var userPrompt = BuildClassifierUserPrompt(labels, history);

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
            return false;
        }

        if (parsed is null)
        {
            log.LogWarning("Labeling: respuesta no parseable para conv {Conv}.", conversationId);
            return false;
        }

        // Resolver el LabelId por Name (case-insensitive).
        var matched = labels.FirstOrDefault(l =>
            string.Equals(l.Name, parsed.LabelName, StringComparison.OrdinalIgnoreCase));
        if (matched.Id == Guid.Empty)
        {
            log.LogWarning("Labeling: Claude devolvió '{Label}' que no está en el catálogo de conv {Conv}.",
                parsed.LabelName, conversationId);
            return false;
        }

        conv.LabelId = matched.Id;
        conv.LabeledAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Labeling: conv {Conv} → '{Label}' (conf={Conf}).",
            conversationId, matched.Name, parsed.Confidence);

        // Disparar evento — cualquier ScheduledWebhookJob configurado con
        // TriggerEvent=ConversationLabeled se programará en el siguiente tick
        // (típicamente el webhook de resultado al cliente).
        try { await eventDispatcher.DispatchAsync("ConversationLabeled", conversationId.ToString(), tenantId, ct); }
        catch (Exception ex) { log.LogWarning(ex, "Labeling: dispatch de ConversationLabeled falló para conv {Conv}.", conversationId); }

        return true;
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
