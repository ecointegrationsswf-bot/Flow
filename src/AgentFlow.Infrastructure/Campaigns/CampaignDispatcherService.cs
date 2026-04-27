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
/// Reglas anti-ban:
/// - Delay aleatorio de 8-15 segundos entre cada mensaje
/// - Máximo 200 mensajes por hora por número
/// - Máximo 1,000 mensajes por día por número
/// - Solo envía dentro del horario de oficina del tenant (default 8am-5pm Panamá)
/// - Si UltraMsg responde error, pausa 30 minutos
///
/// El servicio NO programa los envíos — solo ejecuta un lote.
/// Hangfire (o quien lo llame) se encarga de programar cuándo corre.
/// </summary>
public class CampaignDispatcherService(
    AgentFlowDbContext db,
    IChannelProviderFactory providerFactory,
    IWebhookEventDispatcher eventDispatcher,
    IScheduledJobRepository scheduledJobs,
    ILogger<CampaignDispatcherService> logger)
{
    // ── Configuración anti-ban ────────────────────────
    private const int MinDelaySeconds = 8;      // mínimo entre mensajes
    private const int MaxDelaySeconds = 15;     // máximo entre mensajes
    private const int MaxPerHour = 200;         // máximo mensajes por hora
    private const int MaxPerDay = 1000;         // máximo mensajes por día
    private const int ErrorPauseMinutes = 30;   // pausa ante error de UltraMsg

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

        // ── 4. Contar envíos del día (para límite diario) ─
        var todayUtc = DateTime.UtcNow.Date;
        var sentToday = await db.Set<CampaignContact>()
            .Where(cc => cc.Campaign!.TenantId == tenant.Id
                      && cc.LastContactAt != null
                      && cc.LastContactAt >= todayUtc)
            .CountAsync(ct);

        if (sentToday >= MaxPerDay)
        {
            logger.LogInformation("Campaña {CampaignId}: límite diario alcanzado ({Sent}/{Max}).",
                campaignId, sentToday, MaxPerDay);
            return new DispatchResult(0, sentToday, "Límite diario alcanzado", DispatchStopReason.DailyLimitReached);
        }

        // ── 5. Obtener contactos pendientes ──────────────
        var pendingContacts = await db.Set<CampaignContact>()
            .Where(cc => cc.CampaignId == campaignId
                      && cc.IsPhoneValid
                      && cc.Result == GestionResult.Pending
                      && cc.LastContactAt == null)  // nunca contactado
            .OrderBy(cc => cc.CreatedAt)
            .Take(MaxPerHour)                        // máximo por lote
            .ToListAsync(ct);

        if (pendingContacts.Count == 0)
        {
            // Todos los contactos ya fueron procesados
            campaign.CompletedAt = DateTime.UtcNow;
            campaign.IsActive = false;
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Campaña {CampaignId}: completada. Todos los contactos procesados.", campaignId);

            // Hook ScheduledWebhookWorker — programa jobs EventBased/DelayFromEvent
            // suscritos a "CampaignFinished". Si no hay suscriptores, no-op silencioso.
            try
            {
                await eventDispatcher.DispatchAsync("CampaignFinished", campaignId.ToString(), campaign.TenantId, ct);
            }
            catch (Exception ex)
            {
                // El dispatcher no debe romper el flujo de campañas.
                logger.LogError(ex, "DispatchAsync CampaignFinished falló (continuamos).");
            }

            return new DispatchResult(0, 0, "Campaña completada", DispatchStopReason.AllContactsProcessed);
        }

        // ── 6. Marcar campaña como iniciada ──────────────
        var isFirstStart = campaign.StartedAt is null;
        if (isFirstStart)
        {
            campaign.StartedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            // Hook — programa jobs suscritos a "CampaignStarted" (preconfigurados).
            try
            {
                await eventDispatcher.DispatchAsync("CampaignStarted", campaignId.ToString(), campaign.TenantId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DispatchAsync CampaignStarted falló (continuamos).");
            }

            // Fase 2: programar el job dinámico de cierre automático.
            try { await ScheduleAutoCloseAsync(campaign, ct); }
            catch (Exception ex) { logger.LogError(ex, "Schedule AutoClose falló para campaña {Id}.", campaignId); }
        }

        // ── 7. Enviar mensajes uno a uno ─────────────────
        var sent = 0;
        var failed = 0;

        foreach (var contact in pendingContacts)
        {
            if (ct.IsCancellationRequested) break;

            // Verificar horario antes de cada envío
            if (!IsWithinBusinessHours(tenant))
            {
                logger.LogInformation("Campaña {CampaignId}: salió del horario durante el envío.", campaignId);
                break;
            }

            // Verificar límite diario
            if (sentToday + sent >= MaxPerDay)
            {
                logger.LogInformation("Campaña {CampaignId}: límite diario alcanzado durante el envío.", campaignId);
                break;
            }

            try
            {
                // Construir mensaje personalizado con datos del contacto
                var message = BuildInitialMessage(contact, campaign);

                var result = await provider.SendMessageAsync(
                    new SendMessageRequest(contact.PhoneNumber, message), ct);

                if (result.Success)
                {
                    contact.LastContactAt = DateTime.UtcNow;
                    contact.RetryCount++;
                    sent++;
                    logger.LogDebug("Campaña {CampaignId}: enviado a {Phone} (#{Count})",
                        campaignId, contact.PhoneNumber, sent);

                    // Fase 2: programar jobs de seguimiento (uno por hora configurada).
                    // Si el maestro no tiene FollowUpHours, no-op silencioso.
                    try { await ScheduleFollowUpsForContactAsync(campaign, contact, ct); }
                    catch (Exception ex) { logger.LogError(ex, "Schedule follow-ups falló para {Phone}", contact.PhoneNumber); }
                }
                else
                {
                    contact.LastContactAt = DateTime.UtcNow;
                    failed++;
                    logger.LogWarning("Campaña {CampaignId}: error enviando a {Phone}: {Error}",
                        campaignId, contact.PhoneNumber, result.Error);

                    // Si hay error, posible ban parcial → pausar
                    if (failed >= 3)
                    {
                        logger.LogWarning("Campaña {CampaignId}: 3+ errores consecutivos. Pausando {Min} min.",
                            campaignId, ErrorPauseMinutes);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Campaña {CampaignId}: excepción enviando a {Phone}",
                    campaignId, contact.PhoneNumber);
                failed++;

                if (failed >= 3) break;
            }

            // ── Delay anti-ban ───────────────────────────
            var delay = _random.Next(MinDelaySeconds, MaxDelaySeconds + 1);
            await Task.Delay(TimeSpan.FromSeconds(delay), ct);
        }

        // ── 8. Actualizar contadores de la campaña ───────
        campaign.ProcessedContacts = await db.Set<CampaignContact>()
            .CountAsync(cc => cc.CampaignId == campaignId && cc.LastContactAt != null, ct);

        await db.SaveChangesAsync(ct);

        var stopReason = failed >= 3 ? DispatchStopReason.TooManyErrors
            : sentToday + sent >= MaxPerDay ? DispatchStopReason.DailyLimitReached
            : DispatchStopReason.BatchCompleted;

        logger.LogInformation(
            "Campaña {CampaignId}: lote finalizado. Enviados={Sent}, Fallidos={Failed}, Razón={Reason}",
            campaignId, sent, failed, stopReason);

        return new DispatchResult(sent, failed, null, stopReason);
    }

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
