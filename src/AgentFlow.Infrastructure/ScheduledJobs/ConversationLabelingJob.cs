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
/// Executor del job LABEL_CONVERSATIONS. Se invoca por un ScheduledWebhookJob
/// Cron configurado en /admin/scheduled-jobs (la expresión cron del job es el
/// horario; típicamente "0 23 * * *" para 11pm UTC). Cuando el Worker dispara
/// el job, este executor recorre TODOS los maestros del sistema que tengan al
/// menos una etiqueta asignada y clasifica las conversaciones candidatas.
///
/// Criterio de candidatura (independiente del Status de la conversación):
///   1. Sin etiqueta              → LabelId IS NULL
///   2. Con etiqueta pero el cliente escribió después → LastActivityAt > LabeledAt
/// Las que ya están etiquetadas y NO tuvieron actividad nueva quedan como están.
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
    AgentFlow.Infrastructure.AI.AnthropicSettings anthropicSettings,
    IWebhookEventDispatcher eventDispatcher,
    ILogger<ConversationLabelingJob> log) : IScheduledJobExecutor
{
    public string Slug => "LABEL_CONVERSATIONS";

    private const string ClassifierModel = "claude-sonnet-4-6";
    private const int ClassifierMaxTokens = 300;
    private const decimal ClassifierTemperature = 0.0m;
    private const int MaxConversationsPerRun = 200;

    private const string DefaultSystemPromptText = """
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
        // Recorremos TODOS los maestros activos con al menos una etiqueta asignada.
        // El horario lo controla la expresión cron del ScheduledWebhookJob que
        // dispara este executor — no un campo del maestro.
        // (LabelIds es JSON serializado, así que filtramos en memoria tras cargar.)
        var allTemplates = await db.CampaignTemplates
            .AsNoTracking()
            .Where(t => t.IsActive)
            .Select(t => new { t.Id, t.TenantId, t.Name, t.LabelIds })
            .ToListAsync(ct);

        var templates = allTemplates.Where(t => t.LabelIds.Count > 0).ToList();
        if (templates.Count == 0)
            return JobRunResult.Skipped("Sin maestros con etiquetas asignadas.");

        var totalProcessed = 0;
        var totalLabeled = 0;
        var totalFailed = 0;
        // Acumulamos hasta los primeros 5 mensajes de error para que aparezcan
        // en el summary del execution (visible en /admin/scheduled-jobs sin tocar logs).
        var errorMessages = new List<string>();

        // Cache por tenant: AnthropicClient + prompts configurables (analysis + result schema).
        // Si LabelingAnalysisPrompt es null usamos DefaultSystemPromptText.
        // Si LabelingResultSchemaPrompt no es null pedimos a Claude un campo "result" extra
        // que se persiste en Conversation.LabelingResultJson para mapear webhooks.
        var clientByTenant = new Dictionary<Guid, AnthropicClient?>();
        var promptsByTenant = new Dictionary<Guid, (string AnalysisPrompt, string? ResultSchema)>();

        foreach (var tpl in templates)
        {
            if (ct.IsCancellationRequested) break;

            // Aislamiento por template/tenant: si algo falla aquí (resolver config,
            // query de labels/pending, etc.), saltamos al siguiente template para
            // que el error de un tenant no detenga la ejecución de los demás.
            try
            {
                // Resolver client + prompts del tenant (1 query por tenant).
                if (!clientByTenant.TryGetValue(tpl.TenantId, out var tenantClient))
                {
                    var (client, analysisPrompt, resultSchema) = await ResolveTenantConfigAsync(tpl.TenantId, ct);
                    clientByTenant[tpl.TenantId] = client;
                    promptsByTenant[tpl.TenantId] = (analysisPrompt, resultSchema);
                    tenantClient = client;
                }
                if (tenantClient is null)
                {
                    log.LogWarning("Labeling: tenant {Tenant} sin AnthropicClient disponible — maestro {Tpl} omitido.",
                        tpl.TenantId, tpl.Id);
                    continue;
                }
                var (tenantAnalysisPrompt, tenantResultSchema) = promptsByTenant[tpl.TenantId];

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

                // Conversaciones candidatas a etiquetar/re-etiquetar:
                // - LabelId IS NULL (nunca etiquetadas) o
                // - LastActivityAt > LabeledAt (el cliente escribió tras el último etiquetado).
                // El Status NO se filtra.
                var pending = await db.Conversations
                    .Where(c => c.TenantId == tpl.TenantId
                                && c.CampaignId != null
                                && (c.LabelId == null
                                    || (c.LabeledAt != null && c.LastActivityAt > c.LabeledAt))
                                && db.Campaigns.Any(camp => camp.Id == c.CampaignId && camp.CampaignTemplateId == tpl.Id))
                    .OrderBy(c => c.LastActivityAt)
                    .Take(MaxConversationsPerRun)
                    .Select(c => c.Id)
                    .ToListAsync(ct);

                if (pending.Count == 0) continue;
                log.LogInformation("Labeling: {N} conversaciones para maestro {Tpl}.", pending.Count, tpl.Id);

                // Procesamiento SECUENCIAL — el DbContext es scoped al job y no
                // soporta queries concurrentes. Cada Claude call tarda ~1-3s, así
                // que para 200 conversaciones es 5-10 min, aceptable para un job nocturno.
                var labelTuples = labels.Select(l => (l.Id, l.Name, l.Keywords)).ToList();
                foreach (var convId in pending)
                {
                    if (ct.IsCancellationRequested) break;
                    bool labeled;
                    try
                    {
                        labeled = await ProcessConversationAsync(
                            tenantClient, convId, tpl.TenantId, labelTuples,
                            tenantAnalysisPrompt, tenantResultSchema, ct);
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Labeling: error procesando conversación {Conv}.", convId);
                        if (errorMessages.Count < 5)
                            errorMessages.Add($"conv {convId}: {ex.Message}");
                        labeled = false;
                    }
                    totalProcessed++;
                    if (labeled) totalLabeled++; else totalFailed++;
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Labeling: error procesando maestro {Tpl} (tenant {Tenant}) — continuando con los demás.",
                    tpl.Id, tpl.TenantId);
                if (errorMessages.Count < 5)
                    errorMessages.Add($"maestro {tpl.Id}: {ex.Message}");
                totalFailed++;
            }
        }

        var summary = $"Procesadas={totalProcessed} · Etiquetadas={totalLabeled} · Fallos={totalFailed}";
        if (errorMessages.Count > 0)
            summary += " · " + string.Join(" | ", errorMessages);
        if (summary.Length > 800) summary = summary[..800];
        log.LogInformation("LabelingJob completo: {Summary}", summary);

        if (totalProcessed == 0) return JobRunResult.Skipped("Sin conversaciones pendientes.");
        if (totalFailed == 0) return JobRunResult.Success(totalProcessed, summary);
        if (totalLabeled == 0) return JobRunResult.Failed("Todas las clasificaciones fallaron.", summary);
        return JobRunResult.Partial(totalProcessed, totalLabeled, totalFailed, summary);
    }

    /// <summary>
    /// Resuelve client + prompts del tenant en una sola query. Devuelve client
    /// nulo si no hay LlmApiKey del tenant ni global. AnalysisPrompt nunca es null
    /// (cae al default). ResultSchema es null cuando el tenant no configuró schema.
    /// </summary>
    private async Task<(AnthropicClient? Client, string AnalysisPrompt, string? ResultSchema)>
        ResolveTenantConfigAsync(Guid tenantId, CancellationToken ct)
    {
        var cfg = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new
            {
                t.LlmApiKey,
                t.LabelingAnalysisPrompt,
                t.LabelingResultSchemaPrompt
            })
            .FirstOrDefaultAsync(ct);

        AnthropicClient? client;
        if (!string.IsNullOrEmpty(cfg?.LlmApiKey))
            client = new AnthropicClient(cfg.LlmApiKey);
        else if (anthropicSettings.HasGlobalKey)
            client = anthropic;
        else
            client = null;

        var analysis = string.IsNullOrWhiteSpace(cfg?.LabelingAnalysisPrompt)
            ? DefaultSystemPromptText
            : cfg!.LabelingAnalysisPrompt!;
        var resultSchema = string.IsNullOrWhiteSpace(cfg?.LabelingResultSchemaPrompt)
            ? null
            : cfg!.LabelingResultSchemaPrompt;

        return (client, analysis, resultSchema);
    }

    /// <summary>
    /// Etiqueta UNA conversación. Devuelve true si quedó etiquetada, false si falló.
    /// Tras etiquetar exitosamente, dispara el evento ConversationLabeled.
    /// </summary>
    private async Task<bool> ProcessConversationAsync(
        AnthropicClient client,
        Guid conversationId, Guid tenantId,
        List<(Guid Id, string Name, List<string> Keywords)> labels,
        string analysisPrompt,
        string? resultSchemaPrompt,
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

        var userPrompt = BuildClassifierUserPrompt(labels, history, resultSchemaPrompt);
        // Si el tenant pide schema custom, ampliar maxTokens — el JSON puede ser más largo.
        var maxTokens = resultSchemaPrompt is null ? ClassifierMaxTokens : ClassifierMaxTokens + 700;

        LabelingResult? parsed;
        try
        {
            var response = await client.Messages.GetClaudeMessageAsync(new MessageParameters
            {
                Model = ClassifierModel,
                MaxTokens = maxTokens,
                Temperature = ClassifierTemperature,
                System = [new SystemMessage(analysisPrompt)],
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
        // Persistir resultJson custom si el tenant configuró schema y Claude lo devolvió.
        if (!string.IsNullOrEmpty(parsed.ResultJson))
            conv.LabelingResultJson = parsed.ResultJson;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Labeling: conv {Conv} → '{Label}' (conf={Conf}, hasResult={HasResult}).",
            conversationId, matched.Name, parsed.Confidence, parsed.ResultJson is not null);

        // Disparar evento — cualquier ScheduledWebhookJob configurado con
        // TriggerEvent=ConversationLabeled se programará en el siguiente tick
        // (típicamente el webhook de resultado al cliente).
        try { await eventDispatcher.DispatchAsync("ConversationLabeled", conversationId.ToString(), tenantId, ct); }
        catch (Exception ex) { log.LogWarning(ex, "Labeling: dispatch de ConversationLabeled falló para conv {Conv}.", conversationId); }

        return true;
    }

    private static string BuildClassifierUserPrompt(
        List<(Guid Id, string Name, List<string> Keywords)> labels, string history,
        string? resultSchemaPrompt)
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

        if (!string.IsNullOrWhiteSpace(resultSchemaPrompt))
        {
            sb.AppendLine("## SCHEMA DE RESULTADO ADICIONAL");
            sb.AppendLine("Además del label, extrae un objeto JSON con la siguiente estructura.");
            sb.AppendLine("Si no hay datos para un campo: usa string vacío para textos, 0 para números y null para fechas no encontradas.");
            sb.AppendLine();
            sb.AppendLine(resultSchemaPrompt);
            sb.AppendLine();
            sb.AppendLine("Devuelve ÚNICAMENTE el siguiente JSON, sin markdown ni texto adicional:");
            sb.AppendLine("{\"labelName\":\"...\",\"confidence\":0.0,\"reasoning\":\"...\",\"extractedDate\":null,\"result\":{...campos del schema...}}");
        }
        else
        {
            sb.AppendLine("Devuelve únicamente el JSON. Recuerda: \"labelName\" debe coincidir EXACTAMENTE con el Nombre de una etiqueta de la lista.");
        }
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
            // El campo "result" es opcional — solo lo serializamos si vino como objeto.
            string? resultJson = null;
            if (root.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.Object)
                resultJson = res.GetRawText();
            if (string.IsNullOrEmpty(name)) return null;
            return new LabelingResult(name, conf, reason, date, resultJson);
        }
        catch { return null; }
    }
}
