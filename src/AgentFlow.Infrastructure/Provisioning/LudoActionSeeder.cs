using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Provisioning;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Provisioning;

/// <summary>
/// Integración Ludo CRM — Fase 4. Seed idempotente de:
/// <list type="bullet">
///   <item>3 ActionDefinitions GLOBALES de salida hacia Ludo (registrar_oportunidad,
///     mover_fase, registrar_nota) con su DefaultWebhookContract plantilla (URL de site14,
///     X-Api-Key VACÍO — el token real vive en el TenantActionContract de cada tenant).</item>
///   <item>La acción interna LUDO_OUTBOX_DRAINER + su cron global (cada 5 min) que
///     reintenta las llamadas a Ludo fallidas encoladas en LudoOutboxItems.</item>
/// </list>
/// Aditivo: si ya existen no las modifica. Se invoca al boot del API desde Program.cs.
/// </summary>
public static class LudoActionSeeder
{
    public const string DrainerSlug = "LUDO_OUTBOX_DRAINER";

    public static async Task SeedAsync(AgentFlowDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var existing = await db.ActionDefinitions
            .Where(a => a.TenantId == null)
            .Select(a => a.Name)
            .ToListAsync(ct);
        var have = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var inserted = 0;

        // ── 1. Acciones de salida hacia Ludo (webhook, disparadas por el agente via ATP) ──
        var outbound = new (string Slug, string Description)[]
        {
            (LudoIntegrationDefaults.RegistrarOportunidadSlug,
             "Ludo CRM: registra/reusa una oportunidad (upsert por teléfono; regla 1-activa-por-objetivo). El token del tenant vive en su TenantActionContract."),
            (LudoIntegrationDefaults.MoverFaseSlug,
             "Ludo CRM: mueve la oportunidad activa del cliente a otra fase del pipeline (idempotente). El {oportunidadId} y el faseId los resuelve el LudoActionEnricher en runtime."),
            (LudoIntegrationDefaults.RegistrarNotaSlug,
             "Ludo CRM: registra una nota de seguimiento asociada al prospecto (por teléfono)."),
        };

        foreach (var (slug, description) in outbound)
        {
            if (have.Contains(slug)) continue;
            db.ActionDefinitions.Add(new ActionDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = null,            // global
                Name = slug,
                Description = description,
                IsActive = true,
                RequiresWebhook = true,
                IsProcess = false,
                DefaultWebhookContract = LudoIntegrationDefaults.BuildContractJson(
                    slug, apiBaseUrl: null, apiKey: null),
                CreatedAt = DateTime.UtcNow,
            });
            inserted++;
        }

        // ── 2. Drainer del outbox (proceso interno del Worker) ──
        if (!have.Contains(DrainerSlug))
        {
            db.ActionDefinitions.Add(new ActionDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = null,
                Name = DrainerSlug,
                Description = "Ludo CRM: reintenta con backoff las llamadas de salida a Ludo que fallaron (LudoOutboxItems Pending). Degradación suave: la conversación nunca se bloquea por una caída de Ludo.",
                IsActive = true,
                RequiresWebhook = false,
                IsProcess = true,
                CreatedAt = DateTime.UtcNow,
            });
            inserted++;
        }

        if (inserted > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("LudoActionSeeder: insertadas {Inserted} ActionDefinitions globales.", inserted);
        }

        // ── 3. Cron global del drainer (cada 5 min, AllTenants) — idempotente ──
        var drainerId = await db.ActionDefinitions
            .Where(a => a.TenantId == null && a.Name == DrainerSlug)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        if (drainerId is null) return;

        var hasCron = await db.ScheduledWebhookJobs
            .AnyAsync(j => j.ActionDefinitionId == drainerId.Value
                        && j.TriggerType == "Cron"
                        && j.Scope == "AllTenants", ct);
        if (hasCron) return;

        var now = DateTime.UtcNow;
        db.ScheduledWebhookJobs.Add(new ScheduledWebhookJob
        {
            Id = Guid.NewGuid(),
            ActionDefinitionId = drainerId.Value,
            TriggerType = "Cron",
            CronExpression = "*/5 * * * *",
            Scope = "AllTenants",
            IsActive = true,
            NextRunAt = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc).AddMinutes(1),
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("LudoActionSeeder: creado cron global {Slug} (*/5).", DrainerSlug);
    }
}
