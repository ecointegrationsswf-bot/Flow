using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

public record AgentRegistryRequest(
    string Slug, string Name, string Capabilities,
    Guid CampaignTemplateId, bool IsWelcome = false);

[ApiController]
[Route("api/agent-registry")]
[Authorize]
public class AgentRegistryController(ITenantContext tenantCtx, AgentFlowDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var entries = await db.AgentRegistryEntries
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id, r.Slug, r.Name, r.Capabilities,
                r.CampaignTemplateId,
                CampaignTemplateName = db.CampaignTemplates.Where(ct2 => ct2.Id == r.CampaignTemplateId).Select(ct2 => ct2.Name).FirstOrDefault(),
                AgentName = db.CampaignTemplates.Where(ct2 => ct2.Id == r.CampaignTemplateId)
                    .Select(ct2 => db.AgentDefinitions.Where(a => a.Id == ct2.AgentDefinitionId).Select(a => a.Name).FirstOrDefault()).FirstOrDefault(),
                r.IsWelcome, r.IsActive, r.CreatedAt
            })
            .ToListAsync(ct);
        return Ok(entries);
    }

    [HttpGet("available-templates")]
    public async Task<IActionResult> AvailableTemplates(CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var templates = await db.CampaignTemplates
            .Where(t => t.TenantId == tenantId && t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new
            {
                t.Id, t.Name,
                AgentName = db.AgentDefinitions.Where(a => a.Id == t.AgentDefinitionId).Select(a => a.Name).FirstOrDefault(),
                LabelCount = t.LabelIds.Count
            })
            .ToListAsync(ct);
        return Ok(templates);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AgentRegistryRequest req, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        if (await db.AgentRegistryEntries.AnyAsync(r => r.TenantId == tenantId && r.Slug == req.Slug, ct))
            return BadRequest(new { error = "Ya existe un agente con ese slug." });

        if (!await db.CampaignTemplates.AnyAsync(t => t.Id == req.CampaignTemplateId && t.TenantId == tenantId, ct))
            return BadRequest(new { error = "Maestro de campaña no encontrado." });

        if (await db.AgentRegistryEntries.AnyAsync(r => r.TenantId == tenantId && r.CampaignTemplateId == req.CampaignTemplateId, ct))
        {
            var existingSlug = await db.AgentRegistryEntries
                .Where(r => r.TenantId == tenantId && r.CampaignTemplateId == req.CampaignTemplateId)
                .Select(r => r.Slug)
                .FirstOrDefaultAsync(ct);
            return BadRequest(new { error = $"Este maestro ya esta registrado bajo el slug '{existingSlug}'.", duplicateSlug = existingSlug });
        }

        if (req.IsWelcome && await db.AgentRegistryEntries.AnyAsync(r => r.TenantId == tenantId && r.IsWelcome, ct))
        {
            var welcomeSlug = await db.AgentRegistryEntries
                .Where(r => r.TenantId == tenantId && r.IsWelcome)
                .Select(r => r.Slug).FirstOrDefaultAsync(ct);
            return BadRequest(new { error = $"Ya existe un Agente Welcome registrado (slug: {welcomeSlug}). Desactivalo primero." });
        }

        var entry = new AgentRegistryEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Slug = req.Slug,
            Name = req.Name,
            Capabilities = req.Capabilities,
            CampaignTemplateId = req.CampaignTemplateId,
            IsWelcome = req.IsWelcome,
        };
        db.AgentRegistryEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return Ok(new { entry.Id, entry.Slug, entry.Name });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AgentRegistryRequest req, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var entry = await db.AgentRegistryEntries.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (entry is null) return NotFound();

        if (await db.AgentRegistryEntries.AnyAsync(r => r.TenantId == tenantId && r.Slug == req.Slug && r.Id != id, ct))
            return BadRequest(new { error = "Ya existe un agente con ese slug." });

        if (req.CampaignTemplateId != entry.CampaignTemplateId
            && await db.AgentRegistryEntries.AnyAsync(r => r.TenantId == tenantId && r.CampaignTemplateId == req.CampaignTemplateId && r.Id != id, ct))
        {
            var existingSlug = await db.AgentRegistryEntries
                .Where(r => r.TenantId == tenantId && r.CampaignTemplateId == req.CampaignTemplateId)
                .Select(r => r.Slug).FirstOrDefaultAsync(ct);
            return BadRequest(new { error = $"Este maestro ya esta registrado bajo el slug '{existingSlug}'." });
        }

        if (req.IsWelcome && !entry.IsWelcome
            && await db.AgentRegistryEntries.AnyAsync(r => r.TenantId == tenantId && r.IsWelcome && r.Id != id, ct))
        {
            var welcomeSlug = await db.AgentRegistryEntries
                .Where(r => r.TenantId == tenantId && r.IsWelcome && r.Id != id)
                .Select(r => r.Slug).FirstOrDefaultAsync(ct);
            return BadRequest(new { error = $"Ya existe un Agente Welcome (slug: {welcomeSlug}). Desactivalo primero." });
        }

        entry.Slug = req.Slug;
        entry.Name = req.Name;
        entry.Capabilities = req.Capabilities;
        entry.CampaignTemplateId = req.CampaignTemplateId;
        entry.IsWelcome = req.IsWelcome;
        entry.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { entry.Id, entry.Slug, entry.Name });
    }

    [HttpPut("{id:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var entry = await db.AgentRegistryEntries.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (entry is null) return NotFound();
        entry.IsActive = !entry.IsActive;
        entry.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { entry.Id, entry.IsActive });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var entry = await db.AgentRegistryEntries.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (entry is null) return NotFound();
        db.AgentRegistryEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
