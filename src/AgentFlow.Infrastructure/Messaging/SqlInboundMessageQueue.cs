using System.Text.Json;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Messaging;

/// <summary>
/// Implementación SQL de la cola durable de mensajes entrantes.
/// El API solo upserta; el Worker on-prem reclama y completa.
///
/// Reclamación segura entre múltiples procesos: usa SQL crudo con
/// READPAST + UPDLOCK para que dos workers no tomen la misma fila.
/// El UPSERT vive en una transacción serializable corta — el riesgo
/// de race (mismo cliente, dos webhooks en milisegundos) es bajísimo
/// en el dominio de WhatsApp, pero la implementación lo cubre.
/// </summary>
public sealed class SqlInboundMessageQueue(
    AgentFlowDbContext db,
    ILogger<SqlInboundMessageQueue> log) : IInboundMessageQueue
{
    private const int DefaultClaimedTimeoutMinutes = 5;

    public async Task<Guid> UpsertAsync(InboundMessageUpsertRequest req, CancellationToken ct)
    {
        // Idempotencia por ExternalMessageId: si ya existe una fila (de cualquier estado)
        // con ese id, retornamos su Id y no hacemos nada. Esto cubre reintentos del
        // webhook upstream (UltraMsg/n8n).
        if (!string.IsNullOrEmpty(req.ExternalMessageId))
        {
            var existingByExt = await db.InboundMessageQueueItems
                .Where(x => x.TenantId == req.TenantId
                         && x.ExternalMessageId == req.ExternalMessageId)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(ct);
            if (existingByExt.HasValue)
            {
                log.LogDebug("[Queue] Upsert idempotente — ExternalMessageId={Ext} ya existe ({Id})",
                    req.ExternalMessageId, existingByExt.Value);
                return existingByExt.Value;
            }
        }

        // Append a fila Pending existente del mismo (TenantId, FromPhone), si la hay.
        var pending = await db.InboundMessageQueueItems
            .Where(x => x.TenantId == req.TenantId
                     && x.FromPhone == req.FromPhone
                     && x.Status == InboundMessageStatus.Pending)
            .OrderByDescending(x => x.LastReceivedAt)
            .FirstOrDefaultAsync(ct);

        var now = DateTime.UtcNow;
        var bufferedMsg = new BufferedQueueMessage(
            Content: req.MessageContent,
            ExternalId: req.ExternalMessageId,
            MediaUrl: req.MediaUrl,
            MediaType: req.MediaType,
            ReceivedAt: now);

        if (pending is not null)
        {
            var list = DeserializeOrEmpty(pending.MessagesJson);
            list.Add(bufferedMsg);
            pending.MessagesJson = JsonSerializer.Serialize(list);
            pending.LastReceivedAt = now;
            pending.BufferSeconds = Math.Max(pending.BufferSeconds, req.BufferSeconds);
            pending.ClientName = req.ClientName ?? pending.ClientName;
            pending.ExternalMessageId = req.ExternalMessageId ?? pending.ExternalMessageId;
            pending.MediaUrl = req.MediaUrl ?? pending.MediaUrl;
            pending.MediaType = req.MediaType ?? pending.MediaType;
            pending.WhatsAppLineId = req.WhatsAppLineId ?? pending.WhatsAppLineId;
            await db.SaveChangesAsync(ct);
            log.LogDebug("[Queue] Append a {Id} ({N} msgs)", pending.Id, list.Count);
            return pending.Id;
        }

        var fresh = new InboundMessageQueueItem
        {
            TenantId = req.TenantId,
            FromPhone = req.FromPhone,
            Channel = req.Channel,
            WhatsAppLineId = req.WhatsAppLineId,
            ClientName = req.ClientName,
            ExternalMessageId = req.ExternalMessageId,
            MediaUrl = req.MediaUrl,
            MediaType = req.MediaType,
            MessagesJson = JsonSerializer.Serialize(new List<BufferedQueueMessage> { bufferedMsg }),
            FirstReceivedAt = now,
            LastReceivedAt = now,
            BufferSeconds = req.BufferSeconds,
            Status = InboundMessageStatus.Pending,
        };
        db.InboundMessageQueueItems.Add(fresh);
        await db.SaveChangesAsync(ct);
        log.LogInformation("[Queue] Nuevo item {Id} tenant={Tenant} phone={Phone} bufferSec={Sec}",
            fresh.Id, req.TenantId, req.FromPhone, req.BufferSeconds);
        return fresh.Id;
    }

    public async Task MarkRepliedAsync(Guid id, Guid? outboundMessageId, CancellationToken ct)
    {
        // SqlParameter con tipo explícito — EF Core no acepta DBNull.Value
        // crudo en ExecuteSqlRawAsync, hay que envolverlo así para que el
        // provider lo mapee correctamente a un parámetro nullable.
        var pId = new Microsoft.Data.SqlClient.SqlParameter("@id", System.Data.SqlDbType.UniqueIdentifier) { Value = id };
        var pOut = new Microsoft.Data.SqlClient.SqlParameter("@outboundMessageId", System.Data.SqlDbType.UniqueIdentifier)
        {
            Value = (object?)outboundMessageId ?? DBNull.Value,
        };
        var rows = await db.Database.ExecuteSqlRawAsync(@"
            UPDATE InboundMessageQueueItems
            SET Status = 'Replied',
                CompletedAt = SYSUTCDATETIME(),
                OutboundMessageId = @outboundMessageId
            WHERE Id = @id
              AND Status NOT IN ('Replied','Escalated');", new object[] { pId, pOut }, ct);
        if (rows == 0)
            log.LogDebug("[Queue] MarkReplied no-op para {Id} (ya estaba en estado terminal)", id);
    }

    public async Task MarkFailedAsync(Guid id, string step, string error, int maxAttempts, CancellationToken ct)
    {
        // Si AttemptCount + 1 < maxAttempts → vuelve a Pending para retry.
        // Si supera el límite → queda en Failed esperando al watchdog.
        var truncatedError = error.Length > 4000 ? error[..4000] : error;
        // Paridad con MarkReplied/MarkEscalated: parámetros tipados explícitos.
        // El patrón positional {0}/{1} con EF Core preview tuvo problemas en
        // tests E2E (el UPDATE corría pero AttemptCount no avanzaba), por eso
        // los unificamos en SqlParameter con tipos explícitos.
        var pId = new Microsoft.Data.SqlClient.SqlParameter("@id", System.Data.SqlDbType.UniqueIdentifier) { Value = id };
        var pErr = new Microsoft.Data.SqlClient.SqlParameter("@err", System.Data.SqlDbType.NVarChar, -1) { Value = (object?)truncatedError ?? DBNull.Value };
        var pStep = new Microsoft.Data.SqlClient.SqlParameter("@step", System.Data.SqlDbType.VarChar, 50) { Value = (object?)step ?? DBNull.Value };
        var pMax = new Microsoft.Data.SqlClient.SqlParameter("@maxAttempts", System.Data.SqlDbType.Int) { Value = maxAttempts };
        var rows = await db.Database.ExecuteSqlRawAsync(@"
            UPDATE InboundMessageQueueItems
            SET AttemptCount = AttemptCount + 1,
                LastError = @err,
                LastErrorStep = @step,
                Status = CASE WHEN AttemptCount + 1 < @maxAttempts THEN 'Pending' ELSE 'Failed' END,
                ClaimedAt = NULL,
                ClaimedBy = NULL,
                StartedAt = NULL
            WHERE Id = @id;", new object[] { pId, pErr, pStep, pMax }, ct);
        log.LogWarning("[Queue] MarkFailed {Id} step={Step} rows={Rows} err={Err}",
            id, step, rows, truncatedError.Length > 200 ? truncatedError[..200] : truncatedError);
    }

    public async Task<IReadOnlyList<InboundMessageQueueItem>> ClaimReadyAsync(
        string workerId, int batchSize, CancellationToken ct)
    {
        // Reclamación atómica: UPDATE...OUTPUT con CTE y READPAST/UPDLOCK.
        // - Pending vencidos: LastReceivedAt + BufferSeconds <= now
        // - Claimed huérfanos: ClaimedAt + 5min < now (worker crasheado)
        // El READPAST evita esperar locks de filas que otro worker está procesando.
        var sql = @"
            ;WITH cte AS (
              SELECT TOP (@batch) *
              FROM InboundMessageQueueItems WITH (READPAST, UPDLOCK, ROWLOCK)
              WHERE (
                  (Status = 'Pending' AND DATEADD(SECOND, BufferSeconds, LastReceivedAt) <= SYSUTCDATETIME())
                OR
                  (Status = 'Claimed' AND ClaimedAt IS NOT NULL
                      AND DATEDIFF(MINUTE, ClaimedAt, SYSUTCDATETIME()) >= @timeout)
              )
              ORDER BY LastReceivedAt
            )
            UPDATE cte
            SET Status = 'Claimed', ClaimedAt = SYSUTCDATETIME(), ClaimedBy = @worker
            OUTPUT inserted.Id;";

        var ids = await db.Database
            .SqlQueryRaw<Guid>(sql,
                new Microsoft.Data.SqlClient.SqlParameter("@batch", batchSize),
                new Microsoft.Data.SqlClient.SqlParameter("@timeout", DefaultClaimedTimeoutMinutes),
                new Microsoft.Data.SqlClient.SqlParameter("@worker", workerId))
            .ToListAsync(ct);

        if (ids.Count == 0) return Array.Empty<InboundMessageQueueItem>();

        return await db.InboundMessageQueueItems
            .Where(x => ids.Contains(x.Id))
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<InboundMessageQueueItem>> FindStuckAsync(int thresholdSeconds, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-thresholdSeconds);
        return await db.InboundMessageQueueItems
            .Where(x => x.Status != InboundMessageStatus.Replied
                     && x.Status != InboundMessageStatus.Escalated
                     && x.FirstReceivedAt < cutoff)
            .OrderBy(x => x.FirstReceivedAt)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task MarkEscalatedAsync(Guid id, Guid? userId, CancellationToken ct)
    {
        var pId = new Microsoft.Data.SqlClient.SqlParameter("@id", System.Data.SqlDbType.UniqueIdentifier) { Value = id };
        var pUser = new Microsoft.Data.SqlClient.SqlParameter("@userId", System.Data.SqlDbType.UniqueIdentifier)
        {
            Value = (object?)userId ?? DBNull.Value,
        };
        await db.Database.ExecuteSqlRawAsync(@"
            UPDATE InboundMessageQueueItems
            SET Status = 'Escalated',
                EscalatedAt = SYSUTCDATETIME(),
                EscalatedToUserId = @userId,
                CompletedAt = SYSUTCDATETIME()
            WHERE Id = @id;", new object[] { pId, pUser }, ct);
        log.LogWarning("[Queue] Item {Id} escalado a humano ({User})", id, userId);
    }

    private static List<BufferedQueueMessage> DeserializeOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<BufferedQueueMessage>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }
}

/// <summary>Un mensaje individual dentro de la ráfaga acumulada.</summary>
public record BufferedQueueMessage(
    string Content,
    string? ExternalId,
    string? MediaUrl,
    string? MediaType,
    DateTime ReceivedAt);
