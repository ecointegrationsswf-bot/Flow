using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Domain.Webhooks;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Executor genérico (slug = "*") que el Worker usa cuando ningún executor
/// específico declara el slug del ActionDefinition. Invoca al
/// IActionExecutorService existente reutilizando toda la maquinaria del
/// Webhook Contract System (PayloadBuilder + HttpDispatcher + OutputInterpreter)
/// SIN modificarla.
///
/// Routing por scope:
///   - AllTenants     → no aplica (skipped). Las acciones globales se ejecutan
///                      por tenant; este executor no tiene contexto suficiente.
///   - PerCampaign    → futuro. Skipped por ahora.
///   - PerConversation→ ContextId = ConversationId. Resuelve tenantId,
///                      campaignTemplateId y contactPhone desde la conversación
///                      e invoca IActionExecutorService.ExecuteAsync.
///                      Es el camino del webhook de resultado en Fase 3:
///                      el LabelingJob etiqueta → dispara ConversationLabeled →
///                      el WebhookEventDispatcher programa el job →
///                      este executor lo ejecuta usando el InputSchema/OutputSchema
///                      configurados por el admin en el ActionDefinition.
/// </summary>
public class DefaultWebhookExecutor(
    AgentFlowDbContext db,
    IActionExecutorService actionExecutor,
    ILogger<DefaultWebhookExecutor> log) : IScheduledJobExecutor
{
    public string Slug => "*";

    public async Task<JobRunResult> ExecuteAsync(
        ScheduledWebhookJob job, ScheduledJobContext ctx, CancellationToken ct)
    {
        if (job.ActionDefinition is null)
            return JobRunResult.Failed("ActionDefinition no cargada (¿Include faltante en el repo?)");

        var slug = job.ActionDefinition.Name;

        return job.Scope switch
        {
            "PerConversation" => await ExecutePerConversationAsync(slug, ctx, ct),
            "PerCampaign"     => JobRunResult.Skipped($"Scope PerCampaign sin executor específico para '{slug}'."),
            "AllTenants"      => JobRunResult.Skipped($"Scope AllTenants requiere executor específico para '{slug}'."),
            _                 => JobRunResult.Skipped($"Scope desconocido: {job.Scope}."),
        };
    }

    private async Task<JobRunResult> ExecutePerConversationAsync(
        string slug, ScheduledJobContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.ContextId) || !Guid.TryParse(ctx.ContextId, out var conversationId))
            return JobRunResult.Skipped("ContextId requerido (ConversationId).");

        // Resolver datos de la conversación en una sola query.
        var conv = await db.Conversations
            .AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => new
            {
                c.TenantId,
                c.ClientPhone,
                CampaignTemplateId = c.CampaignId == null
                    ? (Guid?)null
                    : db.Campaigns.Where(camp => camp.Id == c.CampaignId)
                        .Select(camp => (Guid?)camp.CampaignTemplateId)
                        .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);

        if (conv is null)
            return JobRunResult.Skipped($"Conversación {conversationId} no existe.");

        try
        {
            var result = await actionExecutor.ExecuteAsync(
                actionSlug: slug,
                tenantId: conv.TenantId,
                campaignTemplateId: conv.CampaignTemplateId,
                contactPhone: conv.ClientPhone,
                conversationId: conversationId,
                collectedParams: new CollectedParams(),
                agentSlug: null,
                ct: ct);

            // ActionResult del Webhook Contract System → JobRunResult del Worker.
            if (result.Success)
            {
                var summary = result.DataForAgent ?? $"Acción '{slug}' ejecutada para conv {conversationId}.";
                if (summary.Length > 800) summary = summary[..800];
                return JobRunResult.Success(1, summary);
            }

            return JobRunResult.Failed(
                result.ErrorMessage ?? "Sin detalle",
                $"Acción '{slug}' falló para conv {conversationId}.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "DefaultWebhookExecutor: error ejecutando '{Slug}' para conv {Conv}.", slug, conversationId);
            return JobRunResult.Failed(ex.Message, $"Excepción ejecutando '{slug}'.");
        }
    }
}
