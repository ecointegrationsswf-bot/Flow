using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Domain.Provisioning;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

public record RegenerateTemplateDto(string? Objetivo);

/// <summary>
/// Integración Ludo CRM — Fase 3. Regeneración on-demand del maestro de campaña con el LLM.
/// Controller NUEVO y aditivo (no modifica CampaignTemplatesController).
///
/// <para><b>Versionado seguro:</b> la regeneración NUNCA pisa un maestro en producción. Si el
/// target ya está activo (IsActive=true), se crea un BORRADOR nuevo con el prompt regenerado y
/// el activo queda intacto. Si el target ya es borrador, se actualiza en sitio. El Admin compara
/// y activa.</para>
/// </summary>
[ApiController]
[Route("api/campaign-templates")]
[Authorize]
public class CampaignTemplateGenerationController(
    ITenantContext tenantCtx,
    AgentFlowDbContext db,
    ICampaignTemplateGenerator generator) : ControllerBase
{
    [HttpPost("{id:guid}/regenerate")]
    public async Task<IActionResult> Regenerate(Guid id, [FromBody] RegenerateTemplateDto dto, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        if (tenantId == Guid.Empty)
            return Unauthorized(new { error = "Tenant no identificado." });

        var template = await db.CampaignTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);
        if (template is null) return NotFound();

        var objetivo = !string.IsNullOrWhiteSpace(dto.Objetivo) ? dto.Objetivo! : template.Objetivo;
        if (string.IsNullOrWhiteSpace(objetivo))
            return BadRequest(new { error = "Se requiere un objetivo para regenerar el maestro." });

        var agent = await db.AgentDefinitions
            .FirstOrDefaultAsync(a => a.Id == template.AgentDefinitionId && a.TenantId == tenantId, ct);
        var agentSlug = agent?.Name ?? "agente";

        var tipoNegocio = await db.LudoTenantMaps
            .Where(m => m.TenantId == tenantId)
            .Select(m => m.TipoNegocio)
            .FirstOrDefaultAsync(ct) ?? "general";

        var etapas = await db.StageLabelMaps
            .Where(s => s.TenantId == tenantId && s.IsActive)
            .OrderBy(s => s.Orden)
            .Select(s => new StageInfo(s.Nombre, s.Orden))
            .ToListAsync(ct);

        var tenantApiKey = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.LlmApiKey)
            .FirstOrDefaultAsync(ct);

        var gen = await generator.GenerateAsync(
            new GenerateTemplateRequest(tipoNegocio, agentSlug, objetivo!, etapas), tenantApiKey, ct);

        bool createdNew;
        CampaignTemplate target;
        if (template.IsActive)
        {
            // El maestro está en producción: NO se pisa. Se crea un borrador nuevo (versión).
            target = new CampaignTemplate
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                AgentDefinitionId = template.AgentDefinitionId,
                Name = $"{template.Name} (regenerado)",
                Objetivo = objetivo,
                GeneratedByLlm = gen.UsedLlm,
                SystemPrompt = gen.SystemPrompt,
                IsActive = false,           // BORRADOR
                IsPrimaryForAgent = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.CampaignTemplates.Add(target);
            createdNew = true;
        }
        else
        {
            // Ya es borrador: se actualiza en sitio.
            template.Objetivo = objetivo;
            template.SystemPrompt = gen.SystemPrompt;
            template.GeneratedByLlm = gen.UsedLlm;
            template.UpdatedAt = DateTime.UtcNow;
            target = template;
            createdNew = false;
        }

        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            templateId = target.Id,
            createdNewDraft = createdNew,
            usedLlm = gen.UsedLlm,
            objetivo,
            systemPromptPreview = target.SystemPrompt.Length > 600
                ? target.SystemPrompt[..600] + "…"
                : target.SystemPrompt,
            suggestedActionSlugs = gen.SuggestedActionSlugs,
            stageCriteria = gen.StageCriteria.Select(c => new { c.Etapa, c.Criterio }),
        });
    }
}
