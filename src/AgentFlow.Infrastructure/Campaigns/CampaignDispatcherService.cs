using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Channels.UltraMsg;
using AgentFlow.Infrastructure.Email;
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
    IInitialMessageGenerator? messageGenerator,
    IBusinessHoursClock businessHours,
    IEmailService emailService,
    EmailTemplateRenderer emailRenderer,
    IUltraMsgInstanceService ultraMsgInstance,
    ITeamsNotifier teamsNotifier,
    IWhatsAppNumberValidator whatsAppValidator,
    ILogger<CampaignDispatcherService> logger)
{
    // ── Mínimos de seguridad — tope inferior aunque la config diga lo contrario ──
    private const int MinDelaySecondsFloor = 3;     // jamás bajamos de 3s entre mensajes
    private const int ConsecutiveErrorThreshold = 3; // 3 errores seguidos → frena
    // Cada cuántos envíos EXITOSOS re-validamos que la línea siga authenticated.
    // Cinturón doble contra el escenario del 18/05: línea cae a mitad del batch y
    // UltraMsg responde HTTP 200 con sent=false (encolando internamente). Aunque
    // el Fix #1 valida el body, este check periódico atrapa el caso edge donde
    // la línea cae justo antes de un envío y el body responde algo no esperado.
    private const int LineHealthRecheckEveryN = 5;

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
        // ── 1. Cargar campaña con su tenant + template ────
        // Template necesario porque el horario de envío vive ahí (con prioridad sobre el tenant).
        var campaign = await db.Campaigns
            .Include(c => c.Tenant)
            .Include(c => c.CampaignTemplate)
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct);

        if (campaign is null)
            return new DispatchResult(0, 0, "Campaña no encontrada", DispatchStopReason.Error);

        if (!campaign.IsActive)
            return new DispatchResult(0, 0, "Campaña inactiva", DispatchStopReason.CampaignInactive);

        var tenant = campaign.Tenant;
        if (tenant is null)
            return new DispatchResult(0, 0, "Tenant no encontrado", DispatchStopReason.Error);

        // ── 1.b Recovery sweep — liberar Claimed huérfanos ──────────────
        // Causa típica: el Worker se reinicia (deploy, crash, OOM, machine reboot)
        // en medio del foreach de envío. La rama de cancellation del foreach hacía
        // break sin devolver los contactos a Queued, así que esos contactos quedan
        // invisibles para el siguiente tick (la query de candidatos solo mira
        // Queued, no Claimed). Resultado: campaña stuck en X/N sin avanzar.
        //
        // Cualquier Claimed con ClaimedAt más viejo que el cutoff se considera
        // huérfano. Un send individual dura ~3-30 seg incluso con LLM + WhatsApp,
        // así que 5 minutos es seguro: si lleva tanto tiempo Claimed, el proceso
        // que lo reclamó no va a volver. ExecuteUpdate es atómico — no hay riesgo
        // de pisar un Worker activo, porque el send activo todavía no salió de su
        // iteración (no ha pasado tanto tiempo).
        var staleClaimCutoff = DateTime.UtcNow.AddMinutes(-5);
        var releasedStaleClaims = await db.CampaignContacts
            .Where(cc => cc.CampaignId == campaignId
                      && cc.DispatchStatus == DispatchStatus.Claimed
                      && cc.ClaimedAt < staleClaimCutoff
                      && cc.SentAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.DispatchStatus, DispatchStatus.Queued)
                .SetProperty(c => c.ClaimedAt, (DateTime?)null), ct);
        if (releasedStaleClaims > 0)
            logger.LogWarning(
                "Campaña {CampaignId}: liberados {N} contactos huérfanos en Claimed > 5 min " +
                "(probable reinicio del Worker en lote anterior). Vuelven a Queued para el próximo intento.",
                campaignId, releasedStaleClaims);

        // Off-switch del tenant (no relanza, no envía).
        if (!tenant.CampaignDispatchEnabled)
        {
            logger.LogInformation("Campaña {CampaignId}: dispatch deshabilitado para tenant {TenantId}.",
                campaignId, tenant.Id);
            return new DispatchResult(0, 0, "Dispatch deshabilitado por tenant", DispatchStopReason.CampaignInactive);
        }

        // ── 2. Cierre temprano: si NO queda ningún contacto in-flight, la
        //      campaña ya terminó. Marcamos Completed independientemente del
        //      horario o del cool-down — cerrar una campaña terminada no es
        //      lo mismo que enviar. Esto DEBE ir ANTES del check de cool-down:
        //      si lo dejamos al revés, una campaña con todos sus contactos
        //      ya enviados queda como "Running" durante todo el cool-down
        //      (20 min default) aunque ya no haya nada que hacer.
        var hasActive = await db.CampaignContacts.AnyAsync(cc => cc.CampaignId == campaignId
                            && (cc.DispatchStatus == DispatchStatus.Pending
                                || cc.DispatchStatus == DispatchStatus.Queued
                                || cc.DispatchStatus == DispatchStatus.Claimed
                                || cc.DispatchStatus == DispatchStatus.Retry
                                || cc.DispatchStatus == DispatchStatus.Deferred), ct);
        if (!hasActive)
        {
            campaign.CompletedAt = DateTime.UtcNow;
            campaign.Status = CampaignStatus.Completed;
            // Limpiamos NextBatchAfterUtc al cerrar — ya no aplica cool-down a
            // una campaña Completed. Evita que el badge "⏸ Xm" del frontend
            // siga apareciendo tras el cierre.
            campaign.NextBatchAfterUtc = null;
            // IsActive se MANTIENE en true a propósito. "Completed" significa que
            // ya no hay mensajes iniciales por mandar, pero la campaña sigue VIVA
            // para que FollowUpSweep dispare los seguimientos parametrizados y
            // para que AutoCloseSweep la cierre cuando se cumplan AutoCloseHours
            // desde CompletedAt. El flip a IsActive=false lo hace AutoCloseSweep,
            // no este dispatcher.
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Campaña {CampaignId}: completada (cierre temprano fuera de horario o sin in-flight).", campaignId);

            try { await eventDispatcher.DispatchAsync("CampaignFinished", campaignId.ToString(), campaign.TenantId, ct); }
            catch (Exception ex) { logger.LogError(ex, "DispatchAsync CampaignFinished falló (continuamos)."); }

            return new DispatchResult(0, 0, "Campaña completada", DispatchStopReason.AllContactsProcessed);
        }

        // ── 1.c Cool-down entre batches (Phase 3) ────────────────────────
        // Si esta campaña terminó un batch hace poco PERO todavía hay contactos
        // pendientes, esperamos a que pase CampaignBatchCoolDownMinutes antes
        // de tomar el siguiente lote. Esto evita ráfagas largas que disparan
        // detección anti-spam de WhatsApp. (El cool-down NO aplica si ya no
        // hay activos — eso ya se manejó en el check anterior.)
        if (campaign.NextBatchAfterUtc.HasValue && campaign.NextBatchAfterUtc.Value > DateTime.UtcNow)
        {
            var waitMin = (int)(campaign.NextBatchAfterUtc.Value - DateTime.UtcNow).TotalMinutes;
            logger.LogInformation(
                "Campaña {CampaignId}: en cool-down hasta {Until:o} (~{Min} min). Saltando este tick.",
                campaignId, campaign.NextBatchAfterUtc.Value, waitMin);
            return new DispatchResult(0, 0,
                $"En cool-down entre batches ({waitMin} min restantes)",
                DispatchStopReason.BatchCompleted);
        }

        // ── 3. Verificar horario de envío (solo bloquea ENVÍO; el cierre ya pasó arriba) ──
        var hoursCheck = IsWithinBusinessHours(campaign, tenant);
        if (!hoursCheck.Within)
        {
            logger.LogInformation("Campaña {CampaignId}: fuera de horario — {Reason}", campaignId, hoursCheck.Reason);
            return new DispatchResult(0, 0, "Fuera de horario", DispatchStopReason.OutsideBusinessHours);
        }
        logger.LogDebug("Campaña {CampaignId}: {Reason}", campaignId, hoursCheck.Reason);

        // ── 3. Cargar el agente y resolver canales a usar ────────────────
        // El canal a usar viene del AGENTE, no del Campaign.Channel. Si el
        // agente tiene varios canales habilitados, enviamos por TODOS ellos
        // (cada contacto recibe N mensajes, uno por canal).
        var agent = await db.AgentDefinitions
            .Where(a => a.Id == campaign.AgentDefinitionId)
            .Select(a => new { a.Id, a.Name, a.EnabledChannels, a.IsActive })
            .FirstOrDefaultAsync(ct);

        if (agent is null || !agent.IsActive)
        {
            logger.LogWarning("Campaña {CampaignId}: agente inválido o inactivo.", campaignId);
            return new DispatchResult(0, 0, "Agente inválido o inactivo", DispatchStopReason.Error);
        }

        var enabledChannels = agent.EnabledChannels ?? [];
        if (enabledChannels.Count == 0)
        {
            logger.LogWarning("Campaña {CampaignId}: el agente '{Agent}' no tiene canales habilitados.",
                campaignId, agent.Name);
            return new DispatchResult(0, 0,
                $"El agente '{agent.Name}' no tiene canales habilitados.",
                DispatchStopReason.NoProvider);
        }

        // Pre-cargar provider WhatsApp SOLO si el agente lo usa. Si no, no
        // exigimos línea — la campaña puede ser email-only sin WhatsApp configurado.
        IChannelProvider? waProvider = null;
        if (enabledChannels.Contains(ChannelType.WhatsApp))
        {
            waProvider = await providerFactory.GetProviderAsync(tenant.Id, ct);
            if (waProvider is null)
            {
                // Si el agente SOLO tiene WhatsApp y no hay línea, no podemos enviar.
                if (enabledChannels.Count == 1)
                {
                    logger.LogWarning("Campaña {CampaignId}: agente solo soporta WhatsApp pero tenant sin línea activa.", campaignId);
                    return new DispatchResult(0, 0, "Sin línea WhatsApp activa", DispatchStopReason.NoProvider);
                }
                // Si tiene varios canales y falta WhatsApp, seguimos con los otros pero avisamos.
                logger.LogWarning("Campaña {CampaignId}: WhatsApp habilitado en el agente pero tenant sin línea. Se omite WhatsApp en los envíos.", campaignId);
            }
            else
            {
                // ── Fix #2 — Pre-check de salud de la línea ANTES de empezar el batch ──
                // Llama UltraMsg /instance/status. Si != "authenticated" abortamos el
                // batch, pausamos la campaña y notificamos al admin. Evita el escenario
                // del 18/05 donde la línea estaba caída y UltraMsg encolaba 95 mensajes.
                var (healthy, reason) = await EnsureWhatsAppLineHealthyAsync(tenant.Id, ct);
                if (!healthy)
                {
                    logger.LogWarning("Campaña {CampaignId}: línea WhatsApp NO sana ({Reason}). Pausando campaña.",
                        campaignId, reason);
                    campaign.IsActive = false;
                    campaign.Status = CampaignStatus.Paused;
                    await db.SaveChangesAsync(ct);

                    // Notificar al admin del tenant (best-effort).
                    try { await NotifyLineDownAsync(tenant, campaign, reason!, ct); }
                    catch (Exception ex) { logger.LogWarning(ex, "[LineHealth] Notificación falló (no bloqueante)."); }

                    return new DispatchResult(0, 0,
                        $"Línea WhatsApp inactiva: {reason}",
                        DispatchStopReason.NoProvider);
                }
            }
        }

        // Si el agente envía email, validar que el maestro tenga template.
        if (enabledChannels.Contains(ChannelType.Email))
        {
            var hasTemplate = !string.IsNullOrWhiteSpace(campaign.CampaignTemplate?.EmailBodyHtml);
            if (!hasTemplate)
            {
                if (enabledChannels.Count == 1 || waProvider is null)
                {
                    logger.LogWarning(
                        "Campaña {CampaignId}: agente con canal Email pero el maestro no tiene EmailBodyHtml. " +
                        "Configurá la plantilla en el tab Correo del maestro y relanzá.",
                        campaignId);
                    return new DispatchResult(0, 0,
                        "El maestro no tiene plantilla de correo configurada.",
                        DispatchStopReason.NoProvider);
                }
                logger.LogWarning("Campaña {CampaignId}: Email habilitado pero sin plantilla. Se omite email en los envíos.", campaignId);
            }
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

        // ── 4.5 Rescate de contactos en Retry (legacy / pre-refactor) ────
        // Versiones anteriores del dispatcher dejaban contactos en Retry sin
        // ScheduledFor — quedaban en limbo porque la query de candidatos solo
        // mira Queued. Acá los movemos de vuelta a Queued para que el batch
        // los considere. Idempotente — no afecta a contactos ya procesados.
        await db.CampaignContacts
            .Where(cc => cc.CampaignId == campaignId
                      && cc.DispatchStatus == DispatchStatus.Retry)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.DispatchStatus, DispatchStatus.Queued)
                .SetProperty(c => c.ScheduledFor, (DateTime?)null), ct);

        // ── 5. Claim atómico Queued → Claimed (anti double-send entre Workers concurrentes) ─
        // Estrategia: ExecuteUpdate filtrado por DispatchStatus=Queued. Si dos workers ven
        // el mismo candidato, el primero que ejecuta el UPDATE gana — el segundo no encuentra
        // filas con DispatchStatus=Queued para esos IDs y quedará procesando 0.
        var nowUtc = DateTime.UtcNow;
        // Tope efectivo del batch:
        //   • maxPerHour  — ya configurado por tenant
        //   • maxPerDay - sentToday — cuántos quedan en el día
        //   • CampaignBatchSize — NUEVO (Phase 3): el batch de UN solo tick
        //     no debería superar este tope. Permite controlar el ritmo natural
        //     sin tocar maxPerHour (que es un guard absoluto, no operativo).
        var configuredBatchSize = Math.Max(1, tenant.CampaignBatchSize);
        var batchSize = Math.Min(Math.Min(maxPerHour, maxPerDay - sentToday), configuredBatchSize);

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
                // Limpiamos el cool-down al cerrar — coherencia con el cierre
                // temprano arriba para que el frontend no siga mostrando
                // "⏸ Xm" en una campaña ya completada.
                campaign.NextBatchAfterUtc = null;
                // IsActive se MANTIENE en true. Ver nota en el cierre temprano arriba —
                // la transición a IsActive=false la hace AutoCloseSweep cuando se
                // cumplan AutoCloseHours desde CompletedAt.
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

            // El auto-cierre es responsabilidad del sweeper global
            // (CampaignAutoCloseSweepExecutor / cron AUTO_CLOSE_CAMPAIGN_SWEEP).
            // Antes acá programábamos un job DelayFromEvent por campaña — eso
            // saturaba ScheduledWebhookJobs. El sweeper tick cada 30 min revisa
            // todas las Running y cierra las que pasen su AutoCloseHours.
        }

        // ── 7. Enviar mensajes uno a uno (con rate limit del tenant) ─────────────────
        var sent = 0;
        var failed = 0;
        var consecutiveErrors = 0;

        // Try/finally para garantizar que cualquier contacto que quede Claimed
        // por nosotros pero NO fue procesado (porque el ct cancelló durante
        // Task.Delay, o una excepción no controlada burbujeó del send), vuelva
        // a Queued inmediatamente — sin esperar al recovery sweep de 5 min del
        // próximo tick. Esto es el fix de raíz para el caso "campaña stuck en
        // 22/99 después de un deploy del Worker".
        try
        {
        foreach (var contact in pendingContacts)
        {
            if (ct.IsCancellationRequested) break;

            // Verificar horario antes de cada envío.
            var hoursCheckMid = IsWithinBusinessHours(campaign, tenant);
            if (!hoursCheckMid.Within)
            {
                logger.LogInformation("Campaña {CampaignId}: salió del horario — {Reason}", campaignId, hoursCheckMid.Reason);
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
                var sendNow = DateTime.UtcNow;

                // ── Envío por TODOS los canales habilitados del agente ────
                // Cada contacto recibe un mensaje por cada canal del agente
                // (si tiene los datos de contacto adecuados — phone/email).
                int channelSuccesses = 0;
                int channelFailures = 0;
                string? lastError = null;
                DispatchAttemptResult? primaryDispatch = null;

                foreach (var channel in enabledChannels)
                {
                    DispatchAttemptResult? attempt = null;

                    if (channel == ChannelType.Email)
                    {
                        // Validar template (puede no estar si el agente tiene ambos
                        // canales y solo configuraron template para uno).
                        if (string.IsNullOrWhiteSpace(campaign.CampaignTemplate?.EmailBodyHtml))
                            continue;
                        if (string.IsNullOrWhiteSpace(contact.Email))
                        {
                            logger.LogDebug("Campaña {CampaignId}: contacto {Phone} sin email — se omite canal Email.",
                                campaignId, contact.PhoneNumber);
                            continue;
                        }
                        attempt = await SendEmailToContactAsync(campaign, contact, ct);
                    }
                    else if (channel == ChannelType.WhatsApp)
                    {
                        if (waProvider is null) continue; // sin línea, ya logueado arriba
                        if (string.IsNullOrWhiteSpace(contact.PhoneNumber)) continue;

                        string? message = null;
                        string? generationError = null;
                        if (messageGenerator is not null)
                        {
                            try
                            {
                                var genResult = await messageGenerator.GenerateAsync(campaign, contact, ct);
                                if (genResult.ErrorCode == MessageGenerationError.None)
                                {
                                    message = genResult.Text;
                                }
                                else
                                {
                                    // Mapeo de código de error a mensaje accionable para el admin del tenant.
                                    // Cada código tiene una causa raíz distinta y una acción correctiva distinta.
                                    generationError = genResult.ErrorCode switch
                                    {
                                        MessageGenerationError.NoApiKey =>
                                            "Falta configurar la API key del LLM en este tenant. " +
                                            "Acción: ir a Configuración → Tenant → pegar la API key de Anthropic y guardar. " +
                                            "Luego reanudar la campaña desde la página de Campañas.",
                                        MessageGenerationError.NoPrompt =>
                                            "El maestro de campaña no tiene prompt configurado. " +
                                            "Acción: ir a Maestros de Campaña → editar este maestro → tab Prompt → configurar el SystemPrompt y guardar.",
                                        MessageGenerationError.EmptyPromptTemplate =>
                                            "El PromptTemplate global asociado al maestro está vacío. " +
                                            $"Acción: ir a Admin → Prompts y configurar el contenido. Detalle: {genResult.Detail}",
                                        MessageGenerationError.NoContactData =>
                                            "El contacto no tiene los datos del archivo cargados (ContactDataJson vacío). " +
                                            "Acción: re-cargar el archivo de la campaña con los datos completos por columna. " +
                                            "Si el problema persiste, reportar al equipo de TI (problema en el procesador del archivo).",
                                        MessageGenerationError.LlmException =>
                                            $"El servicio del LLM respondió con error. Detalle: {Truncate(genResult.Detail, 250)}. " +
                                            "Verificar que la API key del tenant sea válida y el modelo esté disponible.",
                                        MessageGenerationError.EmptyLlmResponse =>
                                            "El LLM respondió con texto vacío. Esto suele indicar un prompt mal formado o sin contexto suficiente. " +
                                            "Acción: revisar el SystemPrompt del maestro y los datos del contacto.",
                                        _ => $"Error desconocido del generador IA: {genResult.ErrorCode}."
                                    };
                                    logger.LogWarning(
                                        "Campaña {CampaignId}: generación falló para {Phone} con código {Code}. Detalle: {Detail}",
                                        campaignId, contact.PhoneNumber, genResult.ErrorCode, genResult.Detail);
                                }
                            }
                            catch (Exception exGen)
                            {
                                generationError = $"Excepción inesperada del generador: {Truncate(exGen.Message, 250)}";
                                logger.LogWarning(exGen, "Campaña {CampaignId}: generación con Claude falló para {Phone}.",
                                    campaignId, contact.PhoneNumber);
                            }
                        }

                        // Guardrail post-LLM: detectar "output meta" (alertas técnicas, refusal,
                        // referencias al prompt) que el LLM emite a veces cuando se confunde con
                        // datos inconsistentes del contacto. Sin este check el cliente recibiría
                        // texto interno tipo "# ALERTA: CLIENTE FUERA DE SCOPE" en su WhatsApp.
                        // Si se detecta, forzamos message=null para que caiga al bloqueo de
                        // abajo con un detail específico que apunta al SystemPrompt del template.
                        if (!string.IsNullOrWhiteSpace(message))
                        {
                            var metaReason = DetectMetaOutput(message);
                            if (metaReason is not null)
                            {
                                logger.LogError(
                                    "Campaña {CampaignId}: el LLM emitió output META para {Phone} (no apto para cliente). Razón: {Reason}. Output (primeros 500 chars): {Output}",
                                    campaignId, contact.PhoneNumber, metaReason, Truncate(message, 500));
                                generationError = $"El LLM emitió un mensaje 'meta' no apto para el cliente ({metaReason}). " +
                                                  "Causa probable: el SystemPrompt del maestro no tiene reglas claras de formato e identidad — " +
                                                  "el LLM se confundió y emitió texto técnico/refusal en vez del mensaje al cliente. " +
                                                  "Acción: editar el maestro → tab Prompt → reforzar las reglas de tono y formato → guardar.";
                                message = null;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(message))
                        {
                            // El mensaje accionable ya viene en generationError (el switch
                            // por MessageGenerationError lo armó con la acción correctiva
                            // específica). Si llegamos acá sin generationError (caso raro:
                            // messageGenerator==null pero no debería pasar en prod), usamos
                            // un fallback genérico.
                            var finalError = !string.IsNullOrWhiteSpace(generationError)
                                ? generationError
                                : "No se generó mensaje (generador IA no disponible). Reportar al equipo de TI.";
                            attempt = new DispatchAttemptResult(
                                Success: false,
                                ExternalId: null,
                                Error: finalError,
                                SentContent: null,
                                Subject: null,
                                Recipient: contact.PhoneNumber);
                            logger.LogError(
                                "Campaña {CampaignId}: NO se envió mensaje a {Phone}. {Detail}",
                                campaignId, contact.PhoneNumber, finalError);
                        }
                        else
                        {
                            // ── Validación pre-envío: lista negra + UltraMsg /contacts/check ──
                            // Antes de gastar UN envío confirmamos que el número tiene WhatsApp.
                            //   - Lista negra ya registrada → ni siquiera consulta UltraMsg.
                            //   - UltraMsg responde invalid → se registra en BD + se skipea el envío.
                            //   - UltraMsg responde unknown (timeout) → fail-open: el envío real va.
                            // Solo aplica para UltraMsg. MetaCloud se omite (su API tiene su propia validación).
                            WhatsAppLine? activeLine = null;
                            if (waProvider.ProviderType == ProviderType.UltraMsg)
                            {
                                activeLine = await db.Set<WhatsAppLine>()
                                    .Where(l => l.TenantId == tenant.Id && l.IsActive)
                                    .FirstOrDefaultAsync(ct);
                            }

                            WhatsAppNumberValidationResult? preCheck = null;
                            if (activeLine is not null)
                            {
                                preCheck = await whatsAppValidator.ValidateBeforeSendAsync(
                                    contact.PhoneNumber, activeLine, tenant.Id, campaignId, ct);
                            }

                            if (preCheck is not null && !preCheck.IsValid)
                            {
                                logger.LogInformation(
                                    "Campaña {CampaignId}: contacto {Phone} NO enviado — {Source}: {Reason}",
                                    campaignId, contact.PhoneNumber, preCheck.Source, preCheck.Reason);
                                attempt = new DispatchAttemptResult(
                                    Success: false,
                                    ExternalId: null,
                                    Error: preCheck.Reason ?? "Número no entregable",
                                    SentContent: null,
                                    Subject: null,
                                    Recipient: contact.PhoneNumber);
                            }
                            else
                            {
                                var waResult = await waProvider.SendMessageAsync(
                                    new SendMessageRequest(contact.PhoneNumber, message), ct);

                                // Si el provider devolvió error de tipo "número no válido",
                                // registramos en lista negra para futuras campañas.
                                if (!waResult.Success && activeLine is not null && waResult.Error is not null
                                    && (waResult.Error.Contains("invalid", StringComparison.OrdinalIgnoreCase)
                                        || waResult.Error.Contains("not connected", StringComparison.OrdinalIgnoreCase)
                                        || waResult.Error.Contains("no entregado", StringComparison.OrdinalIgnoreCase)))
                                {
                                    try
                                    {
                                        // Registro por tenant — datasets diferentes por corredor.
                                        // El admin puede elevar a global desde el panel si aplica.
                                        await whatsAppValidator.RegisterAsBlacklistedAsync(
                                            contact.PhoneNumber,
                                            reason: $"Dispatch error: {Truncate(waResult.Error, 300)}",
                                            source: "dispatch-error",
                                            tenantId: tenant.Id,
                                            campaignId: campaignId,
                                            userId: null,
                                            ct: ct);
                                    }
                                    catch (Exception ex) { logger.LogWarning(ex, "[WANumberValidator] Register post-dispatch failed."); }
                                }

                                attempt = new DispatchAttemptResult(
                                    Success: waResult.Success,
                                    ExternalId: waResult.ExternalMessageId,
                                    Error: waResult.Error,
                                    SentContent: message,
                                    Subject: null,
                                    Recipient: contact.PhoneNumber);
                            }
                        }
                    }
                    else
                    {
                        // SMS u otro canal no soportado aún — saltamos.
                        logger.LogDebug("Campaña {CampaignId}: canal {Channel} no implementado en dispatcher — se omite.",
                            campaignId, channel);
                        continue;
                    }

                    if (attempt is null) continue;

                    if (attempt.Success)
                    {
                        channelSuccesses++;
                        primaryDispatch ??= attempt;  // primer éxito = "principal" para legacy fields
                        // Persistir Message para este canal específico.
                        try { await PersistOutboundConversationAsync(campaign, contact, channel, attempt, sendNow, ct); }
                        catch (Exception ex) { logger.LogError(ex, "Persistir mensaje {Channel} falló para {Recipient}", channel, attempt.Recipient); }
                        logger.LogDebug("Campaña {CampaignId}: enviado a {Recipient} via {Channel}",
                            campaignId, attempt.Recipient, channel);
                    }
                    else
                    {
                        channelFailures++;
                        lastError = attempt.Error ?? lastError;
                        logger.LogWarning("Campaña {CampaignId}: falló envío {Channel} a {Recipient}: {Error}",
                            campaignId, channel, attempt.Recipient, attempt.Error);
                    }
                }

                // ── Resolver estado del contacto según el agregado ─────────
                if (channelSuccesses > 0)
                {
                    contact.DispatchStatus = DispatchStatus.Sent;
                    contact.SentAt = sendNow;
                    contact.LastContactAt = sendNow;
                    contact.ExternalMessageId = primaryDispatch?.ExternalId;
                    contact.GeneratedMessage = primaryDispatch?.SentContent;
                    contact.DispatchAttempts++;
                    contact.DispatchError = null;
                    sent++;
                    consecutiveErrors = 0;

                    // ── Fix #3 — Re-validar salud de la línea cada N envíos exitosos ──
                    // Cinturón doble contra el caso 18/05: aunque el body venga "ok",
                    // si la línea cayó a mitad del batch este check lo atrapa antes de
                    // seguir bombardeando UltraMsg con requests que terminan en su cola.
                    if (waProvider is not null && sent > 0 && sent % LineHealthRecheckEveryN == 0)
                    {
                        var (stillHealthy, midReason) = await EnsureWhatsAppLineHealthyAsync(tenant.Id, ct);
                        if (!stillHealthy)
                        {
                            logger.LogWarning(
                                "Campaña {CampaignId}: línea WhatsApp cayó a mitad de batch tras {Sent} envíos ({Reason}). Pausando.",
                                campaignId, sent, midReason);
                            campaign.IsActive = false;
                            campaign.Status = CampaignStatus.Paused;
                            await db.SaveChangesAsync(ct);
                            try { await NotifyLineDownAsync(tenant, campaign, midReason!, ct); }
                            catch (Exception ex) { logger.LogWarning(ex, "[LineHealth] Notificación mid-batch falló."); }
                            break;
                        }
                    }
                }
                else if (channelFailures > 0)
                {
                    contact.DispatchAttempts++;
                    contact.DispatchError = Truncate(lastError, 2000);
                    contact.LastContactAt = sendNow;
                    failed++;
                    consecutiveErrors++;
                    if (contact.DispatchAttempts >= 3)
                    {
                        contact.DispatchStatus = DispatchStatus.Error;
                    }
                    else
                    {
                        // Volvemos a Queued con un retraso para que el próximo
                        // tick reintente. Retry sin ScheduledFor quedaba en
                        // limbo (la query de candidatos solo mira Queued).
                        contact.DispatchStatus = DispatchStatus.Queued;
                        contact.ScheduledFor = DateTime.UtcNow.AddMinutes(1);
                    }
                    contact.ClaimedAt = null;
                    if (consecutiveErrors >= ConsecutiveErrorThreshold)
                    {
                        logger.LogWarning("Campaña {CampaignId}: {Threshold}+ errores consecutivos. Frenando lote.",
                            campaignId, ConsecutiveErrorThreshold);
                        // Alerta a Teams: señal temprana (antes del auto-pause por %).
                        // Probablemente la línea acaba de caer mid-batch o UltraMsg está
                        // devolviendo sent:false/queued. Notificar para que el equipo
                        // mire antes de que se desperdicien más batches.
                        try
                        {
                            await teamsNotifier.NotifyAsync(
                                $"⚠️ Batch cortado por {ConsecutiveErrorThreshold} errores consecutivos\n" +
                                $"• Tenant: {tenant.Name}\n" +
                                $"• Campaña: {campaign.Name}\n" +
                                $"• Último error del provider: {Truncate(lastError, 200)}\n" +
                                $"• Acción: verificar estado de la línea WhatsApp; el dispatcher esperará al próximo cool-down antes de reintentar.",
                                ct);
                        }
                        catch (Exception ex) { logger.LogWarning(ex, "[Dispatcher] Teams notify falló (errores consecutivos)."); }
                        break;
                    }
                }
                else
                {
                    // Ningún canal aplicó (contacto sin email/phone/etc).
                    contact.DispatchStatus = DispatchStatus.Skipped;
                    contact.DispatchError = "Contacto sin datos para los canales habilitados (email/teléfono).";
                    contact.ClaimedAt = null;
                    logger.LogInformation("Campaña {CampaignId}: contacto {Phone} omitido — sin datos para los canales del agente.",
                        campaignId, contact.PhoneNumber);
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

            // Persistir estado del contacto + ProcessedContacts en vivo para que
            // el frontend pueda mostrar progreso 1/N, 2/N, ... mientras corre el lote.
            // (En lugar de actualizar solo al final del batch.)
            campaign.ProcessedContacts = await db.CampaignContacts
                .CountAsync(cc => cc.CampaignId == campaignId
                               && (cc.DispatchStatus == DispatchStatus.Sent
                                   || cc.DispatchStatus == DispatchStatus.Error
                                   || cc.DispatchStatus == DispatchStatus.Skipped
                                   || cc.DispatchStatus == DispatchStatus.Duplicate), ct);
            await db.SaveChangesAsync(ct);

            // ── Delay anti-ban: base = 60s/MessagesPerMinute, ±20% jitter ───────
            var jitter = (_random.NextDouble() * 0.4) - 0.2;     // [-0.2, +0.2]
            var actualDelay = Math.Max(MinDelaySecondsFloor, baseDelaySeconds * (1 + jitter));
            await Task.Delay(TimeSpan.FromSeconds(actualDelay), ct);
        }
        }
        finally
        {
            // Liberación inmediata del lote actual: si el foreach salió por
            // cancelación (Task.Delay arrojó OperationCanceledException) o por
            // una excepción que burbujeó, los contactos no procesados aún están
            // como DispatchStatus=Claimed en BD con NUESTRO ClaimedAt. Hay que
            // devolverlos a Queued para que el próximo tick (de este Worker o de
            // otro recién deployado) los retome al instante.
            //
            // ExecuteUpdate atómico bypassa el change tracker — funciona incluso
            // si hay cambios pendientes en el DbContext que se perdieron por la
            // excepción. Usamos CancellationToken.None a propósito: queremos que
            // este release corra incluso cuando el ct padre ya está cancelado
            // (apagado en curso). Si NO usamos None, el ExecuteUpdate también
            // se cancelaría y volvemos al bug original.
            try
            {
                var batchIds = pendingContacts.Select(c => c.Id).ToList();
                var releasedFromBatch = await db.CampaignContacts
                    .Where(cc => batchIds.Contains(cc.Id)
                              && cc.DispatchStatus == DispatchStatus.Claimed
                              && cc.ClaimedAt == claimedAt)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.DispatchStatus, DispatchStatus.Queued)
                        .SetProperty(c => c.ClaimedAt, (DateTime?)null), CancellationToken.None);
                if (releasedFromBatch > 0)
                    logger.LogInformation(
                        "Campaña {CampaignId}: liberados {N} contactos del lote actual " +
                        "(foreach terminó antes de procesarlos — cancellation o exception).",
                        campaignId, releasedFromBatch);
            }
            catch (Exception ex)
            {
                // Best-effort: si esto falla, el recovery sweep de 5 min del
                // próximo tick los rescata igual. No quiero comerme la excepción
                // original del foreach por culpa del cleanup.
                logger.LogError(ex, "Campaña {CampaignId}: error liberando huérfanos del lote en finally.", campaignId);
            }
        }

        // ── 8. Actualizar contadores de la campaña ───────
        campaign.ProcessedContacts = await db.CampaignContacts
            .CountAsync(cc => cc.CampaignId == campaignId
                           && (cc.DispatchStatus == DispatchStatus.Sent
                               || cc.DispatchStatus == DispatchStatus.Error
                               || cc.DispatchStatus == DispatchStatus.Skipped
                               || cc.DispatchStatus == DispatchStatus.Duplicate), ct);

        // ── 8.b Cool-down + Circuit breaker (Phase 3) ────────────────────
        // Solo aplican si el batch movió al menos un mensaje (sent O failed).
        // Si no envió nada (fuera de horario, sin Queued, etc.) NO seteamos
        // cool-down — eso aplazaría innecesariamente el próximo intento.
        var processedThisBatch = sent + failed;
        if (processedThisBatch > 0)
        {
            // Circuit breaker: si la tasa de fallo de ESTE batch supera el
            // umbral configurado, auto-pausar el Campaign + alerta al admin.
            // 0% = deshabilitado.
            var threshold = tenant.CampaignAutoPauseFailureRate;
            if (threshold > 0)
            {
                var failureRate = (decimal)failed * 100m / processedThisBatch;
                if (failureRate >= threshold)
                {
                    campaign.IsActive = false;
                    campaign.Status   = CampaignStatus.Paused;
                    logger.LogWarning(
                        "Campaña {CampaignId}: AUTO-PAUSADA. Batch terminó con " +
                        "{Failed}/{Total} fallidos ({Rate:F1}% >= {Threshold:F1}% umbral). " +
                        "Posible cuenta WhatsApp restringida o lista con problemas. " +
                        "Revisar antes de reanudar.",
                        campaignId, failed, processedThisBatch, failureRate, threshold);
                    // Email al admin del tenant — fire-and-forget para no bloquear.
                    _ = NotifyCampaignAutoPausedAsync(tenant.Id, campaign, failed, processedThisBatch, failureRate);
                }
            }

            // Si la campaña NO se auto-pausó, aplicar cool-down para el próximo batch.
            if (campaign.Status != CampaignStatus.Paused)
            {
                var cooldownMin = Math.Max(0, tenant.CampaignBatchCoolDownMinutes);
                if (cooldownMin > 0)
                {
                    campaign.NextBatchAfterUtc = DateTime.UtcNow.AddMinutes(cooldownMin);
                    logger.LogInformation(
                        "Campaña {CampaignId}: batch completado ({Sent}✓ + {Failed}✗). " +
                        "Próximo batch en {Min} min ({NextUtc:o}).",
                        campaignId, sent, failed, cooldownMin, campaign.NextBatchAfterUtc.Value);
                }
            }
        }

        await db.SaveChangesAsync(ct);

        var stopReason = consecutiveErrors >= ConsecutiveErrorThreshold ? DispatchStopReason.TooManyErrors
            : sentToday + sent >= maxPerDay ? DispatchStopReason.DailyLimitReached
            : DispatchStopReason.BatchCompleted;

        logger.LogInformation(
            "Campaña {CampaignId}: lote finalizado. Enviados={Sent}, Fallidos={Failed}, Razón={Reason}",
            campaignId, sent, failed, stopReason);

        return new DispatchResult(sent, failed, null, stopReason);
    }

    /// <summary>
    /// Best-effort: notificar al admin del tenant que una campaña se auto-pausó
    /// por superar el umbral de fallos. No bloqueamos el dispatcher — si el
    /// email falla, lo logueamos pero seguimos.
    /// </summary>
    private async Task NotifyCampaignAutoPausedAsync(
        Guid tenantId, Campaign campaign, int failed, int total, decimal rate)
    {
        try
        {
            // ── Notificación a Microsoft Teams (Power Automate) ─────────────
            // Best-effort independiente del email: el equipo operativo lo ve
            // inmediato en el canal. Se cubre TODO el escenario del 18/05:
            // - Linea muerta y UltraMsg responde sent:false/queued → fallos por
            //   contacto → este auto-pause por % se dispara → Teams alert.
            // - Aunque #2 y #3 (pre/mid-check de salud) ya pausen primero,
            //   este es la última red de seguridad si el ping de UltraMsg
            //   responde "authenticated" pero el envío real está fallando
            //   por throttling de Meta u otra causa no detectable vía status.
            try
            {
                var tenantName = campaign.Tenant?.Name ?? tenantId.ToString();
                await teamsNotifier.NotifyAsync(
                    $"⚠️ Campaña auto-pausada por alta tasa de fallos\n" +
                    $"• Tenant: {tenantName}\n" +
                    $"• Campaña: {campaign.Name}\n" +
                    $"• Resultado del último batch: {failed}/{total} fallidos ({rate:F1}%)\n" +
                    $"• Causa probable: línea WhatsApp restringida por Meta, lista con números inválidos, o UltraMsg encolando sin entregar.\n" +
                    $"• Acción: revisar el estado de la línea y las estadísticas de la campaña antes de reanudar.",
                    CancellationToken.None);
            }
            catch (Exception teamsEx)
            {
                logger.LogWarning(teamsEx, "[AutoPause] Teams notify falló (no bloqueante).");
            }

            // Buscar admins/supervisores del tenant para notificar
            var adminEmails = await db.AppUsers
                .Where(u => u.TenantId == tenantId && u.IsActive
                         && (u.Role == AgentFlow.Domain.Entities.UserRole.Admin
                             || u.Role == AgentFlow.Domain.Entities.UserRole.Supervisor))
                .Select(u => u.Email)
                .ToListAsync();

            if (adminEmails.Count == 0)
            {
                logger.LogWarning(
                    "Campaña {CampaignId}: auto-pausada pero no hay admins del tenant {TenantId} a quien notificar.",
                    campaign.Id, tenantId);
                return;
            }

            var subject = $"⚠️ Campaña '{campaign.Name}' auto-pausada por alta tasa de fallos";
            var body = $@"<p>Hola,</p>
<p>La campaña <strong>{campaign.Name}</strong> fue <strong>pausada automáticamente</strong>
por el sistema de protección anti-restricción.</p>
<p><strong>Motivo:</strong> El último lote procesó {total} mensajes con
<strong>{failed} fallidos ({rate:F1}%)</strong>, superando el umbral configurado de
{campaign.Tenant?.CampaignAutoPauseFailureRate ?? 30m:F1}%.</p>
<p><strong>Causas probables:</strong></p>
<ul>
<li>La cuenta WhatsApp del tenant está temporalmente restringida por Meta.</li>
<li>La lista contiene una proporción alta de números inválidos o desactivados.</li>
<li>Los mensajes están siendo rechazados por contenido (template demasiado promocional).</li>
</ul>
<p><strong>Próximos pasos:</strong></p>
<ol>
<li>Verificar el estado del número en el dashboard UltraMsg.</li>
<li>Revisar las estadísticas de la campaña (botón 'Estadísticas' en la página).</li>
<li>Si la causa fue puntual, reanudar la campaña manualmente.</li>
</ol>
<p style=""color:#666;font-size:12px;"">AgentFlow — Notificación automática del Circuit Breaker.</p>";

            foreach (var email in adminEmails)
            {
                try
                {
                    await emailService.SendCustomHtmlAsync(
                        toEmail:  email,
                        ccEmail:  null,
                        subject:  subject,
                        htmlBody: body,
                        textBody: null,
                        ct:       CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "No se pudo enviar email de auto-pausa a {Email}", email);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error en NotifyCampaignAutoPausedAsync para campaña {CampaignId}", campaign.Id);
        }
    }

    private static string? Truncate(string? s, int max) =>
        s is null ? null : s.Length <= max ? s : s[..max];

    /// <summary>
    /// Patrones que indican que el LLM emitió un mensaje "meta" — dirigido al
    /// desarrollador/admin o auto-referencial al prompt — en vez de un mensaje
    /// conversacional para el cliente final. Mantener conservador: solo frases
    /// de muy alta señal que NUNCA aparecen en mensajes legítimos a clientes
    /// de campañas comerciales o de cobros.
    /// </summary>
    private static readonly string[] MetaOutputPhrases =
    {
        "estimado desarrollador",
        "estimado admin",
        "estimado developer",
        "alerta: cliente",
        "alerta cliente fuera",
        "según mi identidad",
        "segun mi identidad",
        "según mi prompt",
        "segun mi prompt",
        "según el prompt",
        "segun el prompt",
        "según mi system prompt",
        "según mi rol",
        "no puedo redactar este mensaje",
        "no puedo redactar el mensaje",
        "fuera de scope",
        "fuera del scope",
        "sección 1 del prompt",
        "seccion 1 del prompt",
        "sección 2 del prompt",
        "seccion 2 del prompt",
        "acciones recomendadas:",
        "## el problema:",
    };

    /// <summary>
    /// Devuelve null si el mensaje parece OK para enviar al cliente.
    /// Devuelve la razón si detecta un output meta que NO debe ser enviado.
    /// </summary>
    private static string? DetectMetaOutput(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        // Encabezado Markdown al inicio (#, ##, ###) — los mensajes de WhatsApp
        // legítimos no empiezan con encabezados Markdown técnicos.
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith("# ", StringComparison.Ordinal)
            || trimmed.StartsWith("## ", StringComparison.Ordinal)
            || trimmed.StartsWith("### ", StringComparison.Ordinal))
        {
            return "el mensaje empieza con encabezado Markdown (#, ##, ###) — patrón típico de output meta, no de mensaje conversacional";
        }

        var lower = content.ToLowerInvariant();
        foreach (var phrase in MetaOutputPhrases)
        {
            if (lower.Contains(phrase))
                return $"contiene frase reservada para output interno: '{phrase}'";
        }

        return null;
    }

    /// <summary>
    /// Crea la Conversation + Message correspondiente al primer envío de la campaña.
    /// Sin esto, el monitor solo ve el contacto cuando el cliente responde.
    /// Replica la lógica de N8nCallbackController para mantener paridad con el flujo legacy.
    /// </summary>
    /// <summary>
    /// Resultado de un intento de envío unificado (WhatsApp o Email). El dispatcher
    /// usa esta forma común para no duplicar la lógica de persistencia/manejo
    /// de errores por canal.
    /// </summary>
    private sealed record DispatchAttemptResult(
        bool Success,
        string? ExternalId,
        string? Error,
        string SentContent,    // texto plano del mensaje o body del email (para mostrar en monitor)
        string? Subject,       // solo Email
        string? Recipient);    // dirección de email o phone

    /// <summary>
    /// Envía un correo al contacto renderizando la plantilla del maestro
    /// (EmailBodyHtml/Subject) con los datos de la fila (CampaignContact).
    /// </summary>
    private async Task<DispatchAttemptResult> SendEmailToContactAsync(
        Campaign campaign,
        CampaignContact contact,
        CancellationToken ct)
    {
        var template = campaign.CampaignTemplate!;
        var toEmail = contact.Email ?? string.Empty;
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            return new DispatchAttemptResult(
                Success: false,
                ExternalId: null,
                Error: "El contacto no tiene email registrado.",
                SentContent: string.Empty,
                Subject: null,
                Recipient: null);
        }

        // Nombre del agente (para AgentName del Message).
        var agentName = await db.AgentDefinitions
            .Where(a => a.Id == campaign.AgentDefinitionId)
            .Select(a => (string?)(a.AvatarName ?? a.Name))
            .FirstOrDefaultAsync(ct);

        var tenantInfo = await db.Tenants
            .Where(t => t.Id == campaign.TenantId)
            .Select(t => new { t.Name, t.LogoUrl })
            .FirstOrDefaultAsync(ct);
        var tenantName    = tenantInfo?.Name;
        var tenantLogoUrl = tenantInfo?.LogoUrl;

        var renderCtx = new EmailRenderContext
        {
            ClienteNombre      = contact.ClientName ?? contact.PhoneNumber,
            ClienteTelefono    = contact.PhoneNumber,
            ClienteEmail       = contact.Email,
            ClientePoliza      = contact.PolicyNumber,
            ClienteAseguradora = contact.InsuranceCompany,
            ClienteSaldo       = contact.PendingAmount?.ToString("C2"),
            ClienteDatosJson   = contact.ContactDataJson,

            // En envío inicial NO hay conversación todavía — variables conversacion.*
            // quedan vacías. La plantilla Scriban debe tolerarlo (ya lo hace).
            ConversacionResumen      = string.Empty,
            ConversacionMensajesHtml = string.Empty,
            ConversacionEstado       = "Vigente",

            CampanaNombre = campaign.Name,
            AgenteNombre  = agentName,
            TenantNombre  = tenantName,
            TenantLogoUrl = tenantLogoUrl,

            Fecha = DateTime.UtcNow.ToString("dd/MM/yyyy"),
            Hora  = DateTime.UtcNow.ToString("HH:mm"),

            ItemsConfigJson   = template.ItemsConfig,
            UmbralCorporativo = template.UmbralCorporativo,
        };

        var subjectTemplate = !string.IsNullOrWhiteSpace(template.EmailSubject)
            ? template.EmailSubject
            : $"Mensaje de {tenantName ?? campaign.Name}";

        var rendered = emailRenderer.Render(
            subjectTemplate, template.EmailBodyHtml, template.EmailBodyText, renderCtx);

        try
        {
            var externalId = await emailService.SendCustomHtmlAsync(
                toEmail, ccEmail: null,
                rendered.Subject, rendered.HtmlBody, rendered.TextBody,
                ct);
            return new DispatchAttemptResult(
                Success: true,
                ExternalId: externalId,
                Error: null,
                SentContent: rendered.HtmlBody,
                Subject: rendered.Subject,
                Recipient: $"to={toEmail}");
        }
        catch (Exception ex)
        {
            return new DispatchAttemptResult(
                Success: false,
                ExternalId: null,
                Error: ex.Message,
                SentContent: rendered.HtmlBody,
                Subject: rendered.Subject,
                Recipient: $"to={toEmail}");
        }
    }

    /// <summary>
    /// Crea (si no existe) UNA Conversation por contacto+campaña y agrega el
    /// Message de este envío. Es idempotente: si la conversación ya existe (de
    /// un envío previo por otro canal en la misma iteración), reutiliza esa
    /// y solo agrega el mensaje. Channel del Message viene del parámetro
    /// explícito — el Channel de la Conversation se setea con el "primario"
    /// del agente (WhatsApp tiene prioridad si está en la lista).
    /// </summary>
    private async Task PersistOutboundConversationAsync(
        Campaign campaign,
        CampaignContact contact,
        ChannelType messageChannel,
        DispatchAttemptResult dispatch,
        DateTime sentAtUtc,
        CancellationToken ct)
    {
        var tenantId = campaign.TenantId;
        var phone    = contact.PhoneNumber;

        // ¿Ya hay una conversación activa abierta por este mismo dispatch?
        // (cuando enviamos por 2 canales en la misma iteración, el segundo
        // canal debe agregar Message a la misma Conversation que el primero
        // creó.)
        var existing = await db.Conversations
            .Where(c => c.TenantId == tenantId
                     && c.ClientPhone == phone
                     && c.CampaignId == campaign.Id
                     && c.Status != ConversationStatus.Closed)
            .OrderByDescending(c => c.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            existing.LastActivityAt = sentAtUtc;
            db.Set<Message>().Add(new Message
            {
                Id                = Guid.NewGuid(),
                ConversationId    = existing.Id,
                Direction         = MessageDirection.Outbound,
                Status            = MessageStatus.Sent,
                Content           = dispatch.SentContent,
                ExternalMessageId = dispatch.ExternalId,
                Channel           = messageChannel,
                Subject           = dispatch.Subject,
                Recipient         = dispatch.Recipient,
                IsFromAgent       = true,
                AgentName         = campaign.CampaignTemplate?.Name ?? campaign.Name,
                SentAt            = sentAtUtc,
            });
            await db.SaveChangesAsync(ct);
            return;
        }

        // 1. Cerrar conversaciones activas previas del mismo número (excepto las
        //    que están siendo atendidas por un humano). Mantiene el invariante de
        //    "una sola conversación abierta por contacto".
        var previousActive = await db.Conversations
            .Where(c => c.TenantId == tenantId
                     && c.ClientPhone == phone
                     && c.Status != ConversationStatus.Closed
                     && !c.IsHumanHandled)
            .ToListAsync(ct);

        foreach (var prev in previousActive)
        {
            prev.Status         = ConversationStatus.Closed;
            prev.ClosedAt       = sentAtUtc;
            prev.LastActivityAt = sentAtUtc;

            db.Set<GestionEvent>().Add(new GestionEvent
            {
                Id             = Guid.NewGuid(),
                ConversationId = prev.Id,
                Result         = GestionResult.Pending,
                Origin         = "system:campaign-superseded",
                Notes          = $"Conversación cerrada automáticamente por nueva campaña {campaign.Name} ({campaign.Id})",
                OccurredAt     = sentAtUtc,
            });
        }

        // 2. Crear la conversación nueva. Channel se setea con el canal del
        // mensaje que la abrió (no con campaign.Channel — que era el modelo
        // viejo donde la campaña tenía un único canal). Esto permite que el
        // Monitor filtre correctamente por canal.
        var conversation = new Conversation
        {
            Id             = Guid.NewGuid(),
            TenantId       = tenantId,
            ClientPhone    = phone,
            ClientName     = contact.ClientName,
            PolicyNumber   = contact.PolicyNumber,
            Channel        = messageChannel,
            ActiveAgentId  = campaign.AgentDefinitionId,
            CampaignId     = campaign.Id,
            Status         = ConversationStatus.WaitingClient,
            StartedAt      = sentAtUtc,
            LastActivityAt = sentAtUtc,
        };
        db.Conversations.Add(conversation);

        // 3. Persistir el mensaje saliente — Channel del Message viene del
        // canal específico que se acaba de enviar (puede diferir del
        // Conversation.Channel cuando el agente tiene varios habilitados).
        db.Set<Message>().Add(new Message
        {
            Id                = Guid.NewGuid(),
            ConversationId    = conversation.Id,
            Direction         = MessageDirection.Outbound,
            Status            = MessageStatus.Sent,
            Content           = dispatch.SentContent,
            ExternalMessageId = dispatch.ExternalId,
            Channel           = messageChannel,
            Subject           = dispatch.Subject,
            Recipient         = dispatch.Recipient,
            IsFromAgent       = true,
            AgentName         = campaign.CampaignTemplate?.Name ?? campaign.Name,
            SentAt            = sentAtUtc,
        });

        // 4. Reflejar el envío en el ContactGroup de morosidad (Descargas → Enviado).
        // Idempotente — solo marca grupos que aún no tenían FirstMessageSentAt.
        // Sin esto la columna "Enviado" en la pantalla Descargas queda vacía aunque
        // el WhatsApp ya salió. Replica la lógica del N8nCallbackController.
        var groupsToMark = await db.ContactGroups
            .Where(g => g.CampaignId == campaign.Id
                     && g.PhoneNormalized == phone
                     && g.FirstMessageSentAt == null)
            .ToListAsync(ct);
        foreach (var g in groupsToMark)
        {
            g.FirstMessageSentAt = sentAtUtc;
            if (g.Status == ContactGroupStatus.Pending || g.Status == ContactGroupStatus.CampaignCreated)
                g.Status = ContactGroupStatus.MessageSent;
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Modelo viejo (RETIRADO): jobs DelayFromEvent uno por contacto/campaña ──
    //
    // Antes acá había:
    //   ScheduleFollowUpsForContactAsync(...)  — creaba N jobs FOLLOW_UP_MESSAGE
    //   ScheduleAutoCloseAsync(...)            — creaba 1 job AUTO_CLOSE_CAMPAIGN
    //
    // Eso saturaba ScheduledWebhookJobs (un row por cada (contacto × follow-up) +
    // uno por campaña). Migramos al modelo "sweeper": dos cron jobs globales
    // (FOLLOW_UP_SWEEP cada 5 min, AUTO_CLOSE_CAMPAIGN_SWEEP cada 30 min) que
    // recorren CampaignContacts y Campaigns en cada tick. La tabla queda con
    // tamaño constante sin importar cuántas campañas se lancen.
    //
    // Si necesitás revertir, ver commits anteriores a este — el código vive en
    // la historia de git.

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
    /// Verifica si estamos dentro del horario de envío para esta campaña, priorizando
    /// <c>CampaignTemplate.SendFrom/SendUntil</c> ("Horario de envío" del frontend)
    /// sobre el horario por defecto del tenant. Retorna también un diagnóstico
    /// legible con la hora efectiva — útil para debuguear discrepancias TZ
    /// servidor/frontend.
    ///
    /// IMPORTANTE: NO usa <c>AttentionStartTime/EndTime</c> — esos son el horario de
    /// "atención de asesores humanos" (otra cosa: define cuándo un humano puede
    /// retomar la conversación). El horario que controla el envío masivo de la
    /// campaña son <c>SendFrom/SendUntil</c>.
    /// </summary>
    private static (bool Within, string Reason) IsWithinBusinessHours(Campaign campaign, Tenant tenant)
    {
        // 1) Resolver ventana — template.SendFrom/Until tiene prioridad si está configurado.
        TimeOnly start; TimeOnly end; string source;
        var t = campaign.CampaignTemplate;
        if (t is not null
            && !string.IsNullOrWhiteSpace(t.SendFrom)
            && !string.IsNullOrWhiteSpace(t.SendUntil)
            && TimeOnly.TryParse(t.SendFrom, out start)
            && TimeOnly.TryParse(t.SendUntil, out end))
        {
            source = "CampaignTemplate.SendFrom/Until";
        }
        else
        {
            start = tenant.BusinessHoursStart;
            end = tenant.BusinessHoursEnd;
            source = "Tenant.BusinessHours";
        }

        // 2) Resolver TZ con fallbacks (Windows IDs vs IANA names — varía por SO/runtime).
        var tz = ResolveTimeZone(tenant.TimeZone);
        var nowInTz = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var currentTime = TimeOnly.FromDateTime(nowInTz);

        // 3) Hora dentro del rango. Soporta ventanas que cruzan medianoche
        //    (ej: SendFrom=22:00, SendUntil=06:00 = ventana nocturna).
        var inRange = start <= end
            ? currentTime >= start && currentTime <= end
            : currentTime >= start || currentTime <= end;
        if (!inRange)
            return (false,
                $"hora {currentTime:HH:mm} fuera de [{start:HH:mm}-{end:HH:mm}] " +
                $"(source={source}, tz={tz.Id}, now={nowInTz:yyyy-MM-dd HH:mm})");

        return (true,
            $"dentro [{start:HH:mm}-{end:HH:mm}] (source={source}, tz={tz.Id}, now={currentTime:HH:mm})");
    }

    /// <summary>
    /// Resuelve la TimeZoneInfo aceptando tanto IDs IANA ("America/Panama") como
    /// Windows ("SA Pacific Standard Time"). En .NET 10 ambos formatos funcionan
    /// nativamente, pero en runtimes/OS más viejos hace falta convertir.
    /// </summary>
    private static TimeZoneInfo ResolveTimeZone(string? tzId)
    {
        if (string.IsNullOrWhiteSpace(tzId)) return TimeZoneInfo.Utc;
        if (TimeZoneInfo.TryFindSystemTimeZoneById(tzId, out var direct) && direct is not null)
            return direct;
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(tzId, out var winId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(winId, out var winTz) && winTz is not null)
            return winTz;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(tzId, out var ianaId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out var ianaTz) && ianaTz is not null)
            return ianaTz;
        return TimeZoneInfo.Utc;
    }

    /// <summary>
    /// Verifica que la línea WhatsApp del tenant esté "authenticated" antes de
    /// enviar mensajes. Pingea UltraMsg /instance/status para la línea activa.
    /// Devuelve (false, motivo) cuando hay que abortar el batch.
    ///
    /// Solo aplica al provider UltraMsg. Para MetaCloudApi devuelve siempre
    /// (true, null) porque Meta tiene mejores garantías del lado del provider.
    ///
    /// Persistir LastStatus en la WhatsAppLine también sirve al monitor diario:
    /// si el dispatcher detecta la caída a las 11 AM, el badge en la UI ya lo
    /// refleja sin esperar al ping de las 6 AM siguiente.
    /// </summary>
    private async Task<(bool Healthy, string? Reason)> EnsureWhatsAppLineHealthyAsync(
        Guid tenantId, CancellationToken ct)
    {
        var line = await db.Set<WhatsAppLine>()
            .Where(l => l.TenantId == tenantId && l.IsActive)
            .FirstOrDefaultAsync(ct);

        if (line is null)
            return (false, "Sin línea WhatsApp activa para el tenant.");

        // Solo validamos UltraMsg. MetaCloudApi tiene mejor manejo de errores
        // y no necesita pre-check (su API responde 4xx cuando la cuenta está mal).
        if (line.Provider != ProviderType.UltraMsg)
            return (true, null);

        UltraMsgInstanceStatus status;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            status = await ultraMsgInstance.GetStatusAsync(line.InstanceId, line.ApiToken, cts.Token);
        }
        catch (Exception ex)
        {
            // Timeout o error de red — NO podemos confirmar que la línea esté sana.
            // Política conservadora: fail-closed. Mejor abortar batch que arriesgar
            // bombardear UltraMsg si la línea está caída y solo no respondió a tiempo.
            logger.LogWarning(ex, "[LineHealth] Ping a UltraMsg falló para tenant {TenantId}.", tenantId);
            line.ConsecutivePingFailures++;
            line.LastStatusCheckedAt = DateTime.UtcNow;
            try { await db.SaveChangesAsync(ct); } catch { }
            return (false, $"No se pudo verificar el estado de la línea: {ex.Message}");
        }

        // Persistir el resultado del ping (igual que hace el job diario).
        line.LastStatus = status.Status;
        line.LastStatusCheckedAt = DateTime.UtcNow;
        if (string.Equals(status.Status, "authenticated", StringComparison.OrdinalIgnoreCase))
            line.ConsecutivePingFailures = 0;
        else
            line.ConsecutivePingFailures++;
        try { await db.SaveChangesAsync(ct); } catch { }

        if (!string.Equals(status.Status, "authenticated", StringComparison.OrdinalIgnoreCase))
            return (false, $"Línea '{line.DisplayName}' en estado '{status.Status}' (no authenticated).");

        return (true, null);
    }

    /// <summary>
    /// Notificación best-effort al Admin del tenant cuando una campaña se pausa
    /// porque su línea WhatsApp cayó. Se invoca desde 2 sitios: pre-check inicial
    /// y re-check mid-batch. Best-effort: si el email falla, NO bloquea el flujo
    /// (la campaña ya quedó Pausada con su Status, suficiente para que el equipo
    /// lo vea en la UI cuando entren).
    /// </summary>
    private async Task NotifyLineDownAsync(
        Tenant tenant, Campaign campaign, string reason, CancellationToken ct)
    {
        // ── Notificación a Microsoft Teams (Power Automate) ─────────────────
        // El equipo operativo necesita enterarse INMEDIATO, no esperar al email.
        // Best-effort: si Teams falla, igual mandamos el email a Admin/Supervisor.
        try
        {
            await teamsNotifier.NotifyAsync(
                $"⚠️ Campaña pausada por línea WhatsApp caída\n" +
                $"• Tenant: {tenant.Name}\n" +
                $"• Campaña: {campaign.Name}\n" +
                $"• Motivo: {reason}\n" +
                $"• Acción: reconectar la línea desde Configuración → WhatsApp, luego reanudar la campaña.",
                ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "[LineHealth] Teams notify falló."); }

        var recipients = await db.Set<AppUser>()
            .Where(u => u.TenantId == tenant.Id && u.IsActive
                     && (u.Role == UserRole.Admin || u.Role == UserRole.Supervisor))
            .Select(u => u.Email)
            .ToListAsync(ct);

        if (recipients.Count == 0)
        {
            logger.LogWarning("[LineHealth] Tenant {Tenant} sin Admin/Supervisor activos — no se notifica caída.",
                tenant.Name);
            return;
        }

        var subject = $"⚠️ Campaña pausada — línea WhatsApp de {tenant.Name} sin conexión";
        var html = $"""
<!DOCTYPE html><html><body style="font-family:Arial,sans-serif;max-width:640px;margin:0 auto;padding:24px;color:#333;">
  <table cellpadding="0" cellspacing="0" width="100%" style="background:#b91c1c;color:#fff;border-radius:6px;margin-bottom:20px;">
    <tr><td bgcolor="#b91c1c" style="background:#b91c1c;color:#fff;padding:20px;">
      <h2 style="margin:0;color:#fff;">⚠️ Campaña pausada automáticamente</h2>
    </td></tr>
  </table>
  <p>La campaña <b>{System.Net.WebUtility.HtmlEncode(campaign.Name)}</b> se pausó automáticamente porque la línea WhatsApp del tenant <b>{System.Net.WebUtility.HtmlEncode(tenant.Name)}</b> no está conectada.</p>
  <div style="background:#fff7ed;border-left:4px solid #f59e0b;padding:12px 16px;margin:16px 0;">
    <p style="margin:0;"><b>Motivo:</b> {System.Net.WebUtility.HtmlEncode(reason)}</p>
  </div>
  <p><b>Qué hacer:</b></p>
  <ol>
    <li>Ingresa al portal y ve a <b>Configuración → WhatsApp</b>.</li>
    <li>Presiona <b>Vincular</b> en la línea afectada y escanea el QR.</li>
    <li>Una vez reconectada, presiona <b>Reanudar</b> en la campaña pausada.</li>
  </ol>
  <p style="margin-top:24px;color:#6b7280;font-size:12px;">Esta pausa automática protege el número de un posible bloqueo por parte de Meta cuando UltraMsg encola mensajes sin entregar.</p>
</body></html>
""";

        foreach (var to in recipients)
        {
            try { await emailService.SendCustomHtmlAsync(to, ccEmail: null, subject, html, textBody: null, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "[LineHealth] Email a {Email} falló.", to); }
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
