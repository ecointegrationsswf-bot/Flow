using AgentFlow.Application.Modules.Webhooks;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Messaging;

/// <summary>
/// Job Hangfire que procesa el buffer de mensajes de una conversación. Se encarga
/// del debounce: si al dispararse detecta que llegó otro mensaje dentro de la
/// ventana de espera, sale sin procesar (otro job programado más tarde lo hará).
/// </summary>
public class MessageBufferFlushJob(
    IMessageBufferStore buffer,
    AgentFlowDbContext db,
    IMediator mediator,
    ILogger<MessageBufferFlushJob> log)
{
    // Lock TTL — si el processing se cuelga, se libera solo.
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Llamado por Hangfire con delay. Drena el buffer y corre el pipeline si
    /// han pasado al menos BufferSeconds desde el último mensaje.
    /// </summary>
    public async Task RunAsync(Guid tenantId, string phone, CancellationToken ct)
    {
        var lastTicks = await buffer.GetLastMessageTicksAsync(tenantId, phone, ct);
        if (lastTicks is null)
        {
            log.LogDebug("Flush {Tenant}/{Phone}: buffer vacío, no-op", tenantId, phone);
            return;
        }

        var bufferSec = await GetBufferSecondsAsync(tenantId, ct);
        if (bufferSec <= 0)
        {
            // Feature deshabilitado para este tenant — no debería llegar acá, pero por seguridad
            log.LogDebug("Flush {Tenant}/{Phone}: buffer deshabilitado, drenando y procesando", tenantId, phone);
        }
        else
        {
            // Tolerancia de 500ms para evitar rechazos por diferencias finas de reloj.
            var waited = DateTime.UtcNow - new DateTime(lastTicks.Value, DateTimeKind.Utc);
            var threshold = TimeSpan.FromSeconds(bufferSec) - TimeSpan.FromMilliseconds(500);
            if (waited < threshold)
            {
                log.LogDebug("Flush {Tenant}/{Phone}: llegó mensaje reciente ({Waited}ms < {Threshold}ms), dejo al próximo job programado",
                    tenantId, phone, (int)waited.TotalMilliseconds, (int)threshold.TotalMilliseconds);
                return;
            }
        }

        // Acquire lock — si otro job ya está procesando esta conversación, salir.
        if (!await buffer.TryAcquireFlushLockAsync(tenantId, phone, LockTtl, ct))
        {
            log.LogDebug("Flush {Tenant}/{Phone}: otro job ya tiene el lock, salgo", tenantId, phone);
            return;
        }

        try
        {
            var pending = await buffer.DrainAsync(tenantId, phone, ct);
            if (pending.Count == 0)
            {
                log.LogDebug("Flush {Tenant}/{Phone}: buffer ya drenado por otro job", tenantId, phone);
                return;
            }

            // Concatenar todos los mensajes en orden separados por salto de línea.
            var concatContent = string.Join("\n", pending.Select(m => m.Content).Where(c => !string.IsNullOrWhiteSpace(c)));
            var last = pending[^1];

            log.LogInformation("Flush {Tenant}/{Phone}: procesando {Count} mensajes agrupados ({Chars} chars)",
                tenantId, phone, pending.Count, concatContent.Length);

            if (!Enum.TryParse<ChannelType>(last.Channel, true, out var channel))
                channel = ChannelType.WhatsApp;

            var cmd = new ProcessIncomingMessageCommand(
                TenantId: tenantId,
                FromPhone: phone,
                Message: concatContent,
                Channel: channel,
                ClientName: last.ClientName,
                ExternalMessageId: last.ExternalMessageId,
                MediaUrl: last.MediaUrl,
                MediaType: last.MediaType);

            await mediator.Send(cmd, ct);
        }
        finally
        {
            await buffer.ReleaseFlushLockAsync(tenantId, phone, ct);
        }
    }

    private async Task<int> GetBufferSecondsAsync(Guid tenantId, CancellationToken ct)
    {
        var sec = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => (int?)t.MessageBufferSeconds)
            .FirstOrDefaultAsync(ct);
        return sec ?? 5;
    }
}
