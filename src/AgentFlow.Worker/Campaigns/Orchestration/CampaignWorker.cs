using AgentFlow.Domain.Enums;
using AgentFlow.Infrastructure.Campaigns;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Worker.Campaigns.Orchestration;

/// <summary>
/// BackgroundService que orquesta el envío de campañas v2.
///
/// Cada tick (30s):
/// 1. Encuentra los <c>TenantId</c>s con al menos una campaña <c>Running</c>
///    cuyo tenant tiene <c>CampaignDispatchEnabled = true</c>.
/// 2. Procesa los tenants en paralelo, con un tope global (<see cref="MaxConcurrentTenants"/>).
/// 3. Por cada tenant, procesa sus campañas en orden FIFO (LaunchedAt) — UNA A LA VEZ.
///    Esto garantiza que el rate limit del tenant se respete aunque tenga
///    múltiples campañas abiertas: las 6 msg/min son del NÚMERO, no de la campaña.
/// 4. Para cada campaña llama <see cref="CampaignDispatcherService.DispatchBatchAsync"/>.
///    Si el lote termina por daily limit / business hours / no provider, abandona el
///    tenant en este tick (próximo tick reevaluamos).
///
/// Reanudación tras reinicio: el estado vive en SQL (DispatchStatus). Si el Worker se
/// reinicia, el siguiente tick recoge los Queued de nuevo. Los Claimed huérfanos se
/// resetean por el CampaignRecoveryService (Fase 4, todavía no presente).
/// </summary>
public class CampaignWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<CampaignWorker> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private const int MaxConcurrentTenants = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[CampaignWorker] Arrancando. Tick cada {Seconds}s, max {Tenants} tenants concurrentes.",
            (int)TickInterval.TotalSeconds, MaxConcurrentTenants);

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
                logger.LogError(ex, "[CampaignWorker] Tick falló — reintentando en {Seconds}s.",
                    (int)TickInterval.TotalSeconds);
            }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        logger.LogInformation("[CampaignWorker] Detenido.");
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var tenantIds = await GetTenantsWithRunningCampaignsAsync(ct);
        if (tenantIds.Count == 0) return;

        logger.LogDebug("[CampaignWorker] Tenants con campañas Running: {Count}", tenantIds.Count);

        await Parallel.ForEachAsync(
            tenantIds,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxConcurrentTenants,
                CancellationToken = ct,
            },
            async (tenantId, innerCt) =>
            {
                try
                {
                    await ProcessTenantAsync(tenantId, innerCt);
                }
                catch (OperationCanceledException) when (innerCt.IsCancellationRequested)
                {
                    // shutting down — silencio
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[CampaignWorker] Tenant {TenantId}: error procesando.", tenantId);
                }
            });
    }

    private async Task<List<Guid>> GetTenantsWithRunningCampaignsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentFlowDbContext>();

        return await (
            from c in db.Campaigns
            join t in db.Tenants on c.TenantId equals t.Id
            where c.Status == CampaignStatus.Running
               && c.IsActive
               && t.CampaignDispatchEnabled
            select c.TenantId
        ).Distinct().ToListAsync(ct);
    }

    private async Task ProcessTenantAsync(Guid tenantId, CancellationToken ct)
    {
        // Scope dedicado por tenant — el dispatcher es Scoped, queremos un DbContext fresco.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentFlowDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<CampaignDispatcherService>();

        // Campañas del tenant en orden FIFO (las más antiguas primero).
        var campaignIds = await db.Campaigns
            .Where(c => c.TenantId == tenantId && c.Status == CampaignStatus.Running && c.IsActive)
            .OrderBy(c => c.LaunchedAt ?? c.CreatedAt)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (campaignIds.Count == 0) return;

        // SECUENCIAL dentro del tenant — clave para que el rate limit del tenant se respete
        // aunque tenga 3 campañas Running. Las 6 msg/min son del NÚMERO de WhatsApp del tenant.
        foreach (var campaignId in campaignIds)
        {
            if (ct.IsCancellationRequested) break;

            var result = await dispatcher.DispatchBatchAsync(campaignId, ct);

            // Razones que detienen el procesamiento del TENANT entero en este tick.
            // Si UltraMsg falla 3 veces seguidas (TooManyErrors), no es razonable seguir
            // bombardeando con la siguiente campaña — esperamos al próximo tick.
            if (result.StopReason is DispatchStopReason.DailyLimitReached
                                  or DispatchStopReason.OutsideBusinessHours
                                  or DispatchStopReason.NoProvider
                                  or DispatchStopReason.TooManyErrors)
            {
                logger.LogInformation(
                    "[CampaignWorker] Tenant {TenantId}: detenido en este tick por {Reason}.",
                    tenantId, result.StopReason);
                break;
            }
        }
    }
}
