using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

public record CampaignTemplateRequest(
    string Name, Guid AgentDefinitionId,
    List<int> FollowUpHours, int AutoCloseHours,
    List<Guid> LabelIds,
    bool SendEmail = false, string? EmailAddress = null,
    List<Guid>? ActionIds = null,
    string? ActionConfigs = null,
    List<Guid>? PromptTemplateIds = null
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
                t.FollowUpHours, t.AutoCloseHours, t.LabelIds,
                t.SendEmail, t.EmailAddress,
                t.ActionIds, t.ActionConfigs, t.PromptTemplateIds,
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
                x.FollowUpHours, x.AutoCloseHours, x.LabelIds,
                x.SendEmail, x.EmailAddress,
                x.ActionIds, x.PromptTemplateIds,
                x.IsActive, x.CreatedAt, x.UpdatedAt
            })
            .FirstOrDefaultAsync(ct);

        if (t is null) return NotFound();
        return Ok(t);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CampaignTemplateRequest req, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var template = new CampaignTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = req.Name,
            AgentDefinitionId = req.AgentDefinitionId,
            FollowUpHours = req.FollowUpHours,
            AutoCloseHours = req.AutoCloseHours,
            LabelIds = req.LabelIds,
            SendEmail = req.SendEmail,
            EmailAddress = req.EmailAddress,
            ActionIds = req.ActionIds ?? [],
            ActionConfigs = req.ActionConfigs,
            PromptTemplateIds = req.PromptTemplateIds ?? [],
        };

        db.CampaignTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return Ok(new { template.Id, template.Name });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CampaignTemplateRequest req, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var template = await db.CampaignTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);

        if (template is null) return NotFound();

        template.Name = req.Name;
        template.AgentDefinitionId = req.AgentDefinitionId;
        template.FollowUpHours = req.FollowUpHours;
        template.AutoCloseHours = req.AutoCloseHours;
        template.LabelIds = req.LabelIds;
        template.SendEmail = req.SendEmail;
        template.EmailAddress = req.EmailAddress;
        template.ActionIds = req.ActionIds ?? [];
        template.ActionConfigs = req.ActionConfigs;
        template.PromptTemplateIds = req.PromptTemplateIds ?? [];
        template.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return Ok(new { template.Id, template.Name });
    }

    /// <summary>Lista las acciones activas (globales, definidas en admin) para vincular a maestros.</summary>
    [HttpGet("available-actions")]
    public async Task<IActionResult> AvailableActions(CancellationToken ct)
    {
        var actions = await db.Set<ActionDefinition>()
            .Where(a => a.IsActive)
            .OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Name, a.Description, a.RequiresWebhook, a.SendsEmail, a.SendsSms })
            .ToListAsync(ct);

        return Ok(actions);
    }

    /// <summary>Lista los prompt templates activos (globales, definidos en admin) para vincular a maestros.</summary>
    [HttpGet("available-prompts")]
    public async Task<IActionResult> AvailablePrompts(CancellationToken ct)
    {
        var prompts = await db.Set<PromptTemplate>()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Description, CategoryName = p.Category != null ? p.Category.Name : null })
            .ToListAsync(ct);

        return Ok(prompts);
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
}
