using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Campaigns;

/// <summary>
/// Servicio que envía los mensajes de una campaña de forma controlada.
///
/// Reglas anti-ban (todas parametrizadas por Tenant a partir de Fase 3 v2):
/// - Delay base entre mensajes = 60 / Tenant.CampaignMessagesPerMinute, con jitter ±20%
/// - Tope por lote = Tenant.CampaignMaxPerHour
/// - Tope diario por tenant = Tenant.CampaignMaxPerDay
/// - Solo envía dentro del horario de oficina del tenant (default 8am-5pm Panamá)
/// - Si el provider responde 3+ errores consecutivos, frena el lote
///
/// El servicio NO programa los envíos — solo ejecuta un lote.
/// El CampaignWorker (BackgroundService) se encarga de programar cuándo corre.
///
/// Garantías de aislamiento (lo invoca el CampaignWorker):
/// - El Worker procesa tenants en paralelo, pero las campañas DE UN TENANT son
///   secuenciales (FIFO LaunchedAt). Eso impide que dos campañas del mismo número
///   compitan por el rate limit y dupliquen el throughput observado por Meta.
/// - El claim atómico (Queued→Claimed con ExecuteUpdate filtrado por DispatchStatus)
///   garantiza que dos instancias del Worker NUNCA tomen el mismo CampaignContact.
/// </summary>
public class CampaignDispatcherService(
    AgentFlowDbContext db,
    IChannelProviderFactory providerFactory,
    IWebhookEventDispatcher eventDispatcher,
    IScheduledJobRepository scheduledJobs,
    ILogger<CampaignDispatcherService> logger)
{
    // ── Mínimos de seguridad — tope inferior aunque la config diga lo contrario ──
    private const int MinDelaySecondsFloor = 3;     // jamás bajamos de 3s entre mensajes
    private const int ConsecutiveErrorThreshold = 3; // 3 errores seguidos → frena

    private static readonly Random _random = new();

    /// <summary>
    /// Procesa un lote de envíos para una campaña.
    /// Retorna cuántos mensajes se enviaron exitosamente.
    ///
    /// El método se detiene cuando:
    /// - Se acabaron los contactos pendientes
    /// - Se alcanzó el límite por hora (MaxPerHour)
    /// - Se detectó un error de UltraMsg (posible ban)
    /// - Se salió del horario de oficina
    /// </summary>
    public async Task<DispatchResult> DispatchBatchAsync(Guid campaignId, CancellationToken ct = default)
    {
        // ── 1. Cargar campaña con su tenant ──────────────
        var campaign = await db.Campaigns
            .Include(c => c.Tenant)
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct);

        if (campaign is null)
            return new DispatchResult(0, 0, "Campaña no encontrada", DispatchStopReason.Error);

        if (!campaign.IsActive)
            return new DispatchResult(0, 0, "Campaña inactiva", DispatchStopReason.CampaignInactive);

        var tenant = campaign.Tenant;
        if (tenant is null)
            return new DispatchResult(0, 0, "Tenant no encontrado", DispatchStopReason.Error);

        // Off-switch del tenant (no relanza, no envía).
        if (!tenant.CampaignDispatchEnabled)
        {
            logger.LogInformation("Campaña {CampaignId}: dispatch deshabilitado para tenant {TenantId}.",
                campaignId, tenant.Id);
            return new DispatchResult(0, 0, "Dispatch deshabilitado por tenant", DispatchStopReason.CampaignInactive);
        }

        // ── 2. Verificar horario de oficina ──────────────
        if (!IsWithinBusinessHours(tenant))
        {
            logger.LogInformation(
                "Campaña {CampaignId}: fuera de horario ({Start}-{End} {TZ}). Será reprogramada.",
                campaignId, tenant.BusinessHoursStart, tenant.BusinessHoursEnd, tenant.TimeZone);
            return new DispatchResult(0, 0, "Fuera de horario de oficina", DispatchStopReason.OutsideBusinessHours);
        }

        // ── 3. Obtener provider del tenant ───────────────
        var provider = await providerFactory.GetProviderAsync(tenant.Id, ct);
        if (provider is null)
        {
            logger.LogWarning("Campaña {CampaignId}: tenant sin línea WhatsApp activa.", campaignId);
            return new DispatchResult(0, 0, "Sin línea WhatsApp activa", DispatchStopReason.NoProvider);
        }

        // Configuración del tenant (rate limit, topes) con safe-floor sobre el delay.
        var maxPerHour = Math.Max(1, tenant.CampaignMaxPerHour);
        var maxPerDay = Math.Max(1, tenant.CampaignMaxPerDay);
        var messagesPerMinute = Math.Max(1, tenant.CampaignMessagesPerMinute);
        var baseDelaySeconds = Math.Max(MinDelaySecondsFloor, 60.0 / messagesPerMinute);

        // ── 4. Contar envíos del día por tenant (para límite diario) ─
        var todayUtc = DateTime.UtcNow.Date;
        var sentToday = await (
            from cc in db.CampaignContacts
            join c in db.Campaigns on cc.CampaignId equals c.Id
            where c.TenantId == tenant.Id
               && cc.SentAt != null
               && cc.SentAt >= todayUtc
            select cc.Id
        ).CountAsync(ct);

        if (sentToday >= maxPerDay)
        {
            logger.LogInformation("Campaña {CampaignId}: límite diario alcanzado ({Sent}/{Max}).",
                campaignId, sentToday, maxPerDay);
            return new DispatchResult(0, sentToday, "Límite diario alcanzado", DispatchStopReason.DailyLimitReached);
        }

        // ── 5. Claim atómico Queued → Claimed (anti double-send entre Workers concurrentes) ─
        // Estrategia: ExecuteUpdate filtrado por DispatchStatus=Queued. Si dos workers ven
        // el mismo candidato, el primero que ejecuta el UPDATE gana — el segundo no encuentra
        // filas con DispatchStatus=Queued para esos IDs y quedará procesando 0.
        var nowUtc = DateTime.UtcNow;
        var batchSize = Math.Min(maxPerHour, maxPerDay - sentToday);

        // Paso A: identificar candidatos del lote — IDs en orden FIFO.
        var candidateIds = await db.CampaignContacts
            .Where(cc => cc.CampaignId == campaignId
                      && cc.DispatchStatus == DispatchStatus.Queued
                      && (cc.ScheduledFor == null || cc.ScheduledFor <= nowUtc))
            .OrderBy(cc => cc.CreatedAt)
            .Take(batchSize)
            .Select(cc => cc.Id)
            .ToListAsync(ct);

        if (candidateIds.Count == 0)
        {
            // No quedan Queued elegibles. ¿Ya completamos todo o sólo estamos en pausa?
            // IMPORTANTE: incluimos Pending — son contactos legacy v1 que aún no pasaron
            // por el intake v2. Si los hay, NO marcamos Completed: la campaña está en
            // un flujo legacy y no nos toca cerrarla.
            var hasMore = await db.CampaignContacts
                .AnyAsync(cc => cc.CampaignId == campaignId
                             && (cc.DispatchStatus == DispatchStatus.Pending
                                 || cc.DispatchStatus == DispatchStatus.Queued
                                 || cc.DispatchStatus == DispatchStatus.Claimed
                                 || cc.DispatchStatus == DispatchStatus.Retry
                                 || cc.DispatchStatus == DispatchStatus.Deferred), ct);

            if (!hasMore)
            {
                campaign.CompletedAt = DateTime.UtcNow;
                campaign.Status = CampaignStatus.Completed;
                campaign.IsActive = false;
                await db.SaveChangesAsync(ct);

                logger.LogInformation("Campaña {CampaignId}: completada. Todos los contactos procesados.", campaignId);

                try { await eventDispatcher.DispatchAsync("CampaignFinished", campaignId.ToString(), campaign.TenantId, ct); }
                catch (Exception ex) { logger.LogError(ex, "DispatchAsync CampaignFinished falló (continuamos)."); }

                return new DispatchResult(0, 0, "Campaña completada", DispatchStopReason.AllContactsProcessed);
            }

            // Hay Deferred / Retry pendientes pero no aún elegibles (ScheduledFor en el futuro).
            return new DispatchResult(0, 0, "Sin contactos elegibles ahora", DispatchStopReason.BatchCompleted);
        }

        // Paso B: claim atómico — solo los que sigan Queued.
        var claimedAt = DateTime.UtcNow;
        var actuallyClaimed = await db.CampaignContacts
            .Where(cc => candidateIds.Contains(cc.Id) && cc.DispatchStatus == DispatchStatus.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.DispatchStatus, DispatchStatus.Claimed)
                .SetProperty(c => c.ClaimedAt, claimedAt), ct);

        if (actuallyClaimed == 0)
        {
            // Otra instancia del Worker se llevó todos los candidatos. No es un error — solo no me toca.
            return new DispatchResult(0, 0, "Otro worker tomó el lote", DispatchStopReason.BatchCompleted);
        }

        // Paso C: recargar los efectivamente Claimed por mí (filtro por ClaimedAt para distinguir
        // de Claims de otras instancias que pudieran haber ganado otros candidatos).
        var pendingContacts = await db.CampaignContacts
            .Where(cc => candidateIds.Contains(cc.Id)
                      && cc.DispatchStatus == DispatchStatus.Claimed
                      && cc.ClaimedAt == claimedAt)
            .ToListAsync(ct);

        if (pendingContacts.Count == 0)
        {
            // Caso raro: otro worker nos ganó la carrera entre el ExecuteUpdate y el SELECT.
            return new DispatchResult(0, 0, "Otro worker tomó el lote (post-claim)", DispatchStopReason.BatchCompleted);
        }

        // ── 6. Marcar campaña como iniciada (idempotente) ──
        var isFirstStart = campaign.StartedAt is null;
        if (isFirstStart)
        {
            campaign.StartedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            try { await eventDispatcher.DispatchAsync("CampaignStarted", campaignId.ToString(), campaign.TenantId, ct); }
            catch (Exception ex) { logger.LogError(ex, "DispatchAsync CampaignStarted falló (continuamos)."); }

            try { await ScheduleAutoCloseAsync(campaign, ct); }
            catch (Exception ex) { logger.LogError(ex, "Schedule AutoClose falló para campaña {Id}.", campaignId); }
        }

        // ── 7. Enviar mensajes uno a uno (con rate limit del tenant) ─────────────────
        var sent = 0;
        var failed = 0;
        var consecutiveErrors = 0;

        foreach (var contact in pendingContacts)
        {
            if (ct.IsCancellationRequested) break;

            // Verificar horario antes de cada envío.
            if (!IsWithinBusinessHours(tenant))
            {
                logger.LogInformation("Campaña {CampaignId}: salió del horario durante el envío.", campaignId);
                // Devolver al pool: vuelvo a Queued para que el Worker reintente más tarde.
                contact.DispatchStatus = DispatchStatus.Queued;
                contact.ClaimedAt = null;
                break;
            }

            // Verificar límite diario.
            if (sentToday + sent >= maxPerDay)
            {
                logger.LogInformation("Campaña {CampaignId}: límite diario alcanzado durante el envío.", campaignId);
                contact.DispatchStatus = DispatchStatus.Queued;
                contact.ClaimedAt = null;
                break;
            }

            try
            {
                var message = BuildInitialMessage(contact, campaign);
                var sendNow = DateTime.UtcNow;

                var result = await provider.SendMessageAsync(
                    new SendMessageRequest(contact.PhoneNumber, message), ct);

                if (result.Success)
                {
                    contact.DispatchStatus = DispatchStatus.Sent;
                    contact.SentAt = sendNow;
                    contact.LastContactAt = sendNow;       // mantenido para compatibilidad legacy
                    contact.ExternalMessageId = result.ExternalMessageId;
                    contact.GeneratedMessage = message;
                    contact.DispatchAttempts++;
                    contact.DispatchError = null;
                    sent++;
                    consecutiveErrors = 0;
                    logger.LogDebug("Campaña {CampaignId}: enviado a {Phone} (#{Count})",
                        campaignId, contact.PhoneNumber, sent);

                    try { await ScheduleFollowUpsForContactAsync(campaign, contact, ct); }
                    catch (Exception ex) { logger.LogError(ex, "Schedule follow-ups falló para {Phone}", contact.PhoneNumber); }
                }
                else
                {
                    contact.DispatchAttempts++;
                    contact.DispatchError = Truncate(result.Error, 2000);
                    contact.LastContactAt = sendNow;
                    failed++;
                    consecutiveErrors++;
                    logger.LogWarning("Campaña {CampaignId}: error enviando a {Phone}: {Error}",
                        campaignId, contact.PhoneNumber, result.Error);

                    // Si superó el max attempts → Error definitivo. Si no → Retry.
                    contact.DispatchStatus = contact.DispatchAttempts >= 3
                        ? DispatchStatus.Error
                        : DispatchStatus.Retry;
                    contact.ClaimedAt = null;

                    if (consecutiveErrors >= ConsecutiveErrorThreshold)
                    {
                        logger.LogWarning("Campaña {CampaignId}: {Threshold}+ errores consecutivos. Frenando lote.",
                            campaignId, ConsecutiveErrorThreshold);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Campaña {CampaignId}: excepción enviando a {Phone}",
                    campaignId, contact.PhoneNumber);
                contact.DispatchAttempts++;
                contact.DispatchError = Truncate(ex.Message, 2000);
                contact.DispatchStatus = contact.DispatchAttempts >= 3
                    ? DispatchStatus.Error
                    : DispatchStatus.Retry;
                contact.ClaimedAt = null;
                failed++;
                consecutiveErrors++;

                if (consecutiveErrors >= ConsecutiveErrorThreshold) break;
            }

            // ── Delay anti-ban: base = 60s/MessagesPerMinute, ±20% jitter ───────
            var jitter = (_random.NextDouble() * 0.4) - 0.2;     // [-0.2, +0.2]
            var actualDelay = Math.Max(MinDelaySecondsFloor, baseDelaySeconds * (1 + jitter));
            await Task.Delay(TimeSpan.FromSeconds(actualDelay), ct);
        }

        // ── 8. Actualizar contadores de la campaña ───────
        campaign.ProcessedContacts = await db.CampaignContacts
            .CountAsync(cc => cc.CampaignId == campaignId
                           && (cc.DispatchStatus == DispatchStatus.Sent
                               || cc.DispatchStatus == DispatchStatus.Error
                               || cc.DispatchStatus == DispatchStatus.Skipped
                               || cc.DispatchStatus == DispatchStatus.Duplicate), ct);

        await db.SaveChangesAsync(ct);

        var stopReason = consecutiveErrors >= ConsecutiveErrorThreshold ? DispatchStopReason.TooManyErrors
            : sentToday + sent >= maxPerDay ? DispatchStopReason.DailyLimitReached
            : DispatchStopReason.BatchCompleted;

        logger.LogInformation(
            "Campaña {CampaignId}: lote finalizado. Enviados={Sent}, Fallidos={Failed}, Razón={Reason}",
            campaignId, sent, failed, stopReason);

        return new DispatchResult(sent, failed, null, stopReason);
    }

    private static string? Truncate(string? s, int max) =>
        s is null ? null : s.Length <= max ? s : s[..max];

    /// <summary>
    /// Construye el mensaje inicial personalizado para un contacto.
    /// Usa los datos del contacto (nombre, póliza, monto) para personalizar.
    /// En el futuro, el agente IA generará este mensaje con su system prompt.
    /// </summary>
    private static string BuildInitialMessage(CampaignContact contact, Campaign campaign)
    {
        // Mensaje template básico — será reemplazado por generación del LLM
        var name = contact.ClientName ?? "Estimado cliente";
        var parts = new List<string>
        {
            $"Hola {name}, le saluda el equipo de cobros."
        };

        if (!string.IsNullOrEmpty(contact.PolicyNumber))
            parts.Add($"Nos comunicamos respecto a su póliza {contact.PolicyNumber}");

        if (contact.PendingAmount.HasValue && contact.PendingAmount > 0)
            parts.Add($"con un saldo pendiente de ${contact.PendingAmount:N2}");

        if (!string.IsNullOrEmpty(contact.InsuranceCompany))
            parts.Add($"en {contact.InsuranceCompany}");

        parts.Add("¿Podría indicarnos cuándo realizará el pago? Quedamos atentos.");

        return string.Join(". ", parts) + ".";
    }

    // ── Slugs de ActionDefinitions globales que el seed registra al boot ─────
    // y que los executors específicos (FollowUpExecutor, CampaignAutoCloseExecutor)
    // declaran como su Slug. El campo Name de la action en BD coincide con estos.
    private const string FollowUpActionSlug = "FOLLOW_UP_MESSAGE";
    private const string AutoCloseActionSlug = "AUTO_CLOSE_CAMPAIGN";

    /// <summary>
    /// Crea jobs DelayFromEvent (uno por cada hora en CampaignTemplate.FollowUpHours)
    /// para enviar los mensajes de seguimiento al contacto. Skip silencioso si el
    /// maestro no tiene seguimientos configurados o no hay mensajes definidos.
    /// </summary>
    private async Task ScheduleFollowUpsForContactAsync(
        Campaign campaign, CampaignContact contact, CancellationToken ct)
    {
        var template = await db.CampaignTemplates
            .Where(t => t.Id == campaign.CampaignTemplateId)
            .Select(t => new { t.FollowUpHours, t.FollowUpMessagesJson })
            .FirstOrDefaultAsync(ct);
        if (template is null) return;
        if (template.FollowUpHours.Count == 0) return;
        if (string.IsNullOrEmpty(template.FollowUpMessagesJson)) return;

        var actionId = await GetOrFailActionDefinitionIdAsync(FollowUpActionSlug, ct);
        if (actionId is null) return;

        for (var i = 0; i < template.FollowUpHours.Count; i++)
        {
            var hours = template.FollowUpHours[i];
            if (hours <= 0) continue;

            var job = new ScheduledWebhookJob
            {
                Id = Guid.NewGuid(),
                ActionDefinitionId = actionId.Value,
                TriggerType = "DelayFromEvent",
                TriggerEvent = "CampaignContactSent",
                DelayMinutes = hours * 60,
                Scope = "PerConversation",
                IsActive = true,
                NextRunAt = DateTime.UtcNow.AddMinutes(hours * 60)
            };
            await scheduledJobs.AddAsync(job, ct);
        }
        // Log compacto — útil al revisar la cola.
        logger.LogInformation(
            "Programados {Count} seguimientos para contacto {Phone}.",
            template.FollowUpHours.Count, contact.PhoneNumber);
    }

    /// <summary>
    /// Crea UN job DelayFromEvent que cierra la campaña al cumplirse AutoCloseHours.
    /// Skip silencioso si AutoCloseHours = 0.
    /// </summary>
    private async Task ScheduleAutoCloseAsync(Campaign campaign, CancellationToken ct)
    {
        var template = await db.CampaignTemplates
            .Where(t => t.Id == campaign.CampaignTemplateId)
            .Select(t => new { t.AutoCloseHours })
            .FirstOrDefaultAsync(ct);
        if (template is null || template.AutoCloseHours <= 0) return;

        var actionId = await GetOrFailActionDefinitionIdAsync(AutoCloseActionSlug, ct);
        if (actionId is null) return;

        var job = new ScheduledWebhookJob
        {
            Id = Guid.NewGuid(),
            ActionDefinitionId = actionId.Value,
            TriggerType = "DelayFromEvent",
            TriggerEvent = "CampaignStarted",
            DelayMinutes = template.AutoCloseHours * 60,
            Scope = "PerCampaign",
            IsActive = true,
            NextRunAt = DateTime.UtcNow.AddMinutes(template.AutoCloseHours * 60)
        };
        await scheduledJobs.AddAsync(job, ct);

        logger.LogInformation(
            "Auto-cierre programado para campaña {Id} en {Hours}h.",
            campaign.Id, template.AutoCloseHours);
    }

    /// <summary>
    /// Resuelve el Id de una ActionDefinition global por su Name. Si no existe,
    /// loguea warning y devuelve null — los executors específicos no se invocan
    /// si no hay action en BD (la app debería seedearla al boot).
    /// </summary>
    private async Task<Guid?> GetOrFailActionDefinitionIdAsync(string name, CancellationToken ct)
    {
        var id = await db.ActionDefinitions
            .Where(a => a.TenantId == null && a.Name == name && a.IsActive)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        if (id is null)
        {
            logger.LogWarning(
                "ActionDefinition global '{Name}' no existe — no se programan jobs. Aplica el seed al boot.", name);
        }
        return id;
    }

    /// <summary>
    /// Verifica si estamos dentro del horario de oficina del tenant.
    /// Convierte la hora UTC actual a la zona horaria del tenant y compara.
    /// </summary>
    private static bool IsWithinBusinessHours(Tenant tenant)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);
            var nowInTz = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var currentTime = TimeOnly.FromDateTime(nowInTz);
            return currentTime >= tenant.BusinessHoursStart && currentTime <= tenant.BusinessHoursEnd;
        }
        catch
        {
            // Si la zona horaria es inválida, asumir que estamos en horario
            return true;
        }
    }
}

/// <summary>Resultado de un lote de envío.</summary>
public record DispatchResult(int Sent, int Failed, string? Error, DispatchStopReason StopReason);

/// <summary>Razón por la que se detuvo el envío.</summary>
public enum DispatchStopReason
{
    BatchCompleted,         // Terminó el lote normalmente
    AllContactsProcessed,   // Todos los contactos ya fueron procesados
    OutsideBusinessHours,   // Fuera de horario de oficina
    DailyLimitReached,      // Alcanzó el máximo diario
    HourlyLimitReached,     // Alcanzó el máximo por hora
    TooManyErrors,          // 3+ errores consecutivos (posible ban)
    CampaignInactive,       // La campaña fue desactivada
    NoProvider,             // Sin línea WhatsApp activa
    Error                   // Error general
}
