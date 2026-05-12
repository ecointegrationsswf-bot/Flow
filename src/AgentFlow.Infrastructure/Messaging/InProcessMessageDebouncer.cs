using System.Collections.Concurrent;
using AgentFlow.Application.Modules.Webhooks;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Messaging;

/// <summary>
/// Debouncer in-process — agrupa mensajes entrantes que llegan en ráfaga del
/// mismo (TenantId, Phone) y los procesa como un solo turno cuando el cliente
/// deja de escribir por <c>bufferSec</c> segundos.
///
/// Reemplaza el approach anterior que dependía de Hangfire+Redis. Ventajas:
///   1. Cero dependencias externas (no necesita Hangfire ni Redis arriba).
///   2. Latencia menor — el timer in-process dispara con precisión de ms.
///   3. Reset real en cada mensaje nuevo: cancela el timer pendiente y arma uno
///      nuevo. La ventana de espera SE EXTIENDE con cada mensaje, así que
///      mientras el cliente siga escribiendo no se procesa nada.
///
/// Limitación aceptada: si el AppPool de IIS recicla durante la ventana de
/// espera, los mensajes en buffer se pierden. Para volúmenes normales (un
/// usuario tipeando 2-4 mensajes en pocos segundos), el riesgo es despreciable.
/// </summary>
public sealed class InProcessMessageDebouncer : IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InProcessMessageDebouncer> _log;
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public InProcessMessageDebouncer(
        IServiceScopeFactory scopeFactory,
        ILogger<InProcessMessageDebouncer> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    /// <summary>
    /// Encola un mensaje. Si ya hay otro pendiente para el mismo (tenant, phone),
    /// se concatena y SE RESETEA el timer — solo se procesa cuando hay
    /// <paramref name="bufferSec"/> segundos de silencio.
    /// </summary>
    public void Enqueue(
        Guid tenantId, string fromPhone, ChannelType channel,
        string message, string? clientName, string? externalId,
        string? mediaUrl, string? mediaType, int bufferSec,
        Guid? whatsAppLineId = null)
    {
        var key = Key(tenantId, fromPhone);
        _entries.AddOrUpdate(key,
            // No existía: crear entry con un solo mensaje
            _ => CreateEntry(tenantId, fromPhone, channel, message, clientName, externalId, mediaUrl, mediaType, bufferSec, whatsAppLineId),
            // Ya existía: appendear y reiniciar timer
            (_, existing) =>
            {
                lock (existing.Lock)
                {
                    if (existing.Drained) return CreateEntry(tenantId, fromPhone, channel, message, clientName, externalId, mediaUrl, mediaType, bufferSec, whatsAppLineId);
                    existing.Messages.Add(new BufferedMessage(message, channel.ToString(), clientName, externalId, mediaUrl, mediaType, DateTime.UtcNow.Ticks));
                    existing.Last = (clientName, externalId, mediaUrl, mediaType);
                    // Si el mensaje nuevo trae LineId (no debería cambiar pero por
                    // las dudas, preferimos el más reciente que no sea null).
                    if (whatsAppLineId.HasValue) existing.WhatsAppLineId = whatsAppLineId;
                    // Reset del timer — la ventana de silencio empieza de nuevo
                    existing.Timer.Change(TimeSpan.FromSeconds(bufferSec), Timeout.InfiniteTimeSpan);
                    _log.LogDebug("Debouncer ext: {Tenant}/{Phone} msgs={N}", tenantId, fromPhone, existing.Messages.Count);
                    return existing;
                }
            });
    }

    private Entry CreateEntry(
        Guid tenantId, string fromPhone, ChannelType channel,
        string message, string? clientName, string? externalId,
        string? mediaUrl, string? mediaType, int bufferSec,
        Guid? whatsAppLineId)
    {
        Entry? entry = null;
        var timer = new Timer(_ => Flush(tenantId, fromPhone), null, Timeout.Infinite, Timeout.Infinite);
        entry = new Entry
        {
            TenantId = tenantId,
            Phone = fromPhone,
            Channel = channel,
            Timer = timer,
            Messages = new List<BufferedMessage>
            {
                new(message, channel.ToString(), clientName, externalId, mediaUrl, mediaType, DateTime.UtcNow.Ticks)
            },
            Last = (clientName, externalId, mediaUrl, mediaType),
            WhatsAppLineId = whatsAppLineId,
        };
        timer.Change(TimeSpan.FromSeconds(bufferSec), Timeout.InfiniteTimeSpan);
        _log.LogInformation("Debouncer new: {Tenant}/{Phone} bufferSec={Sec} lineId={Line}",
            tenantId, fromPhone, bufferSec, whatsAppLineId?.ToString() ?? "null");
        return entry;
    }

    private void Flush(Guid tenantId, string fromPhone)
    {
        var key = Key(tenantId, fromPhone);
        if (!_entries.TryRemove(key, out var entry)) return;

        List<BufferedMessage> snapshot;
        ChannelType channel;
        (string? Name, string? Ext, string? Url, string? Type) last;
        Guid? lineId;
        lock (entry.Lock)
        {
            entry.Drained = true;
            entry.Timer.Dispose();
            snapshot = entry.Messages.ToList();
            channel = entry.Channel;
            last = entry.Last;
            lineId = entry.WhatsAppLineId;
        }

        if (snapshot.Count == 0) return;

        var concat = string.Join("\n", snapshot
            .Select(m => m.Content)
            .Where(c => !string.IsNullOrWhiteSpace(c)));

        _log.LogInformation("Debouncer flush: {Tenant}/{Phone} agrupando {N} msg(s) ({Chars} chars)",
            tenantId, fromPhone, snapshot.Count, concat.Length);

        // Procesamos en un scope nuevo y task aparte — el thread del Timer
        // no debe bloquearse en mediator.Send (puede tardar varios segundos
        // por el LLM).
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                await mediator.Send(new ProcessIncomingMessageCommand(
                    TenantId: tenantId,
                    FromPhone: fromPhone,
                    Message: concat,
                    Channel: channel,
                    ClientName: last.Name,
                    ExternalMessageId: last.Ext,
                    MediaUrl: last.Url,
                    MediaType: last.Type,
                    WhatsAppLineId: lineId));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Debouncer: error procesando flush de {Tenant}/{Phone}", tenantId, fromPhone);
            }
        });
    }

    private static string Key(Guid tenantId, string phone) => $"{tenantId:N}|{phone}";

    public void Dispose()
    {
        foreach (var e in _entries.Values)
        {
            try { e.Timer.Dispose(); } catch { }
        }
        _entries.Clear();
    }

    private sealed class Entry
    {
        public Guid TenantId { get; set; }
        public string Phone { get; set; } = "";
        public ChannelType Channel { get; set; }
        public Timer Timer { get; set; } = null!;
        public List<BufferedMessage> Messages { get; set; } = new();
        public (string? Name, string? Ext, string? Url, string? Type) Last { get; set; }
        public Guid? WhatsAppLineId { get; set; }
        public bool Drained { get; set; }
        public readonly object Lock = new();
    }
}
