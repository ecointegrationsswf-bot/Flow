using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Domain.Webhooks;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Executor batch del slug NOTIFY_GESTION. Diseñado para correr en cron a una
/// hora fija (ej: 22:00 Panamá), una vez por día. En cada corrida itera todas
/// las conversaciones etiquetadas con resultJson disponible que hayan sido
/// (re)etiquetadas DESPUÉS del último run del job, y dispara un webhook por
/// cada una usando el InputSchema/OutputSchema del ActionDefinition global
/// NOTIFY_GESTION (configurado por el admin en el Webhook Builder).
///
/// Idempotencia: filtramos por <c>Conversation.LabeledAt &gt; job.LastRunAt</c>
/// — si el cron corre cada día y ninguna conv fue re-etiquetada, no se reenvía
/// nada. Si una conv fue re-etiquetada (LastActivityAt &gt; LabeledAt fue
/// detectado por el LabelingJob y refrescó LabeledAt) la siguiente corrida
/// vuelve a enviar.
///
/// Scope esperado: AllTenants. El executor itera todos los tenants y solo
/// procesa los que tengan conversaciones candidatas.
/// </summary>
public class NotifyGestionBatchExecutor(
    AgentFlowDbContext db,
    IActionExecutorService actionExecutor,
    JobExecutionAuditor auditor,
    AgentFlow.Domain.Interfaces.ITeamsNotifier teamsNotifier,
    ILogger<NotifyGestionBatchExecutor> log) : IScheduledJobExecutor
{
    public string Slug => "NOTIFY_GESTION";

    // Umbral para alertar a Teams (por tenant, al cierre del run):
    // dispara con cualquiera de los dos — porcentaje O conteo absoluto.
    private const double FailureRateThreshold = 0.20;   // 20%
    private const int FailureAbsoluteThreshold = 10;

    public async Task<JobRunResult> ExecuteAsync(
        ScheduledWebhookJob job, ScheduledJobContext ctx, CancellationToken ct)
    {
        // Cutoff: solo procesar conversaciones etiquetadas/re-etiquetadas tras la última corrida
        // del job. Si nunca corrió, procesamos las labeladas en los últimos 30 días.
        // Nota: la idempotencia fina se hace per-KeyValue dentro del loop, no aquí.
        // Aquí solo filtramos por "etiquetada después del cutoff" para no procesar
        // conversaciones viejas innecesariamente.
        var cutoff = job.LastRunAt ?? DateTime.UtcNow.AddDays(-30);

        var pending = await db.Conversations
            .AsNoTracking()
            .Where(c => c.LabelId != null
                        && c.LabelingResultJson != null
                        && c.LabeledAt != null
                        && c.LabeledAt > cutoff)
            .Select(c => new
            {
                c.Id,
                c.TenantId,
                c.ClientPhone,
                c.CampaignId,
                c.LabeledAt,
            })
            .ToListAsync(ct);

        if (pending.Count == 0)
            return JobRunResult.Skipped($"Sin conversaciones etiquetadas tras {cutoff:O}.");

        log.LogInformation("NotifyGestionBatch: {N} conversaciones candidatas (cutoff={Cutoff:O}).",
            pending.Count, cutoff);

        // Resolver CampaignTemplateId en una sola query (necesario para el ActionExecutor).
        var campaignIds = pending.Where(p => p.CampaignId.HasValue).Select(p => p.CampaignId!.Value).Distinct().ToList();
        var templateByCampaign = await db.Campaigns
            .Where(c => campaignIds.Contains(c.Id))
            .Select(c => new { c.Id, c.CampaignTemplateId })
            .ToDictionaryAsync(c => c.Id, c => c.CampaignTemplateId, ct);

        var sent = 0;
        var failed = 0;
        var errorMessages = new List<string>();

        // Stats POR TENANT para alertar a Teams al cierre del run cuando un
        // tenant supere el umbral (>=20% fallidos o >=10 fallos absolutos).
        // Errores se agrupan por mensaje para mostrar el top 3 más frecuente.
        var statsByTenant = new Dictionary<Guid, (int Sent, int Failed, Dictionary<string, int> Errors)>();
        void Bump(Guid tenantId, bool isSuccess, string? errMsg)
        {
            if (!statsByTenant.TryGetValue(tenantId, out var s))
                s = (0, 0, new Dictionary<string, int>(StringComparer.Ordinal));
            if (isSuccess) s.Sent++;
            else
            {
                s.Failed++;
                var key = string.IsNullOrWhiteSpace(errMsg) ? "(sin detalle)"
                        : errMsg.Length > 160 ? errMsg[..160] : errMsg;
                s.Errors[key] = s.Errors.GetValueOrDefault(key) + 1;
            }
            statsByTenant[tenantId] = s;
        }

        // Resolver nombre del cliente para mostrar en la UI cuando falla.
        var convIds = pending.Select(p => p.Id).ToList();
        var convNames = await db.Conversations
            .Where(c => convIds.Contains(c.Id))
            .Select(c => new { c.Id, c.ClientName, c.ClientPhone })
            .ToDictionaryAsync(c => c.Id, c => c.ClientName ?? c.ClientPhone, ct);

        // Nombre del tenant para el mensaje de Teams.
        var tenantIds = pending.Select(p => p.TenantId).Distinct().ToList();
        var tenantNames = await db.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        // Por cada conversación, iteramos los records del CampaignContact y disparamos
        // un webhook por cada KeyValue. Idempotencia per-KeyValue: si ya hubo un
        // dispatch Success para ese (ConversationId, KeyValue) DESPUÉS del LabeledAt
        // actual de la conv, ese KeyValue se skipea. Si la conv se re-etiqueta (LabeledAt
        // avanza), los Success previos quedan obsoletos (StartedAt < nuevo LabeledAt)
        // y todos los KeyValues vuelven a enviarse.
        foreach (var p in pending)
        {
            if (ct.IsCancellationRequested) break;

            var campaignTemplateId = p.CampaignId.HasValue && templateByCampaign.TryGetValue(p.CampaignId.Value, out var tplId)
                ? (Guid?)tplId
                : null;

            var convLabel = convNames.TryGetValue(p.Id, out var lbl) ? lbl : p.ClientPhone;

            // ── Resolver records del CampaignContact ───────────────────────────
            var records = await LoadContactRecordsAsync(p.CampaignId, p.ClientPhone, ct);
            if (records.Count == 0)
            {
                log.LogWarning("NotifyGestionBatch: conv {Conv} sin records en CampaignContact — skip.", p.Id);
                continue;
            }

            // ── KeyValues ya enviados con éxito desde el último LabeledAt ──────
            var alreadySent = await GetAlreadySentKeyValuesAsync(p.Id, p.LabeledAt!.Value, ct);

            foreach (var record in records)
            {
                if (ct.IsCancellationRequested) break;

                // Premisa estricta: cada record tiene su KeyValue. Si falta, skip
                // defensivo y log de advertencia (no debería pasar — se valida en
                // FixedFormatCampaignService y MorosidadController al crear la
                // campaña/download).
                var keyValue = record.TryGetValue("KeyValue", out var kvElem)
                    ? GetStringValue(kvElem)
                    : null;
                if (string.IsNullOrWhiteSpace(keyValue))
                {
                    log.LogWarning("NotifyGestionBatch: conv {Conv} tiene un record sin KeyValue — skip ese record.", p.Id);
                    continue;
                }

                // Idempotencia: si este (Conv, KeyValue) ya tiene un Success desde
                // el LabeledAt actual, no re-enviamos.
                if (alreadySent.Contains(keyValue))
                {
                    log.LogDebug("NotifyGestionBatch: conv {Conv} KeyValue {Kv} ya enviado, skip.", p.Id, keyValue);
                    continue;
                }

                // Override: pisamos contact.{field} con los valores del record actual
                // así el InputSchema con sourceKey="contact.KeyValue" (y otros campos
                // del record) resuelve al valor correcto de ESTA póliza, no a la del
                // primer record que el SystemContextBuilder aplana por defecto.
                var overrides = BuildContactOverrides(record);

                try
                {
                    var result = await actionExecutor.ExecuteAsync(
                        actionSlug: Slug,
                        tenantId: p.TenantId,
                        campaignTemplateId: campaignTemplateId,
                        contactPhone: p.ClientPhone,
                        conversationId: p.Id,
                        collectedParams: new CollectedParams(),
                        agentSlug: null,
                        jobExecutionId: ctx.ExecutionId,
                        jobId: job.Id,
                        systemContextOverrides: overrides,
                        ct: ct);

                    if (result.Success)
                    {
                        sent++;
                        Bump(p.TenantId, isSuccess: true, errMsg: null);
                        alreadySent.Add(keyValue); // evitar reenvíos en el mismo run
                    }
                    else
                    {
                        failed++;
                        var errMsg = result.ErrorMessage ?? "sin detalle";
                        Bump(p.TenantId, isSuccess: false, errMsg);
                        if (errorMessages.Count < 5) errorMessages.Add($"conv {p.Id} kv {keyValue}: {errMsg}");
                        log.LogWarning("NotifyGestionBatch falló conv {Conv} kv {Kv}: {Err}", p.Id, keyValue, errMsg);
                        auditor.RecordFailure(
                            ctx.ExecutionId, p.TenantId,
                            JobExecutionAuditor.ContextTypes.Conversation,
                            $"{p.Id}|{keyValue}",
                            $"{convLabel} (KV={keyValue})",
                            errMsg);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Bump(p.TenantId, isSuccess: false, ex.Message);
                    if (errorMessages.Count < 5) errorMessages.Add($"conv {p.Id} kv {keyValue}: {ex.Message}");
                    log.LogError(ex, "NotifyGestionBatch: excepción conv {Conv} kv {Kv}.", p.Id, keyValue);
                    auditor.RecordFailure(
                        ctx.ExecutionId, p.TenantId,
                        JobExecutionAuditor.ContextTypes.Conversation,
                        $"{p.Id}|{keyValue}",
                        $"{convLabel} (KV={keyValue})",
                        ex.Message);
                }
            }
        }

        await auditor.FlushAsync(ct);

        // ── Alerta a Teams por tenant cuando supera el umbral ───────────────
        // Dispara si: >= FailureAbsoluteThreshold fallos O >= FailureRateThreshold%
        // del total procesado para ese tenant. Best-effort: si Teams falla, NO
        // afecta el resultado del job.
        foreach (var kv in statsByTenant)
        {
            var tenantId = kv.Key;
            var (tSent, tFailed, tErrors) = kv.Value;
            var tTotal = tSent + tFailed;
            if (tTotal == 0 || tFailed == 0) continue;
            var rate = (double)tFailed / tTotal;
            if (tFailed < FailureAbsoluteThreshold && rate < FailureRateThreshold) continue;

            var tenantName = tenantNames.TryGetValue(tenantId, out var n) ? n : tenantId.ToString();
            var top3 = tErrors
                .OrderByDescending(e => e.Value)
                .Take(3)
                .Select(e => $"   • ({e.Value}x) {e.Key}")
                .ToList();

            var msg =
                $"⚠️ NOTIFY_GESTION con fallos altos\n" +
                $"• Tenant: {tenantName}\n" +
                $"• Procesados: {tTotal} · Exitosos: {tSent} · Fallidos: {tFailed} ({rate:P0})\n" +
                $"• Umbral: ≥{FailureAbsoluteThreshold} fallos O ≥{FailureRateThreshold:P0}\n" +
                (top3.Count > 0 ? $"• Top errores:\n{string.Join("\n", top3)}\n" : "") +
                $"• Acción: revisar el contrato/credenciales del tenant en el panel admin → Webhook Builder. " +
                $"Detalle por item en Scheduled Jobs → Historial.";

            try { await teamsNotifier.NotifyAsync(msg, ct); }
            catch (Exception ex) { log.LogWarning(ex, "[NotifyGestionBatch] Teams notify falló para tenant {Tenant}.", tenantName); }
        }

        var summary = $"Total={pending.Count} · Enviadas={sent} · Fallos={failed}";
        if (errorMessages.Count > 0) summary += " · Errores: " + string.Join(" | ", errorMessages);
        if (summary.Length > 800) summary = summary[..800];

        log.LogInformation("NotifyGestionBatch completo: {Summary}", summary);

        if (sent == 0 && failed == 0) return JobRunResult.Skipped(summary);
        if (failed == 0) return JobRunResult.Success(sent, summary);
        if (sent == 0) return JobRunResult.Failed("Todos los envíos fallaron.", summary);
        return JobRunResult.Partial(sent + failed, sent, failed, summary);
    }

    /// <summary>
    /// Carga el ContactDataJson del CampaignContact y lo deserializa como
    /// lista de records (1 por póliza). Devuelve lista vacía si no hay
    /// CampaignContact, no hay JSON, o el JSON es inválido.
    /// </summary>
    private async Task<List<Dictionary<string, System.Text.Json.JsonElement>>>
        LoadContactRecordsAsync(Guid? campaignId, string contactPhone, CancellationToken ct)
    {
        if (!campaignId.HasValue) return [];

        var json = await db.CampaignContacts
            .AsNoTracking()
            .Where(cc => cc.CampaignId == campaignId.Value && cc.PhoneNumber == contactPhone)
            .Select(cc => cc.ContactDataJson)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return System.Text.Json.JsonSerializer
                .Deserialize<List<Dictionary<string, System.Text.Json.JsonElement>>>(json) ?? [];
        }
        catch (Exception ex)
        {
            log.LogWarning("[NotifyGestion] ContactDataJson inválido (campaign {C}, phone {P}): {Err}",
                campaignId, contactPhone, ex.Message);
            return [];
        }
    }

    /// <summary>
    /// Devuelve el set de KeyValues ya enviados con éxito para esta conversación
    /// desde el LabeledAt actual. Si la conv se re-etiqueta (LabeledAt avanza),
    /// los Success viejos quedan automáticamente fuera del filtro StartedAt >=
    /// labeledAt y se permite re-envío.
    /// </summary>
    private async Task<HashSet<string>> GetAlreadySentKeyValuesAsync(
        Guid conversationId, DateTime labeledAt, CancellationToken ct)
    {
        var payloads = await db.WebhookDispatchLogs
            .AsNoTracking()
            .Where(l => l.ConversationId == conversationId
                        && l.ActionSlug == Slug
                        && l.Status == "Success"
                        && l.StartedAt >= labeledAt)
            .Select(l => l.RequestPayloadJson)
            .ToListAsync(ct);

        var sent = new HashSet<string>(StringComparer.Ordinal);
        foreach (var json in payloads)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("KeyValue", out var kv))
                {
                    var s = GetStringValue(kv);
                    if (!string.IsNullOrEmpty(s)) sent.Add(s);
                }
            }
            catch { /* payload corrupto — ignorar */ }
        }
        return sent;
    }

    /// <summary>
    /// Construye el dictionary de overrides para el SystemContext con todos los
    /// campos del record actual prefijados como "contact.{key}". Sobrescribe los
    /// valores del primer record que el SystemContextBuilder aplana por defecto,
    /// asegurando que cada llamada (cada póliza) tenga sus propios datos.
    /// </summary>
    private static Dictionary<string, string?> BuildContactOverrides(
        Dictionary<string, System.Text.Json.JsonElement> record)
    {
        var ov = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, val) in record)
        {
            var s = GetStringValue(val);
            if (!string.IsNullOrEmpty(s))
                ov[$"contact.{key}"] = s;
        }
        return ov;
    }

    private static string GetStringValue(System.Text.Json.JsonElement el) => el.ValueKind switch
    {
        System.Text.Json.JsonValueKind.String => el.GetString() ?? "",
        System.Text.Json.JsonValueKind.Null => "",
        _ => el.ToString()
    };
}
