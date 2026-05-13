using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

public record DuplicateTemplateRequest(string Name);

public record CampaignTemplateRequest(
    string Name, Guid AgentDefinitionId,
    List<int> FollowUpHours, int AutoCloseHours,
    List<Guid> LabelIds,
    bool SendEmail = false, string? EmailAddress = null,
    List<Guid>? ActionIds = null,
    string? ActionConfigs = null,
    List<Guid>? PromptTemplateIds = null,
    string SystemPrompt = "",
    string? SendFrom = null, string? SendUntil = null,
    int MaxRetries = 3, int RetryIntervalHours = 24,
    int InactivityCloseHours = 72, string? CloseConditionKeyword = null,
    int MaxTokens = 1024,
    List<int>? AttentionDays = null,
    string AttentionStartTime = "08:00",
    string AttentionEndTime = "17:00",
    string OutOfContextPolicy = "Contain",
    // Fase 2 — Campaign Automation Worker
    string? FollowUpMessagesJson = null,
    string? AutoCloseMessage = null
    // Fase 3 — el etiquetado IA ya NO se configura aquí. Vive en /admin/scheduled-jobs.
);

[ApiController]
[Route("api/campaign-templates")]
[Authorize]
public class CampaignTemplatesController(ITenantContext tenantCtx, AgentFlowDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var templates = await db.CampaignTemplates
            .Where(t => t.TenantId == tenantId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id, t.Name, t.AgentDefinitionId,
                AgentName = db.AgentDefinitions.Where(a => a.Id == t.AgentDefinitionId).Select(a => a.Name).FirstOrDefault(),
                t.FollowUpHours, t.FollowUpMessagesJson,
                t.AutoCloseHours, t.AutoCloseMessage,
                t.LabelIds,
                t.SendEmail, t.EmailAddress,
                t.ActionIds, t.ActionConfigs, t.PromptTemplateIds,
                t.SystemPrompt, t.SendFrom, t.SendUntil,
                t.MaxRetries, t.RetryIntervalHours, t.InactivityCloseHours,
                t.CloseConditionKeyword, t.MaxTokens,
                t.AttentionDays, t.AttentionStartTime, t.AttentionEndTime,
                t.IsActive, t.CreatedAt, t.UpdatedAt
            })
            .ToListAsync(ct);

        return Ok(templates);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var t = await db.CampaignTemplates
            .Where(x => x.Id == id && x.TenantId == tenantId)
            .Select(x => new
            {
                x.Id, x.Name, x.AgentDefinitionId,
                AgentName = db.AgentDefinitions.Where(a => a.Id == x.AgentDefinitionId).Select(a => a.Name).FirstOrDefault(),
                x.FollowUpHours, x.FollowUpMessagesJson,
                x.AutoCloseHours, x.AutoCloseMessage,
                x.LabelIds,
                x.SendEmail, x.EmailAddress,
                x.ActionIds, x.ActionConfigs, x.PromptTemplateIds,
                x.SystemPrompt, x.SendFrom, x.SendUntil,
                x.MaxRetries, x.RetryIntervalHours, x.InactivityCloseHours,
                x.CloseConditionKeyword, x.MaxTokens,
                x.AttentionDays, x.AttentionStartTime, x.AttentionEndTime,
                OutOfContextPolicy = x.OutOfContextPolicy.ToString(),
                x.IsActive, x.CreatedAt, x.UpdatedAt
            })
            .FirstOrDefaultAsync(ct);

        if (t is null) return NotFound();
        return Ok(t);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CampaignTemplateRequest req,
        [FromQuery] bool confirmSwap,
        CancellationToken ct)
    {
        if (req.ActionConfigs != null && req.ActionConfigs.Length > 50000)
            return BadRequest(new { error = "ActionConfigs excede el tamaño máximo permitido." });

        var tenantId = tenantCtx.TenantId;

        // Validación + swap del maestro primario (no-Brain). Si el agente ya
        // tiene un maestro primario y el admin no confirmó el swap, retorna 409.
        var swap = await ResolvePrimarySwapAsync(
            tenantId, req.AgentDefinitionId, excludeTemplateId: null, confirmSwap, ct);
        if (swap.ConflictResult is not null) return swap.ConflictResult;

        var template = new CampaignTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = req.Name,
            AgentDefinitionId = req.AgentDefinitionId,
            IsPrimaryForAgent = swap.NewTemplateShouldBePrimary,
            FollowUpHours = req.FollowUpHours,
            FollowUpMessagesJson = req.FollowUpMessagesJson,
            AutoCloseHours = req.AutoCloseHours,
            AutoCloseMessage = req.AutoCloseMessage,
            LabelIds = req.LabelIds,
            SendEmail = req.SendEmail,
            EmailAddress = req.EmailAddress,
            ActionIds = req.ActionIds ?? [],
            ActionConfigs = req.ActionConfigs,
            PromptTemplateIds = req.PromptTemplateIds ?? [],
            SystemPrompt = req.SystemPrompt,
            SendFrom = req.SendFrom,
            SendUntil = req.SendUntil,
            MaxRetries = req.MaxRetries,
            RetryIntervalHours = req.RetryIntervalHours,
            InactivityCloseHours = req.InactivityCloseHours,
            CloseConditionKeyword = req.CloseConditionKeyword,
            MaxTokens = req.MaxTokens,
            AttentionDays = req.AttentionDays ?? [1, 2, 3, 4, 5],
            AttentionStartTime = req.AttentionStartTime,
            AttentionEndTime = req.AttentionEndTime,
            OutOfContextPolicy = Enum.TryParse<AgentFlow.Domain.Enums.OutOfContextPolicy>(req.OutOfContextPolicy, out var policy)
                ? policy : AgentFlow.Domain.Enums.OutOfContextPolicy.Contain,
        };

        db.CampaignTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return Ok(new { template.Id, template.Name });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] CampaignTemplateRequest req,
        [FromQuery] bool confirmSwap,
        CancellationToken ct)
    {
        if (req.ActionConfigs != null && req.ActionConfigs.Length > 50000)
            return BadRequest(new { error = "ActionConfigs excede el tamaño máximo permitido." });

        var tenantId = tenantCtx.TenantId;
        var template = await db.CampaignTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);

        if (template is null) return NotFound();

        // Si cambió de agente, revalidar el swap del primario para el nuevo agente.
        if (template.AgentDefinitionId != req.AgentDefinitionId)
        {
            var swap = await ResolvePrimarySwapAsync(
                tenantId, req.AgentDefinitionId, excludeTemplateId: id, confirmSwap, ct);
            if (swap.ConflictResult is not null) return swap.ConflictResult;
            template.IsPrimaryForAgent = swap.NewTemplateShouldBePrimary;
        }

        template.Name = req.Name;
        template.AgentDefinitionId = req.AgentDefinitionId;
        template.FollowUpHours = req.FollowUpHours;
        template.FollowUpMessagesJson = req.FollowUpMessagesJson;
        template.AutoCloseHours = req.AutoCloseHours;
        template.AutoCloseMessage = req.AutoCloseMessage;
        template.LabelIds = req.LabelIds;
        template.SendEmail = req.SendEmail;
        template.EmailAddress = req.EmailAddress;
        template.ActionIds = req.ActionIds ?? [];
        template.ActionConfigs = req.ActionConfigs;
        template.PromptTemplateIds = req.PromptTemplateIds ?? [];
        template.SystemPrompt = req.SystemPrompt;
        template.SendFrom = req.SendFrom;
        template.SendUntil = req.SendUntil;
        template.MaxRetries = req.MaxRetries;
        template.RetryIntervalHours = req.RetryIntervalHours;
        template.InactivityCloseHours = req.InactivityCloseHours;
        template.CloseConditionKeyword = req.CloseConditionKeyword;
        template.MaxTokens = req.MaxTokens;
        template.AttentionDays = req.AttentionDays ?? [1, 2, 3, 4, 5];
        template.AttentionStartTime = req.AttentionStartTime;
        template.AttentionEndTime = req.AttentionEndTime;
        template.OutOfContextPolicy = Enum.TryParse<AgentFlow.Domain.Enums.OutOfContextPolicy>(req.OutOfContextPolicy, out var pol)
            ? pol : AgentFlow.Domain.Enums.OutOfContextPolicy.Contain;
        template.UpdatedAt = DateTime.UtcNow;

        // HasConversion en List<int> no tiene ValueComparer — marcar explícitamente como modificado
        db.Entry(template).Property(t => t.AttentionDays).IsModified = true;
        db.Entry(template).Property(t => t.AttentionStartTime).IsModified = true;
        db.Entry(template).Property(t => t.AttentionEndTime).IsModified = true;

        await db.SaveChangesAsync(ct);
        return Ok(new { template.Id, template.Name });
    }

    /// <summary>
    /// Lista las acciones activas asignadas explícitamente a este tenant por el super admin
    /// (Tenant.AssignedActionIds). Sin fallback: si el admin no asignó nada, devuelve vacío.
    /// La validación 409 al desasignar (SuperAdminController) protege el caso de maestros
    /// en uso, así que en flujo normal nunca habrá un template referenciando un ID no asignado.
    /// </summary>
    [HttpGet("available-actions")]
    public async Task<IActionResult> AvailableActions(CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.AssignedActionIds })
            .FirstOrDefaultAsync(ct);

        var assignedIds = tenant?.AssignedActionIds ?? [];
        if (assignedIds.Count == 0)
            return Ok(Array.Empty<object>());

        var actions = await db.Set<ActionDefinition>()
            .Where(a => a.IsActive && assignedIds.Contains(a.Id))
            .OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Name, a.Description, a.RequiresWebhook, a.SendsEmail, a.SendsSms, a.DefaultTriggerConfig, a.DefaultWebhookContract })
            .ToListAsync(ct);

        return Ok(actions);
    }

    /// <summary>
    /// Lista los prompt templates activos asignados explícitamente a este tenant
    /// por el super admin (Tenant.AssignedPromptIds). Sin fallback.
    /// </summary>
    [HttpGet("available-prompts")]
    public async Task<IActionResult> AvailablePrompts(CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.AssignedPromptIds })
            .FirstOrDefaultAsync(ct);

        var assignedIds = tenant?.AssignedPromptIds ?? [];
        if (assignedIds.Count == 0)
            return Ok(Array.Empty<object>());

        var prompts = await db.Set<PromptTemplate>()
            .Where(p => p.IsActive && assignedIds.Contains(p.Id))
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Description, CategoryName = p.Category != null ? p.Category.Name : null })
            .ToListAsync(ct);

        return Ok(prompts);
    }

    /// <summary>
    /// Devuelve el texto completo (SystemPrompt) de un prompt template para que la UI
    /// del maestro pueda copiarlo al campo editable local (CampaignTemplate.SystemPrompt).
    /// Sólo accesible si está asignado al tenant.
    /// </summary>
    [HttpGet("available-prompts/{id:guid}")]
    public async Task<IActionResult> AvailablePromptDetail(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.AssignedPromptIds })
            .FirstOrDefaultAsync(ct);

        var assignedIds = tenant?.AssignedPromptIds ?? [];
        if (!assignedIds.Contains(id))
            return NotFound(new { error = "Prompt no asignado a este tenant." });

        var prompt = await db.Set<PromptTemplate>()
            .Where(p => p.IsActive && p.Id == id)
            .Select(p => new { p.Id, p.Name, p.Description, p.SystemPrompt })
            .FirstOrDefaultAsync(ct);

        if (prompt is null) return NotFound();
        return Ok(prompt);
    }

    /// <summary>Duplica un maestro de campaña existente con un nuevo nombre.</summary>
    [HttpPost("{id:guid}/duplicate")]
    public async Task<IActionResult> Duplicate(Guid id, [FromBody] DuplicateTemplateRequest req, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var original = await db.CampaignTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);

        if (original is null) return NotFound();

        // El duplicado nace SIEMPRE como secundario — preserva al original como
        // primario. Si el admin quiere que el duplicado sea el primario, debe
        // editar el duplicado y confirmar el swap explícitamente.
        var copy = new CampaignTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = req.Name.Trim(),
            AgentDefinitionId = original.AgentDefinitionId,
            IsPrimaryForAgent = false,
            FollowUpHours = [.. original.FollowUpHours],
            FollowUpMessagesJson = original.FollowUpMessagesJson,
            AutoCloseHours = original.AutoCloseHours,
            AutoCloseMessage = original.AutoCloseMessage,
            LabelIds = [.. original.LabelIds],
            SendEmail = original.SendEmail,
            EmailAddress = original.EmailAddress,
            ActionIds = [.. original.ActionIds],
            ActionConfigs = original.ActionConfigs,
            PromptTemplateIds = [.. original.PromptTemplateIds],
            SystemPrompt = original.SystemPrompt,
            SendFrom = original.SendFrom,
            SendUntil = original.SendUntil,
            MaxRetries = original.MaxRetries,
            RetryIntervalHours = original.RetryIntervalHours,
            InactivityCloseHours = original.InactivityCloseHours,
            CloseConditionKeyword = original.CloseConditionKeyword,
            MaxTokens = original.MaxTokens,
            AttentionDays = [.. original.AttentionDays],
            AttentionStartTime = original.AttentionStartTime,
            AttentionEndTime = original.AttentionEndTime,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.CampaignTemplates.Add(copy);
        await db.SaveChangesAsync(ct);
        return Ok(new { copy.Id, copy.Name });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var template = await db.CampaignTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);

        if (template is null) return NotFound();

        db.CampaignTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Maestro eliminado." });
    }

    /// <summary>
    /// Helper de Create/Update — decide si el nuevo/editado maestro puede ser
    /// primario para el agente. Reglas:
    ///   • Si el tenant tiene BrainEnabled=true → no hay primario obligatorio;
    ///     dejamos IsPrimaryForAgent=false (el Cerebro elige por slug).
    ///   • Si el agente NO tiene primario activo → este pasa a ser primario.
    ///   • Si el agente YA tiene un primario activo:
    ///       - Sin confirmSwap → 409 con detalles del maestro actual para que el
    ///         frontend muestre la modal de confirmación.
    ///       - Con confirmSwap=true → bajamos al primario actual a no-primario
    ///         (sólo el flag, sin desactivar) y este pasa a primario.
    /// </summary>
    private async Task<(IActionResult? ConflictResult, bool NewTemplateShouldBePrimary)>
        ResolvePrimarySwapAsync(
            Guid tenantId, Guid agentDefinitionId, Guid? excludeTemplateId,
            bool confirmSwap, CancellationToken ct)
    {
        var brainEnabled = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.BrainEnabled)
            .FirstOrDefaultAsync(ct);

        if (brainEnabled)
        {
            // Brain decide por slug — el flag IsPrimaryForAgent no se usa.
            return (null, NewTemplateShouldBePrimary: false);
        }

        var currentPrimaryQuery = db.CampaignTemplates
            .Where(t => t.TenantId == tenantId
                     && t.AgentDefinitionId == agentDefinitionId
                     && t.IsActive
                     && t.IsPrimaryForAgent);

        if (excludeTemplateId.HasValue)
            currentPrimaryQuery = currentPrimaryQuery.Where(t => t.Id != excludeTemplateId.Value);

        var currentPrimary = await currentPrimaryQuery.FirstOrDefaultAsync(ct);

        if (currentPrimary is null)
        {
            // El agente no tiene primario activo → el nuevo asume el rol.
            return (null, NewTemplateShouldBePrimary: true);
        }

        // Hay otro primario. Pedimos confirmación o aplicamos swap.
        if (!confirmSwap)
        {
            var conflict = Conflict(new
            {
                error = "primary_template_swap_required",
                message = $"El agente ya tiene un maestro primario asignado: \"{currentPrimary.Name}\". "
                        + "Si continúas, ese maestro perderá el rol primario y este pasará a ser el que "
                        + "responde a los mensajes orgánicos (sin campaña activa). Las campañas vivas "
                        + "del maestro anterior seguirán funcionando sin cambios.",
                currentPrimaryId = currentPrimary.Id,
                currentPrimaryName = currentPrimary.Name,
                hint = "Reintenta con ?confirmSwap=true para aplicar el cambio."
            });
            return (conflict, false);
        }

        // confirmSwap=true → swap atómico (todo en el mismo SaveChanges del caller).
        currentPrimary.IsPrimaryForAgent = false;
        currentPrimary.UpdatedAt = DateTime.UtcNow;
        return (null, NewTemplateShouldBePrimary: true);
    }
}
