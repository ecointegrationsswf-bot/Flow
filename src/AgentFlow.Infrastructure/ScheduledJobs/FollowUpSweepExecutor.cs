using System.Text.Json;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Channels.MetaCloudApi;
using AgentFlow.Infrastructure.Channels.UltraMsg;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Sweeper global de follow-ups. UN solo job cron en la tabla
/// (Cron */5 * * * *, Scope=AllTenants) que en cada tick escanea TODOS los
/// CampaignContacts pendientes de follow-up y dispara los que correspondan.
///
/// Reemplaza el modelo anterior "1 job DelayFromEvent por contacto × índice",
/// que provocaba que la tabla ScheduledWebhookJobs creciera N×M con cada
/// campaña. Acá la tabla queda con UN job sin importar cuántos contactos.
///
/// Reglas (preservadas del FollowUpExecutor original):
/// - Solo se procesa si la conversación está en WaitingClient
///   (cliente NO ha respondido).
/// - Solo dentro del horario laboral del tenant (Tenant.TimeZone +
///   CampaignTemplate.SendFrom/SendUntil con override por campaña, AttentionDays).
/// - Idempotencia: el índice se persiste en CampaignContact.FollowUpsSentJson
///   apenas se envía. Si el sweeper se atasca y se repite, no manda dos veces.
/// - Tope por tick: 200 envíos para evitar bloquear el Worker.
/// </summary>
public class FollowUpSweepExecutor(
    AgentFlowDbContext db,
    IChannelProviderFactory providerFactory,
    IUltraMsgInstanceService ultraMsgInstance,
    IMetaCloudApiHealthService metaHealth,
    IBusinessHoursClock businessHours,
    JobExecutionAuditor auditor,
    ILogger<FollowUpSweepExecutor> log) : IScheduledJobExecutor
{
    public string Slug => "FOLLOW_UP_SWEEP";

    // Tope global de envíos por tick (anti-runaway). Con 6 msg/min × 5 min = 30
    // por tenant en condiciones normales; este tope solo protege en escenarios
    // patológicos (ej: tenant con MessagesPerMinute mal configurado en 1000).
    private const int MaxPerTick = 200;

    // Tope inferior de delay anti-ban — independiente de la config del tenant.
    private const int MinDelaySecondsFloor = 3;
    private const int ConsecutiveErrorThreshold = 3;
    private static readonly Random _rng = new();

    public async Task<JobRunResult> ExecuteAsync(
        ScheduledWebhookJob job, ScheduledJobContext ctx, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;

        // Candidatos: contactos enviados que aún no hayan completado todos los
        // follow-ups del template.
        var candidates = await db.CampaignContacts
            .Include(c => c.Campaign).ThenInclude(c => c.Tenant)
            .Where(c => c.DispatchStatus == DispatchStatus.Sent
                     && c.SentAt != null
                     && c.Campaign.IsActive)
            .OrderBy(c => c.SentAt)
            .Take(MaxPerTick)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return JobRunResult.Skipped("No hay contactos pendientes de follow-up.");

        var templateIds = candidates.Where(c => c.Campaign.CampaignTemplateId.HasValue)
                                    .Select(c => c.Campaign.CampaignTemplateId!.Value)
                                    .Distinct().ToList();
        var templates = await db.CampaignTemplates
            .Where(t => templateIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);

        // Agrupamos por tenant — cada tenant tiene su propio rate limit
        // (CampaignMessagesPerMinute, CampaignMaxPerHour, CampaignMaxPerDay).
        // El sweeper respeta los MISMOS topes que el dispatcher inicial para
        // proteger del ban de UltraMsg.
        var byTenant = candidates.GroupBy(c => c.Campaign.TenantId).ToList();

        var totalSent = 0;
        var totalSkipped = 0;
        var totalFailed = 0;
        var totalDeferred = 0;

        foreach (var group in byTenant)
        {
            if (ct.IsCancellationRequested) break;
            var (s, sk, f, d) = await ProcessTenantAsync(group.Key, group.ToList(), templates, nowUtc, ctx, ct);
            totalSent += s;
            totalSkipped += sk;
            totalFailed += f;
            totalDeferred += d;
        }

        await auditor.FlushAsync(ct);

        var summary = $"Sent={totalSent} | Skipped={totalSkipped} | Deferred={totalDeferred} | Failed={totalFailed} | Scanned={candidates.Count}";
        log.LogInformation("[FollowUpSweep] {Summary}", summary);

        if (totalSent == 0 && totalFailed == 0)
            return JobRunResult.Skipped(summary);
        if (totalFailed > 0 && totalSent == 0)
            return JobRunResult.Failed($"Todos los envíos fallaron — {summary}");
        return JobRunResult.Success(totalSent, summary);
    }

    /// <summary>
    /// Procesa los contactos de UN tenant respetando su rate limit individual.
    /// Anti-ban: delay entre envíos = 60s/MessagesPerMinute con jitter ±20%,
    /// con piso absoluto de 3s. Tope diario y por hora se evalúan antes de
    /// enviar cada mensaje.
    /// </summary>
    private async Task<(int sent, int skipped, int failed, int deferred)> ProcessTenantAsync(
        Guid tenantId,
        List<CampaignContact> contacts,
        IReadOnlyDictionary<Guid, CampaignTemplate> templates,
        DateTime nowUtc,
        ScheduledJobContext ctx,
        CancellationToken ct)
    {
        if (contacts.Count == 0) return (0, 0, 0, 0);
        var tenant = contacts[0].Campaign.Tenant;

        // Pre-check de salud de la línea — SIMÉTRICO al dispatcher inicial.
        // Si la línea WhatsApp está caída/desvinculada (IsActive=true pero
        // LastStatus != "authenticated"), NO enviamos NADA a UltraMsg: diferimos
        // TODOS los follow-ups de este tenant al próximo tick. Esto evita encolar
        // mensajes en una instancia caída que, al reconectar, los dispararía en
        // ráfaga (incidente SOMOS 18/05 → riesgo de ban de Meta).
        var (lineHealthy, healthReason) = await EnsureTenantLineHealthyAsync(tenantId, ct);
        if (!lineHealthy)
        {
            log.LogWarning(
                "[FollowUpSweep] Tenant {Id}: línea no disponible — {Count} follow-up(s) diferido(s). {Reason}",
                tenantId, contacts.Count, healthReason);
            return (0, 0, 0, contacts.Count); // todos diferidos: ni enviados ni fallidos
        }

        // Validar tope DIARIO antes de empezar — si ya se llegó, no procesamos
        // ningún contacto de este tenant en este tick.
        var startOfDayUtc = DateTime.UtcNow.Date;
        var sentToday = await db.Messages
            .Where(m => m.SentAt >= startOfDayUtc
                     && m.Direction == MessageDirection.Outbound
                     && m.Conversation!.TenantId == tenantId)
            .CountAsync(ct);
        var maxPerDay = Math.Max(1, tenant.CampaignMaxPerDay);
        if (sentToday >= maxPerDay)
        {
            log.LogInformation("[FollowUpSweep] Tenant {Id}: tope diario alcanzado ({Sent}/{Max}).",
                tenantId, sentToday, maxPerDay);
            return (0, contacts.Count, 0, 0);
        }

        // Tope HORARIO — cuántos van en la última hora.
        var oneHourAgo = nowUtc.AddHours(-1);
        var sentLastHour = await db.Messages
            .Where(m => m.SentAt >= oneHourAgo
                     && m.Direction == MessageDirection.Outbound
                     && m.Conversation!.TenantId == tenantId)
            .CountAsync(ct);
        var maxPerHour = Math.Max(1, tenant.CampaignMaxPerHour);

        // Delay base — igual que el dispatcher inicial.
        var messagesPerMinute = Math.Max(1, tenant.CampaignMessagesPerMinute);
        var baseDelaySeconds = Math.Max(MinDelaySecondsFloor, 60.0 / messagesPerMinute);

        var sent = 0;
        var skipped = 0;
        var failed = 0;
        var deferred = 0;
        var consecutiveErrors = 0;

        foreach (var contact in contacts)
        {
            if (ct.IsCancellationRequested) break;

            // Re-evaluar topes en cada iteración (otros procesos pueden estar
            // enviando a la vez al mismo tenant via dispatcher inicial).
            if (sentToday + sent >= maxPerDay)
            {
                log.LogInformation("[FollowUpSweep] Tenant {Id}: tope diario alcanzado durante el tick.", tenantId);
                break;
            }
            if (sentLastHour + sent >= maxPerHour)
            {
                log.LogInformation("[FollowUpSweep] Tenant {Id}: tope horario alcanzado.", tenantId);
                break;
            }

            try
            {
                var (result, _, error) = await TryDispatchOneAsync(contact, templates, nowUtc, ct);
                switch (result)
                {
                    case DispatchOutcome.Sent:
                        sent++;
                        consecutiveErrors = 0;
                        // Delay anti-ban con jitter ±20% — solo entre envíos exitosos.
                        var jitter = (_rng.NextDouble() * 0.4) - 0.2;
                        var delay = Math.Max(MinDelaySecondsFloor, baseDelaySeconds * (1 + jitter));
                        await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                        break;
                    case DispatchOutcome.Skipped:  skipped++;  break;
                    case DispatchOutcome.Deferred: deferred++; break;
                    case DispatchOutcome.Failed:
                        failed++;
                        consecutiveErrors++;
                        auditor.RecordFailure(
                            ctx.ExecutionId, tenantId,
                            JobExecutionAuditor.ContextTypes.Contact,
                            contact.Id.ToString(),
                            contact.ClientName ?? contact.PhoneNumber,
                            error ?? "Error de envío sin detalle");
                        if (consecutiveErrors >= ConsecutiveErrorThreshold)
                        {
                            log.LogWarning("[FollowUpSweep] Tenant {Id}: {N}+ errores consecutivos — frenando.",
                                tenantId, ConsecutiveErrorThreshold);
                            return (sent, skipped, failed, deferred);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                failed++;
                log.LogError(ex, "[FollowUpSweep] Error procesando contacto {Id}", contact.Id);
                auditor.RecordFailure(
                    ctx.ExecutionId, tenantId,
                    JobExecutionAuditor.ContextTypes.Contact,
                    contact.Id.ToString(),
                    contact.ClientName ?? contact.PhoneNumber,
                    ex.Message);
            }
        }
        return (sent, skipped, failed, deferred);
    }

    /// <summary>
    /// Pre-check de salud de la línea WhatsApp del tenant, simétrico al
    /// EnsureWhatsAppLineHealthyAsync del CampaignDispatcherService.
    ///
    /// Verifica la PRIMERA línea activa (la misma que usa el ChannelProviderFactory
    /// para enviar — OrderBy CreatedAt). Solo se hace ping a UltraMsg; MetaCloudApi
    /// se asume sano (su API responde 4xx cuando la cuenta está mal, no necesita
    /// pre-check). Política conservadora fail-closed: si el ping falla por timeout
    /// o red, devolvemos NO-sano para no arriesgar bombardear una línea caída.
    ///
    /// Persistir LastStatus también alimenta al monitor diario: si detectamos la
    /// caída en un tick, el badge de la UI ya lo refleja sin esperar al ping de las 6 AM.
    /// </summary>
    private async Task<(bool Healthy, string? Reason)> EnsureTenantLineHealthyAsync(
        Guid tenantId, CancellationToken ct)
    {
        var line = await db.Set<WhatsAppLine>()
            .Where(l => l.TenantId == tenantId && l.IsActive)
            .OrderBy(l => l.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (line is null)
            return (false, "Sin línea WhatsApp activa para el tenant.");

        // Pre-check de salud por proveedor (igual que el dispatcher de campañas).
        //   • UltraMsg  → /instance/status.
        //   • Meta      → Graph API health_status (normalizado a "authenticated").
        // Diferir un tick de follow-ups si la línea está caída evita encolar fallos
        // que, al reconectar, dispararían en ráfaga (incidente SOMOS 18/05).
        string status;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            status = line.Provider == ProviderType.MetaCloudApi
                ? (await metaHealth.GetHealthAsync(line.InstanceId, line.MetaAccessToken ?? "", null, cts.Token)).Status
                : (await ultraMsgInstance.GetStatusAsync(line.InstanceId, line.ApiToken, cts.Token)).Status;
        }
        catch (Exception ex)
        {
            // Timeout o error de red — NO podemos confirmar que la línea esté sana.
            // Política conservadora: fail-closed. Mejor diferir el tick que arriesgar
            // bombardear el proveedor si la línea está caída y solo no respondió a tiempo.
            log.LogWarning(ex, "[FollowUpSweep][LineHealth] Ping de salud falló para tenant {TenantId} ({Provider}).",
                tenantId, line.Provider);
            line.ConsecutivePingFailures++;
            line.LastStatusCheckedAt = DateTime.UtcNow;
            try { await db.SaveChangesAsync(ct); } catch { }
            return (false, $"No se pudo verificar el estado de la línea: {ex.Message}");
        }

        // Persistir el resultado del ping (igual que hace el job diario).
        line.LastStatus = status;
        line.LastStatusCheckedAt = DateTime.UtcNow;
        if (string.Equals(status, "authenticated", StringComparison.OrdinalIgnoreCase))
            line.ConsecutivePingFailures = 0;
        else
            line.ConsecutivePingFailures++;
        try { await db.SaveChangesAsync(ct); } catch { }

        if (!string.Equals(status, "authenticated", StringComparison.OrdinalIgnoreCase))
            return (false, $"Línea '{line.DisplayName}' en estado '{status}' (no authenticated).");

        return (true, null);
    }

    private enum DispatchOutcome { Sent, Skipped, Deferred, Failed }

    private async Task<(DispatchOutcome Result, int Count, string? Error)> TryDispatchOneAsync(
        CampaignContact contact,
        IReadOnlyDictionary<Guid, CampaignTemplate> templates,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (contact.Campaign.CampaignTemplateId is null) return (DispatchOutcome.Skipped, 0, null);
        if (!templates.TryGetValue(contact.Campaign.CampaignTemplateId.Value, out var template))
            return (DispatchOutcome.Skipped, 0, null);

        if (template.FollowUpHours.Count == 0) return (DispatchOutcome.Skipped, 0, null);
        if (string.IsNullOrEmpty(template.FollowUpMessagesJson)) return (DispatchOutcome.Skipped, 0, null);

        var sentIndices = ParseIndices(contact.FollowUpsSentJson);

        // Encontrar el primer índice DUE no enviado.
        int? dueIndex = null;
        for (var i = 0; i < template.FollowUpHours.Count; i++)
        {
            if (sentIndices.Contains(i)) continue;
            var dueAt = contact.SentAt!.Value.AddHours(template.FollowUpHours[i]);
            if (nowUtc < dueAt) break; // los siguientes índices están aún más en el futuro
            dueIndex = i;
            break;
        }

        if (dueIndex is null) return (DispatchOutcome.Skipped, 0, null);

        // Horario laboral del tenant — si fuera de hora, no enviamos pero
        // tampoco perdemos el job: el próximo tick (5 min después) reevalúa.
        if (!businessHours.IsWithinBusinessHours(nowUtc, contact.Campaign.Tenant, template))
            return (DispatchOutcome.Deferred, 0, null);

        // Solo seguimos si la conversación está esperando al cliente.
        var conv = await db.Conversations
            .Where(c => c.TenantId == contact.Campaign.TenantId
                        && c.ClientPhone == contact.PhoneNumber
                        && c.CampaignId == contact.CampaignId)
            .OrderByDescending(c => c.LastActivityAt)
            .FirstOrDefaultAsync(ct);

        if (conv is null) return (DispatchOutcome.Skipped, 0, null);
        if (conv.Status != ConversationStatus.WaitingClient) return (DispatchOutcome.Skipped, 0, null);

        // Resolver mensaje
        List<string>? messages;
        try { messages = JsonSerializer.Deserialize<List<string>>(template.FollowUpMessagesJson); }
        catch { return (DispatchOutcome.Skipped, 0, null); }
        if (messages is null || dueIndex.Value >= messages.Count
            || string.IsNullOrWhiteSpace(messages[dueIndex.Value]))
            return (DispatchOutcome.Skipped, 0, null);

        var resolved = ResolveVariables(messages[dueIndex.Value], contact);

        // Enviar
        var provider = await providerFactory.GetProviderAsync(contact.Campaign.TenantId, ct);
        if (provider is null) return (DispatchOutcome.Failed, 0, "Tenant sin provider WhatsApp activo");

        var sendResult = await provider.SendMessageAsync(
            new SendMessageRequest(contact.PhoneNumber, resolved), ct);
        if (!sendResult.Success) return (DispatchOutcome.Failed, 0, sendResult.Error ?? "Provider WhatsApp respondió error sin detalle");

        // Persistir Message + actualizar FollowUpsSentJson + LastActivityAt.
        // CRÍTICO: marcar el índice ANTES de SaveChanges para idempotencia frente
        // a fallos parciales — si el SaveChanges falla, el cliente recibió el
        // mensaje pero no se marcó. En el próximo tick volvería a mandarse.
        // Para minimizar la ventana, hacemos UN solo SaveChanges con todo.
        sentIndices.Add(dueIndex.Value);
        contact.FollowUpsSentJson = JsonSerializer.Serialize(sentIndices);
        conv.LastActivityAt = DateTime.UtcNow;
        db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conv.Id,
            Direction = MessageDirection.Outbound,
            Status = MessageStatus.Sent,
            Content = resolved,
            ExternalMessageId = sendResult.ExternalMessageId,
            IsFromAgent = true,
            AgentName = "FollowUp",
            SentAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        log.LogInformation("[FollowUpSweep] Enviado idx={Idx} a {Phone} (campaign {Campaign}).",
            dueIndex.Value, contact.PhoneNumber, contact.CampaignId);

        return (DispatchOutcome.Sent, 1, null);
    }

    private static List<int> ParseIndices(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<int>();
        try { return JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>(); }
        catch { return new List<int>(); }
    }

    private static string ResolveVariables(string template, CampaignContact c)
        => template
            .Replace("{nombre}", c.ClientName ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{poliza}", c.PolicyNumber ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{aseguradora}", c.InsuranceCompany ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{monto_pendiente}", c.PendingAmount?.ToString("N2") ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{telefono}", c.PhoneNumber, StringComparison.OrdinalIgnoreCase);
}
