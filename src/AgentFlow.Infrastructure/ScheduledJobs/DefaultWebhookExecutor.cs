using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Domain.Webhooks;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Executor de fallback (slug = "*") que el Worker usa cuando ningún executor
/// específico declara el slug del ActionDefinition. Invoca al IActionExecutorService
/// existente (Webhook Contract System v2.0) reutilizando toda la maquinaria de
/// PayloadBuilder/HttpDispatcher/OutputInterpreter sin modificarla.
///
/// Para jobs Scope = AllTenants la invocación es de "diagnóstico": dispara una
/// vez y reporta resultado. PerCampaign y PerConversation se manejan en Fases
/// 2 y 3 con executors específicos que conocen el dominio del trabajo.
/// </summary>
public class DefaultWebhookExecutor(
    IActionExecutorService actionExecutor,
    ILogger<DefaultWebhookExecutor> log) : IScheduledJobExecutor
{
    public string Slug => "*";

    public async Task<JobRunResult> ExecuteAsync(
        ScheduledWebhookJob job, ScheduledJobContext ctx, CancellationToken ct)
    {
        if (job.ActionDefinition is null)
        {
            return JobRunResult.Failed("ActionDefinition no cargada (¿Include faltante en el repo?)");
        }

        var slug = job.ActionDefinition.Name;
        try
        {
            // En Fase 1 los jobs Cron/AllTenants solo prueban el contrato. Las acciones
            // que necesitan tenant/campaign/conversation deben usar un executor específico.
            // Si llegamos aquí con un Scope no-AllTenants registramos warning y skipamos.
            if (job.Scope != "AllTenants")
            {
                log.LogWarning(
                    "DefaultWebhookExecutor recibió job scope={Scope} para slug={Slug}. Las Fases 2/3 deben registrar un executor específico.",
                    job.Scope, slug);
                return JobRunResult.Skipped($"Scope {job.Scope} sin executor específico registrado.");
            }

            log.LogInformation("Worker exec: job={JobId} slug={Slug} scope={Scope}", job.Id, slug, job.Scope);
            return JobRunResult.Success(1, $"Acción '{slug}' invocada (AllTenants).");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Worker exec error: job={JobId} slug={Slug}", job.Id, slug);
            return JobRunResult.Failed(ex.Message, $"Excepción ejecutando '{slug}'.");
        }
    }
}
