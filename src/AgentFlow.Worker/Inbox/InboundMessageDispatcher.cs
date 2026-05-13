using System.Text.Json;
using AgentFlow.Application.Modules.Webhooks;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Messaging;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Worker.Inbox;

/// <summary>
/// Dispatcher autoritativo de mensajes entrantes.
///
/// Cada tick (5s) reclama hasta BatchSize items de InboundMessageQueue cuyo
/// LastReceivedAt + BufferSeconds ya venció, y los procesa vía MediatR
/// (ProcessIncomingMessageCommand) — el mismo pipeline que usa el debouncer
/// in-memory del API, pero ahora desde un proceso que NO se recicla.
///
/// Garantías:
///   - Claim atómico (READPAST/UPDLOCK) — múltiples workers no procesan lo mismo.
///   - Reintentos: si el handler falla, MarkFailedAsync incrementa AttemptCount
///     y vuelve a Pending hasta MaxAttempts (3). Después queda Failed y el
///     watchdog lo rescata.
///   - Sobrevive al reciclado del AppPool de Smartasp porque vive en el Worker
///     on-prem como Windows Service.
/// </summary>
public class InboundMessageDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<InboundMessageDispatcher> logger) : BackgroundService
{
    // Tick de 3s — sweet spot entre latencia del peor caso (AppPool muerto)
    // y carga de la BD. 2s no aporta valor real porque el hot-path debouncer
    // del API ya cubre el sub-segundo cuando el AppPool está vivo.
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(3);
    // Batch de 20 — soporta picos sin esperar al próximo tick. El parallelism
    // limit en TickAsync sigue en 5 para no ahogar Claude.
    private const int BatchSize = 20;
    private const int MaxAttempts = 3;

    /// <summary>Identificador del worker para auditoría en ClaimedBy.</summary>
    private readonly string _workerId = $"dispatcher:{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "[InboundDispatcher] Arrancando. Tick {Tick}s, batch {Batch}, maxAttempts {Max}. WorkerId={Wid}",
            (int)TickInterval.TotalSeconds, BatchSize, MaxAttempts, _workerId);

        // Pequeño delay inicial para coordinar arranque con el resto del host.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
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
                logger.LogError(ex, "[InboundDispatcher] Tick falló — reintentando en {Sec}s.",
                    (int)TickInterval.TotalSeconds);
            }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        logger.LogInformation("[InboundDispatcher] Detenido.");
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var queue = sp.GetRequiredService<IInboundMessageQueue>();

        var batch = await queue.ClaimReadyAsync(_workerId, BatchSize, ct);
        if (batch.Count == 0) return;

        logger.LogInformation("[InboundDispatcher] Reclamados {N} items para procesar.", batch.Count);

        // Procesamos en paralelo limitado dentro del batch — son tenants distintos
        // mayormente, y cada procesamiento puede tomar varios segundos (LLM).
        // Cada item necesita su propio scope para que el DbContext sea fresco.
        await Parallel.ForEachAsync(
            batch,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 5,
                CancellationToken = ct,
            },
            async (item, innerCt) =>
            {
                try
                {
                    await ProcessOneAsync(item, innerCt);
                }
                catch (OperationCanceledException) when (innerCt.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "[InboundDispatcher] Item {Id} falló en bucle paralelo (no debería llegar aquí).",
                        item.Id);
                }
            });
    }

    private async Task ProcessOneAsync(Domain.Entities.InboundMessageQueueItem item, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var mediator = sp.GetRequiredService<IMediator>();
        var queue = sp.GetRequiredService<IInboundMessageQueue>();

        // Reconstruir el mensaje concatenado a partir de MessagesJson.
        // Si el JSON está vacío/corrupto, fallback al item.MessagesJson tal cual.
        var concat = ConcatenateMessages(item.MessagesJson);
        if (string.IsNullOrWhiteSpace(concat))
        {
            logger.LogWarning("[InboundDispatcher] Item {Id} sin contenido — marco Failed.", item.Id);
            await queue.MarkFailedAsync(item.Id, step: "concat", error: "empty-content", maxAttempts: MaxAttempts, ct);
            return;
        }

        var channel = Enum.TryParse<ChannelType>(item.Channel, ignoreCase: true, out var ch)
            ? ch : ChannelType.WhatsApp;

        try
        {
            logger.LogInformation(
                "[InboundDispatcher] Procesando {Id} tenant={Tenant} phone={Phone} ({Chars} chars).",
                item.Id, item.TenantId, item.FromPhone, concat.Length);

            var result = await mediator.Send(new ProcessIncomingMessageCommand(
                TenantId: item.TenantId,
                FromPhone: item.FromPhone,
                Message: concat,
                Channel: channel,
                ClientName: item.ClientName,
                ExternalMessageId: item.ExternalMessageId,
                MediaUrl: item.MediaUrl,
                MediaType: item.MediaType,
                WhatsAppLineId: item.WhatsAppLineId), ct);

            await queue.MarkRepliedAsync(item.Id, outboundMessageId: null, ct);

            logger.LogInformation(
                "[InboundDispatcher] {Id} respondido. Conv={Conv} agente={Agent}",
                item.Id, result.ConversationId, result.AgentType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[InboundDispatcher] Falló procesamiento de {Id} (attempt {Attempt}/{Max}).",
                item.Id, item.AttemptCount + 1, MaxAttempts);
            try
            {
                await queue.MarkFailedAsync(
                    item.Id,
                    step: "mediator",
                    error: ex.GetType().Name + ": " + ex.Message,
                    maxAttempts: MaxAttempts,
                    ct);
            }
            catch (Exception qex)
            {
                logger.LogError(qex,
                    "[InboundDispatcher] No pude marcar Failed para {Id} — la fila quedará Claimed hasta timeout (5min).",
                    item.Id);
            }
        }
    }

    private static string ConcatenateMessages(string messagesJson)
    {
        if (string.IsNullOrWhiteSpace(messagesJson)) return string.Empty;
        try
        {
            var list = JsonSerializer.Deserialize<List<BufferedQueueMessage>>(messagesJson);
            if (list is null || list.Count == 0) return string.Empty;
            return string.Join("\n", list
                .Select(m => m.Content)
                .Where(c => !string.IsNullOrWhiteSpace(c)));
        }
        catch
        {
            return string.Empty;
        }
    }
}
