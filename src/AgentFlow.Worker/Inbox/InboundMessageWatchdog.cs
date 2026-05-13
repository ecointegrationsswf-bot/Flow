using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Worker.Inbox;

/// <summary>
/// Watchdog "ningún cliente sin respuesta".
///
/// Cada tick (30s) detecta mensajes que llevan más de StuckThresholdSeconds
/// sin alcanzar Replied/Escalated y los rescata:
///   1. Verifica si hubo respuesta posterior por otra vía (race). Si sí, marca Replied.
///   2. Si no, envía un canned al cliente vía el IChannelProvider del tenant.
///   3. Marca la conversación con Status=EscalatedToHuman + IsHumanHandled=true.
///   4. Registra GestionEvent con Origin="watchdog:auto-escalation" para auditoría.
///   5. Marca el item de la cola como Escalated.
///
/// El monitor del tenant ya muestra conversaciones EscalatedToHuman, así que
/// el ejecutivo verá la escalación al refrescar (o por SignalR si está conectado).
///
/// Vive en el Worker on-prem: el AppPool de Smartasp puede morir, pero este
/// watchdog sigue corriendo y garantiza que cada cliente reciba algo en máximo
/// StuckThresholdSeconds + 30s.
/// </summary>
public class InboundMessageWatchdog(
    IServiceScopeFactory scopeFactory,
    ILogger<InboundMessageWatchdog> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private const int StuckThresholdSeconds = 120; // 2 minutos — la garantía dura

    /// <summary>
    /// Mensaje canned por defecto. Se puede mover a config por tenant más adelante,
    /// pero en español y neutral basta para el Día 2.
    /// </summary>
    private const string CannedMessage =
        "Hemos recibido tu mensaje y lo estamos revisando. " +
        "Un asesor te atenderá a la brevedad.";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "[InboundWatchdog] Arrancando. Tick {Tick}s, threshold {Threshold}s.",
            (int)TickInterval.TotalSeconds, StuckThresholdSeconds);

        // Pequeño delay inicial para dejar que el resto del host termine de levantarse.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[InboundWatchdog] Tick falló — reintentando en {Sec}s.",
                    (int)TickInterval.TotalSeconds);
            }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        logger.LogInformation("[InboundWatchdog] Detenido.");
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var queue = sp.GetRequiredService<IInboundMessageQueue>();
        var stuck = await queue.FindStuckAsync(StuckThresholdSeconds, ct);
        if (stuck.Count == 0) return;

        logger.LogWarning("[InboundWatchdog] {N} mensajes stuck detectados.", stuck.Count);

        foreach (var item in stuck)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await RescueAsync(item, sp, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "[InboundWatchdog] Falló rescate de {Id} tenant={Tenant} phone={Phone}",
                    item.Id, item.TenantId, item.FromPhone);
            }
        }
    }

    private async Task RescueAsync(InboundMessageQueueItem item, IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AgentFlowDbContext>();
        var queue = sp.GetRequiredService<IInboundMessageQueue>();
        var factory = sp.GetRequiredService<IChannelProviderFactory>();

        // ── Paso 1: ¿hubo respuesta posterior por otra vía? ─────────────────
        // Pregunta a la BD si existe un Message Outbound en alguna Conversation
        // del tenant para este teléfono creado DESPUÉS de FirstReceivedAt.
        // Si sí, el sistema sí respondió pero no marcamos Replied (race). Marcamos
        // y salimos sin escalar.
        var hasLaterReply = await db.Messages
            .Where(m => m.Direction == MessageDirection.Outbound
                     && m.SentAt >= item.FirstReceivedAt
                     && m.Conversation.TenantId == item.TenantId
                     && m.Conversation.ClientPhone == item.FromPhone)
            .AnyAsync(ct);

        if (hasLaterReply)
        {
            logger.LogInformation(
                "[InboundWatchdog] {Id} ya tenía respuesta posterior — marcando Replied.", item.Id);
            await queue.MarkRepliedAsync(item.Id, outboundMessageId: null, ct);
            return;
        }

        // ── Paso 2: enviar canned al cliente ────────────────────────────────
        IChannelProvider? provider = null;
        try
        {
            provider = item.WhatsAppLineId.HasValue
                ? await factory.GetProviderByLineAsync(item.WhatsAppLineId.Value, ct)
                : await factory.GetProviderAsync(item.TenantId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[InboundWatchdog] No pude resolver provider para tenant={Tenant}", item.TenantId);
        }

        if (provider is null)
        {
            logger.LogWarning(
                "[InboundWatchdog] Sin provider para tenant={Tenant} line={Line} — escalo sin canned.",
                item.TenantId, item.WhatsAppLineId);
        }
        else
        {
            try
            {
                var sendResult = await provider.SendMessageAsync(
                    new SendMessageRequest(To: item.FromPhone, Body: CannedMessage),
                    ct);
                if (!sendResult.Success)
                {
                    logger.LogWarning(
                        "[InboundWatchdog] Canned NO se envió a {Phone}: {Error}",
                        item.FromPhone, sendResult.Error);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[InboundWatchdog] Falló envío de canned a {Phone}", item.FromPhone);
                // Continuamos con la escalación aunque no haya canned: lo importante
                // es que el humano vea la conversación.
            }
        }

        // ── Paso 3: marcar Conversation como EscalatedToHuman + GestionEvent ─
        var conversation = await db.Conversations
            .Where(c => c.TenantId == item.TenantId && c.ClientPhone == item.FromPhone)
            .OrderByDescending(c => c.LastActivityAt)
            .FirstOrDefaultAsync(ct);

        if (conversation is not null)
        {
            conversation.Status = ConversationStatus.EscalatedToHuman;
            conversation.IsHumanHandled = true;
            conversation.LastActivityAt = DateTime.UtcNow;
            db.GestionEvents.Add(new GestionEvent
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                Origin = "watchdog:auto-escalation",
                Result = GestionResult.Pending,
                Notes = $"Watchdog escaló por {StuckThresholdSeconds}s sin respuesta. " +
                        $"InboundQueueItem={item.Id}. " +
                        $"Último error en cola: {item.LastError ?? "(ninguno registrado)"}",
                OccurredAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            logger.LogWarning(
                "[InboundWatchdog] Conversation {ConvId} escalada — tenant={Tenant} phone={Phone}",
                conversation.Id, item.TenantId, item.FromPhone);
        }
        else
        {
            logger.LogWarning(
                "[InboundWatchdog] No encontré Conversation para tenant={Tenant} phone={Phone} — solo marco la cola.",
                item.TenantId, item.FromPhone);
        }

        // ── Paso 4: marcar item de la cola ──────────────────────────────────
        await queue.MarkEscalatedAsync(item.Id, userId: null, ct);
    }
}
