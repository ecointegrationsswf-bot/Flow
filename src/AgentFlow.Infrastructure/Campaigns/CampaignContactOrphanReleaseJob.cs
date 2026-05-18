using AgentFlow.Domain.Enums;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Campaigns;

/// <summary>
/// Segunda capa de defensa contra contactos huérfanos en DispatchStatus=Claimed.
///
/// CAPA 1 (in-dispatcher): CampaignDispatcherService.DispatchBatchAsync libera
/// los Claimed huérfanos al inicio de cada tick (recovery sweep scoped a la
/// campaña) y en un try/finally alrededor del foreach de envío (release
/// inmediato del lote actual ante cancellation/exception). Esa capa vive en
/// el Worker on-prem.
///
/// CAPA 2 (este job): corre en el API (vía Hangfire RecurringJob `*/5 * * * *`)
/// y barre TODAS las campañas globalmente. Cubre:
///
/// - Worker on-prem caído, no actualizado, o sin Hangfire-server.
/// - Bug futuro en el dispatcher que reintroduzca el problema.
/// - Campañas que por alguna razón dejaron de ser tickeadas y nadie las visita.
/// - Cualquier otra fuente de Claimed huérfano que no consideramos.
///
/// Defense in depth: aunque la capa 1 falle por cualquier motivo, esta capa
/// recupera los contactos en máximo 5 minutos sin intervención manual.
///
/// Idempotente y segura para correr concurrentemente: usa ExecuteUpdate
/// atómico con filtro ClaimedAt &lt; cutoff, así que si dos instancias del
/// API corren el job al mismo tiempo, el segundo encuentra 0 filas.
/// </summary>
public class CampaignContactOrphanReleaseJob(
    AgentFlowDbContext db,
    ILogger<CampaignContactOrphanReleaseJob> log)
{
    /// <summary>
    /// Edad mínima de un Claimed para considerarse huérfano. Un send individual
    /// dura ~3-30 seg incluso con LLM + WhatsApp; cualquier Claimed con más
    /// de 5 minutos sin SentAt confirmado es ya casi seguro un huérfano de
    /// un Worker que se reinició / crasheó / fue killed.
    /// </summary>
    private static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Libera todos los Claimed huérfanos del sistema (todas las campañas,
    /// todos los tenants). Pensado para correr en cron recurrente cada 5 min.
    /// </summary>
    /// <returns>Número de contactos liberados (vueltos a Queued).</returns>
    public async Task<int> ReleaseAllAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - StaleClaimAge;

        var released = await db.CampaignContacts
            .Where(cc => cc.DispatchStatus == DispatchStatus.Claimed
                      && cc.ClaimedAt < cutoff
                      && cc.SentAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.DispatchStatus, DispatchStatus.Queued)
                .SetProperty(c => c.ClaimedAt, (DateTime?)null), ct);

        if (released > 0)
        {
            // WARN porque si esto se activa quiere decir que la capa 1 (in-dispatcher)
            // no liberó algo que debería haber liberado — o que el Worker está caído.
            // Vale la pena verlo en logs.
            log.LogWarning(
                "OrphanRelease: liberados {N} contactos en DispatchStatus=Claimed " +
                "más viejos que {Minutes} min (cutoff={Cutoff:o}). " +
                "El dispatcher in-Worker debería haberlos liberado primero; " +
                "este sweep es la red de seguridad global.",
                released, StaleClaimAge.TotalMinutes, cutoff);
        }
        else
        {
            log.LogDebug("OrphanRelease: 0 huérfanos encontrados (cutoff={Cutoff:o}).", cutoff);
        }

        return released;
    }
}
