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
        // Acciones internas del Campaign Automation Worker. Todas son IsProcess=true:
        // no requieren webhook saliente, email o SMS. La lógica vive en un
        // IScheduledJobExecutor del backend.
        var required = new (string Name, string Description)[]
        {
            (
                "FOLLOW_UP_MESSAGE",
                "Acción interna LEGACY (modelo per-contacto). Sustituida por FOLLOW_UP_SWEEP. Se mantiene en BD por compatibilidad con jobs viejos."
            ),
            (
                "AUTO_CLOSE_CAMPAIGN",
                "Acción interna LEGACY (modelo per-campaña). Sustituida por AUTO_CLOSE_CAMPAIGN_SWEEP. Se mantiene en BD por compatibilidad con jobs viejos."
            ),
            (
                "FOLLOW_UP_SWEEP",
                "Sweeper global de follow-ups. UN solo cron */5 * * * * que recorre TODOS los CampaignContacts con DispatchStatus=Sent y dispara los follow-ups que correspondan según CampaignTemplate.FollowUpHours/FollowUpMessagesJson. Reemplaza al modelo per-contacto FOLLOW_UP_MESSAGE."
            ),
            (
                "AUTO_CLOSE_CAMPAIGN_SWEEP",
                "Sweeper global de auto-cierre. UN solo cron */30 * * * * que cierra todas las campañas Running cuya última actividad excedió CampaignTemplate.AutoCloseHours. Reemplaza al modelo per-campaña AUTO_CLOSE_CAMPAIGN."
            ),
            (
                "LABEL_CONVERSATIONS",
                "Acción interna del job de etiquetado IA. Recorre las conversaciones cerradas no etiquetadas y llama a Claude para clasificarlas según las labels asociadas al maestro. Tras etiquetar dispara el evento ConversationLabeled — el admin puede programar webhooks de resultado al cliente como Scheduled Jobs aparte."
            ),
            (
                "SEND_LABELING_SUMMARY",
                "Acción interna del job que genera el reporte Excel de etiquetado y lo envía por email al usuario que cargó las campañas. Sube el archivo al container 'sumary' de Azure Blob. Programar 1 hora después del job de etiquetado."
            ),
            (
                "WHATSAPP_LINE_HEALTH_CHECK",
                "Verifica cada día a las 6am Panamá el estado de TODAS las líneas UltraMsg activas. Notifica caídas: 1 correo consolidado a SuperAdmins + 1 correo por tenant afectado a sus usuarios Admin/Supervisor listando agentes IA que quedan inactivos."
            ),
        };

        var existingActions = await db.ActionDefinitions
            .Where(a => a.TenantId == null)
            .ToDictionaryAsync(a => a.Name, ct);

        var inserted = 0;
        var updated = 0;
        foreach (var (name, description) in required)
        {
            if (existingActions.TryGetValue(name, out var existing))
            {
                // Si ya existe pero IsProcess sigue en false (instalación previa al campo),
                // lo marcamos como proceso ahora — son acciones internas por definición.
                if (!existing.IsProcess)
                {
                    existing.IsProcess = true;
                    updated++;
                }
                continue;
            }

            db.ActionDefinitions.Add(new ActionDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = null,            // global
                Name = name,
                Description = description,
                IsActive = true,
                RequiresWebhook = false,    // las invoca el Worker, no requiere webhook saliente directo
                IsProcess = true,           // acción interna del backend
                CreatedAt = DateTime.UtcNow
            });
            inserted++;
        }

        if (inserted > 0 || updated > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("CampaignAutomationSeeder: insertadas {Inserted}, actualizadas {Updated} ActionDefinitions globales.", inserted, updated);
        }

        // Fase 3: garantizar que existe un único ScheduledWebhookJob Cron horario
        // que dispare LABEL_CONVERSATIONS. El executor decide internamente qué
        // maestros corresponden a la hora UTC actual via CampaignTemplate.LabelingJobHourUtc.
        await EnsureLabelingCronJobAsync(db, logger, ct);

        // Sweepers globales del modelo nuevo (reemplazan los jobs per-contacto/per-campaña).
        await EnsureGlobalCronJobAsync(db, logger,
            actionName: "FOLLOW_UP_SWEEP",
            cron: "*/5 * * * *",
            description: "Recorre contactos enviados y manda follow-ups que toquen.",
            ct);
        await EnsureGlobalCronJobAsync(db, logger,
            actionName: "AUTO_CLOSE_CAMPAIGN_SWEEP",
            cron: "*/30 * * * *",
            description: "Cierra campañas que excedieron AutoCloseHours.",
            ct);

        // Monitor diario de líneas WhatsApp — 6am hora Panamá (la expresión cron
        // se interpreta en TZ Panamá por el ScheduledWebhookWorker via cronos).
        await EnsureGlobalCronJobAsync(db, logger,
            actionName: "WHATSAPP_LINE_HEALTH_CHECK",
            cron: "0 6 * * *",
            description: "Pingea todas las líneas UltraMsg, reporta caídas por email a SuperAdmins y a los Admin/Supervisor del tenant.",
            ct);
    }

    /// <summary>
    /// Idempotente: si ya existe un Cron AllTenants para esa Action, no hace nada.
    /// </summary>
    private static async Task EnsureGlobalCronJobAsync(
        AgentFlowDbContext db, ILogger logger,
        string actionName, string cron, string description, CancellationToken ct)
    {
        var actionId = await db.ActionDefinitions
            .Where(a => a.TenantId == null && a.Name == actionName)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        if (actionId is null)
        {
            logger.LogWarning("Seeder: ActionDefinition '{Name}' no existe — no se crea cron.", actionName);
            return;
        }

        var alreadyHasCron = await db.ScheduledWebhookJobs
            .AnyAsync(j => j.ActionDefinitionId == actionId.Value
                        && j.TriggerType == "Cron"
                        && j.Scope == "AllTenants", ct);
        if (alreadyHasCron) return;

        // Calculamos NextRunAt al próximo minuto UTC para que tickée pronto.
        var now = DateTime.UtcNow;
        var nextRun = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc).AddMinutes(1);

        db.ScheduledWebhookJobs.Add(new ScheduledWebhookJob
        {
            Id = Guid.NewGuid(),
            ActionDefinitionId = actionId.Value,
            TriggerType = "Cron",
            CronExpression = cron,
            Scope = "AllTenants",
            IsActive = true,
            NextRunAt = nextRun,
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeder: creado cron global '{Name}' ({Cron}). NextRunAt={Next}. {Desc}",
            actionName, cron, nextRun, description);
    }

    private static async Task EnsureLabelingCronJobAsync(AgentFlowDbContext db, ILogger logger, CancellationToken ct)
    {
        var labelAction = await db.ActionDefinitions
            .Where(a => a.TenantId == null && a.Name == "LABEL_CONVERSATIONS")
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        if (labelAction is null) return;

        var alreadyHasCron = await db.ScheduledWebhookJobs
            .AnyAsync(j => j.ActionDefinitionId == labelAction.Value
                           && j.TriggerType == "Cron"
                           && j.Scope == "AllTenants", ct);
        if (alreadyHasCron) return;

        // Cron "0 * * * *" → al minuto 0 de cada hora UTC.
        var cron = "0 * * * *";
        var next = ComputeNextHourTopUtc(DateTime.UtcNow);

        db.ScheduledWebhookJobs.Add(new ScheduledWebhookJob
        {
            Id = Guid.NewGuid(),
            ActionDefinitionId = labelAction.Value,
            TriggerType = "Cron",
            CronExpression = cron,
            Scope = "AllTenants",
            IsActive = true,
            NextRunAt = next,
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("CampaignAutomationSeeder: creado job Cron horario LABEL_CONVERSATIONS (NextRunAt={Next}).", next);
    }

    private static DateTime ComputeNextHourTopUtc(DateTime fromUtc)
    {
        // Próximo "0 * * * *" en UTC.
        return new DateTime(fromUtc.Year, fromUtc.Month, fromUtc.Day, fromUtc.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);
    }
}
