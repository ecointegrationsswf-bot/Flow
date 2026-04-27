using AgentFlow.Domain.Entities;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Asegura que las ActionDefinitions globales requeridas por la Fase 2 existan
/// en BD. Idempotente: solo inserta los que faltan. Se invoca una vez al boot
/// del API desde Program.cs.
///
/// Aditivo: si las acciones ya existen (por seed manual previo) no las modifica.
/// </summary>
public static class CampaignAutomationSeeder
{
    public static async Task SeedAsync(AgentFlowDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var required = new (string Name, string Description)[]
        {
            (
                "FOLLOW_UP_MESSAGE",
                "Acción interna usada por el ScheduledWebhookWorker para enviar mensajes de seguimiento configurados en CampaignTemplate.FollowUpHours/FollowUpMessagesJson. No invocar manualmente."
            ),
            (
                "AUTO_CLOSE_CAMPAIGN",
                "Acción interna que cierra todas las conversaciones activas de una campaña al cumplirse CampaignTemplate.AutoCloseHours. No invocar manualmente."
            ),
        };

        var existing = await db.ActionDefinitions
            .Where(a => a.TenantId == null)
            .Select(a => a.Name)
            .ToListAsync(ct);

        var inserted = 0;
        foreach (var (name, description) in required)
        {
            if (existing.Contains(name)) continue;

            db.ActionDefinitions.Add(new ActionDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = null,            // global
                Name = name,
                Description = description,
                IsActive = true,
                RequiresWebhook = false,    // las invoca el Worker, no requiere webhook saliente directo
                CreatedAt = DateTime.UtcNow
            });
            inserted++;
        }

        if (inserted > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("CampaignAutomationSeeder: insertadas {Count} ActionDefinitions globales.", inserted);
        }
    }
}
